# Provider-wide kill-switch is a fast fan-out over the provider's current locations

## Context

Beyond freezing one Location, a provider admin needs a **single fast action** to freeze
**all** command templates across their whole provider during a provider-wide incident
(compromised credentials, a shared vulnerability). The Location kill-switch (ADR-0077) is
per-Location; this adds a provider-scoped bulk operation on top of it.

Relationship facts that shaped the mechanism (`.ai/relationship-map.md`):

- `Provider 1:N Location` via non-nullable `Location.ProviderId` — a provider's locations
  are enumerable exactly via `ILocationService.GetLocationIdsForProviderIdAsync(providerId)`
  (`LocationRepository.cs:56`).
- The Heappe module holds only `LocationId` (no `ProviderId`, no Core reference). The kill
  degrade already keys on `LocationId` (partition → `LocationPartition.LocationId`), so a
  per-location kill row covers all a location's partitions/aggregations.
- Resolving `LocationId → ProviderId` inside the hot read path is NOT cheap (module has no
  ProviderId; would need a new cross-module call in the enforcement path). Resolving
  `Provider → LocationIds` at enforce time IS cheap and already exists.

A read-time "provider lens" (a `ProviderId`-keyed row checked in every read path) was
rejected: it drags provider resolution into the enforcement gate (ADR-0077's hot path) for
a case the fan-out handles at enforce time.

## Decision

**A provider-wide kill is a bulk fan-out over the provider's current locations at enforce
time, reusing the per-Location kill table. The three read/write/sync paths are untouched —
they still see only per-`LocationId` kill rows. Provider-wide is a FAST action: no whitelist.**

1. **New route** `PUT /api/Heappe/KillSwitch/{providerName}` (no location segment), same
   perms as the location routes (`perm:provider:admin` + `perm:platform:admin`). Body:
   `{ isEnforced, reason }` — **no whitelist** (see §4). The location route
   `PUT /KillSwitch/{providerName}/{locationName}` is unchanged and keeps its whitelist.

2. **Enforce** (`isEnforced=true`): resolve `providerName → ProviderId`, enumerate the
   provider's current `LocationIds`, and **upsert one `LocationCommandTemplateKillSwitch`
   row per location** (same `reason`/`enforcedBy`/`enforcedAt`), last-write-wins. It **also
   DELETES every per-location whitelist row across those locations** (see §2a). Then evict
   the global cache tag once (ADR-0077 §4).

2a. **Provider-wide enforce clears pre-existing per-location whitelists.** Without this, a
   whitelist row left over from an EARLIER, narrower incident survives the provider-wide
   freeze, and a template the admin believes is now frozen keeps running. "Freeze the whole
   provider now" must mean *everything* — a fast action whose blast radius is silently
   reduced by forgotten state from a previous incident is worse than no button at all. This
   composes with §4: the fast path freezes everything unconditionally, and exceptions are
   then re-added deliberately, per-location, by a second request. Clearing on enforce (not
   just on lift) is what makes the fast path's promise true.

3. **Lift** (`isEnforced=false`): **lift-all** — un-enforce **every** current location of the
   provider and delete its per-location whitelist rows, evict the global cache tag, and
   enqueue a resync per `(project, resource)` partition across all those locations via the
   existing `Channel<WorkItem>` with `TryWrite` (overflow → Warning + list unqueued, per
   ADR-0077 §7). **"Un-enforce" means UPDATE the row (`IsEnforced=false` + `LiftedAtUtc`/
   `LiftedByUserId`), NOT delete it** — ADR-0077 §1's `LiftedAtUtc`/`LiftedByUserId` columns
   exist precisely to survive a lift and record who lifted it, and B02's rule already treats
   `IsEnforced=false` as not-killed. (An earlier draft of this ADR said "remove the kill",
   which was loose wording; the audit row is retained.) The **whitelist** rows, by contrast,
   ARE deleted — ADR-0077 §7 — because B02 consults them with no recency guard, so leftovers
   would silently exempt templates on a later re-freeze. Lift-all is deliberate and
   consistent with the issue's last-write-wins model: a provider-wide lift is the latest
   write and clears any location kill on that provider; the admin re-applies a specific
   location kill if still needed.

4. **No whitelist on the provider route — by design.** Provider-wide is the fastest incident
   action ("freeze the whole provider now"). Exceptions are handled *afterward* with
   per-location `PUT /KillSwitch/{providerName}/{locationName}` requests carrying a whitelist
   for the specific locations that must keep a template running. Keeping the fast path
   whitelist-free means it has nothing to validate and cannot fail on a bad whitelist entry.

5. **`enforcedBy` is stored but NEVER exposed in any response.** The audit columns
   (`EnforcedByUserId`/`LiftedByUserId`, ADR-0077 §1) are retained for the record, but no
   endpoint returns them. Surfacing the raw value would put a DB `Guid` on a contract —
   forbidden (natural keys only) — and resolving it to a username costs another Core lookup
   for a field no acceptance criterion asks for. Admin responses carry `reason` + `enforcedAt`
   only. This supersedes ADR-0077's Consequences wording, which listed `enforcedBy` among the
   admin-visible fields. If it is ever surfaced, it must be an IdP username, never the Guid.

5. **Two new `IUserorgService` host-contract methods** (Heappe module has no Core ref; Core
   adapter injects `ILocationService` + a provider lookup):
   - `providerName → ProviderId` — **exact** match; ambiguous or missing name → **409**
     (fail-closed, mirroring the `Location.Name` ambiguity rule in ADR-0080; neither
     `Provider.Name` nor `Location.Name` has a unique index).
   - `ProviderId → IList<LocationId>` — wraps `GetLocationIdsForProviderIdAsync`.

6. **Provider scoping** (ADR-0080): provider admin may target only their own provider
   (resolved `ProviderId == caller.ProviderId`), else **403**. Platform admin → any provider.

## Consequences

- **Read/write/sync paths unchanged.** All provider logic is isolated in the endpoint block;
  the enforcement gate, v1 filter, UI rollup, 409 guards, and sync skip keep checking only
  per-`LocationId` kill rows. Smallest possible blast radius on the hot paths.
- **Snapshot semantics (accepted).** Fan-out captures the provider's locations *at enforce
  time*. A location added to the provider **after** a provider-wide kill is not auto-frozen;
  the admin re-runs the provider-wide PUT. Acceptable for an incident where the admin knows
  the provider's locations; must be documented, not silent.
- **Lift-all can clear an independent location kill.** If a location had its own kill and a
  later provider-wide lift runs, that location is lifted too (last-write-wins). This is the
  documented, consistent behaviour, not a bug.
- **Provider-wide resync is a large fan-out.** Many `(project, resource)` items may enqueue
  on lift; the bounded channel's overflow path (Warning + list unqueued in the response)
  matters more here than for a single location — the admin must be able to see and re-run
  what didn't enqueue.
