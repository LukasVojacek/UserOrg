# Location kill-switch is a read-time lens applied at partition grain, never a stored-state overwrite

## Context

A provider admin needs to freeze all HEAppE command templates on one Location
during a security incident, and un-freeze instantly afterwards, without losing the
original Active/Inactive states operators set before the incident.

Facts established from the code:

- Stored Active/Inactive state lives **only** on `ProjectCommandTemplatePartition.State`
  (`ProjectCommandTemplatePartitionConfiguration.cs:27`). `ProjectCommandTemplate.State`
  is a computed rollup (`ProjectCommandTemplate.cs:11`, `GetCommandTemplateState()`),
  EF-`Ignore`d (`ProjectCommandTemplateConfiguration.cs:41`).
- A command template is **1:many** with Location: one `ProjectCommandTemplate`
  (keyed `(ProjectId, ProjectResourceId, Name)`) owns N partitions, each pointing at
  one `LocationPartition` carrying a `LocationId`. So a **partition is `(template ×
  location)`** — the natural grain of "this template on this location."
- HEAppE enforces launch by calling `GET /api/Heappe/CommandTemplatePermissions/{id}`
  and honouring the per-row `Enabled` flag (`CommandTemplateModel(Name, Enabled)`,
  `CommandTemplatePermissionQueryHandler.cs:198`). It already handles `Enabled=false`
  rows for Inactive templates. So we never need to call HEAppE to disable anything —
  our output returning `Enabled=false` is sufficient.
- The existing `isExpired` read path degrades a template's *response* enum without
  writing the DB (`ECommandTemplateStateExtensions.ToResponseType`). The kill-switch is
  the same idea: a read-time transform, not a write.

## Decision

**The kill-switch degrades the *effective* (read) state of a command-template partition
whose Location is frozen; the stored `ProjectCommandTemplatePartition.State` is never
touched. The degrade is applied at partition grain, keyed on the partition's
`LocationPartition.LocationId`, so a template stays usable on non-frozen locations.**

1. **New tables in the Heappe module** (`heappe_integration` schema, added to the
   existing `HeappeIntegrationDbContext` — already registered in both `DatabaseMigrations`
   context lists, so no dual-list change):
   - `LocationCommandTemplateKillSwitch` — PK `LocationId` (no FK, module convention),
     `IsEnforced`, `Reason` (required, min 10 chars), `EnforcedAtUtc`, `EnforcedByUserId`,
     `LiftedAtUtc?`, `LiftedByUserId?`. One row per Location, overwritten (last-write-wins).
   - `LocationCommandTemplateWhitelist` — stored by **internal id, not name**:
     `(LocationId, ProjectId, ProjectResourceId, CommandTemplateName)` (or an equivalent
     `(LocationId, ProjectCommandTemplateId)`). The API **contract** accepts names
     (`projectShortName, resourceName, templateName`); the PUT resolves them to ids **once**
     and stores by id. Rationale: the Heappe module holds only GUIDs on a partition — project
     and resource *names* are Core-only (`.ai/relationship-map.md` §D). Storing by id lets the
     three read-path degrades match the whitelist against the GUIDs a partition already
     carries **in-module, with no Core call in the hot enforcement path**. Whitelisting a
     template on a location covers all its aggregation partitions there (whitelist ignores
     `AggregationName` — the admin whitelists a template on a location, not a queue).

2. **A partition is effectively killed** when its `LocationPartition.LocationId` has an
   enforced kill-switch AND the `(location, project, resource, template)` tuple is not
   whitelisted. Effective `Enabled = State == Active && !killed`.

3. **All three read paths apply the degrade at partition grain**, so each path's existing
   aggregation yields correct per-location behaviour:
   - **HEAppE permissions** (`CommandTemplatePermissionQueryHandler`) — already partition
     grain with `LocationId` in scope. Row stays; `Enabled=false` when killed. This is the
     only path that blocks launches; the contract shape does not change.
   - **v1 `TemplatesInfo`** via `HeappeService.GetActiveCommandTemplatesAsync:538` — extend
     the `State==Active` filter with `&& !killed(partition)`. A template with a still-active
     partition on a non-frozen location survives; it disappears only when all its partitions
     are inactive/killed.
   - **UI `GET /CommandTemplate/{proj}/{res}`** — make the `GetCommandTemplateState()`
     rollup kill-aware; add `frozen: true` when any of the template's partitions is on a
     frozen location. **`reason` is never exposed here** (see Consequences).

4. **Kill-switch + whitelist are loaded inside the cached permissions composition**, and
   a single global cache tag is added to every permission entry. `HybridCache` already
   supports tags (`HeappeCacheKeys`), but no global tag exists yet — add one constant tag
   to the producer `tags:` array (`CommandTemplatePermissionQueryHandler.cs:82`). PUT
   evicts that one tag (`RemoveByTagAsync`) → all permission entries drop at once. TTL
   stays 5 minutes. One tag, not N per-project tags: no partial-eviction hole during an
   incident.

   **The tag must cover EVERY cache the kill-switch degrades — not just permissions.**
   §3's read paths are cached in more than one place: the HEAppE permissions entry (Heappe
   module) *and* the v1 `TemplatesInfo` response, which Core caches separately
   (`ProjectService`, `CacheTags.TemplatesInfo(...)`, 5-min TTL). A tag attached by only one
   producer leaves the other stale — enforcing would freeze launches while v1 kept
   advertising the template as active for the full TTL. The tag therefore lives in the
   shared `UserOrg.Shared/CacheTags.cs` (`CacheTags.KillSwitchGlobal`, referenced by both
   Core and the module — `HeappeCacheKeys.KillSwitchGlobalTag` aliases it so the literal
   exists once), and BOTH producers attach it. **Accepted cost:** one global tag means an
   incident on one location evicts every project's cached permissions and TemplatesInfo, not
   just the affected ones. That over-eviction is deliberate — it is rare (incident-scoped),
   single-flighted per key, and strictly better than the partial-eviction hole N per-project
   tags would open at exactly the wrong moment.

5. **Write paths on a frozen location return `409 Conflict`** (state, not permission):
   `PUT /CommandTemplate/Activate`, `.../Deactivate`, `POST /CommandTemplates`. Message
   `Command templates on location '{name}' are frozen by an active kill-switch (since
   {enforcedAt})` — **without reason**. Blocking Deactivate is deliberate: otherwise a
   ProjectOwner could turn a template off mid-incident and it would stay off after the
   lift, breaking the "stored state untouched" guarantee.

6. **`POST /CommandTemplate/Synchronization` skips a frozen location** (Warning log),
   syncs the resource's other locations.

7. **Lift (`isEnforced=false`)** deletes the whitelist rows and enqueues a resync per
   `(project, resource)` partition on the lifted Location via the existing
   `Channel<WorkItem>` (`HeappeIntegrationExtensions.cs:178`) using **`TryWrite`** (never
   blocking `WriteAsync` — the channel is bounded 1000 `FullMode.Wait`). Overflow →
   Warning log + list the unqueued items in the response so the admin can re-run.

   **This resync runs `ProcessAllocationExpiration` first** (ADR-0083), so a lift immediately
   materialises any expiry that came due during the freeze — writing stored `State`. That is
   deliberate and must not be "fixed" by reordering or by making expiry freeze-aware; ADR-0083
   explains why deferring a latching state change is strictly worse. Named here, at the point of
   decision, because the Consequences below promise a non-destructive lift and the two must be
   read together.

## Consequences

- **The freeze itself is non-destructive, and that is the guarantee.** Enforcing never writes
  stored `State`; lifting is a state flip + cache evict, and nothing is "restored" because the
  kill-switch overwrote nothing. Test #5 (stored `State` bit-identical before/after a rejected
  call) pins this — it is the whole point of the design. Confirmed against a live database: while
  enforced, stored state was unchanged (51 Active / 1 Inactive) while effective state degraded
  (51 Frozen / 1 Active, the survivor being the whitelisted template).

  **The guarantee covers the kill-switch's own writes — not the lift's downstream effects.**
  §7's lift enqueues a resync, and the sync applies allocation expiry (ADR-0083), which *does*
  write stored `State`. Lifting a location whose allocations expired during the freeze therefore
  deactivates those templates — observed live: a lift took a location from 51 Active / 1 Inactive
  to 45 Inactive / 7 Active, because the resync ran expiry on an already-expired allocation. This
  is not the kill-switch overwriting anything: the background sync would have applied the same
  expiry within its normal interval, and the lift only pulled it forward. But "lifting restores
  the previous behaviour" is true only when nothing else changed underneath in the meantime, and
  expiry is exactly such a thing. Do not state the lift guarantee without this caveat.
- **Per-location containment.** An incident on location A does not take down the same
  template on locations B, C. This falls out of the partition-grain degrade for free.
- **`reason` is admin-only.** It may describe an unpatched vulnerability ("RCE in
  BulkCopy") — a exploit hint until fixed. `reason`/`enforcedBy`/`enforcedAt` are returned
  only by the KillSwitch endpoints (admin). ProjectOwner sees `frozen`; HEAppE gets neither
  (it only needs `Enabled`).
- **The degrade lives in three query sites.** Test #1 (permissions degrade, row retained)
  guards the enforcement path against a mapper refactor silently dropping it — the one path
  that actually stops launches.
- **No HEAppE call, no running-job cancellation** (out of scope): the kill-switch stops
  *new* launches via the permissions gate; running jobs are the provider's to stop in
  HEAppE directly.
