# Change detection via an in-memory SHA breadcrumb map with cold rebuild

**Status:** accepted (grilling session 2026-07-06) · **amended after independent review 2026-07-06 — see Amendments** · relates to ADR-0053, ADR-0054, ADR-0056, ADR-0061

## Context

Today every 30-minute iteration re-upserts everything pulled from Waldur — there is no change
detection, which wastes DB writes and floods logs with no-op activity. The refactor introduces a
diff so that unchanged items produce **no event, no write, no log line**.

The diff state must not become a second source of truth that can drift from the database, and it
must survive the future move of `Waldur.Sync` into its own container without dragging persistence
along.

## Decision

`Waldur.Sync` keeps a **low-memory, in-memory breadcrumb map** per instance:

```
(instance, entityType, externalId[, userId]) → SHA-256
```

The SHA answers exactly one question: *"has this entity, in the form UserOrg cares about, already
been processed?"* Per iteration, for each pulled item: build the DTO → hash it → compare with the
map → emit `Created` (no entry), `Updated` (different hash), or nothing (same hash).

**The hash is computed from the event payload, not the raw API response.** The normalized DTO the
consumer would receive (excluding metadata such as event id/timestamps) is serialized
canonically — stable property order, invariant culture, defined collection ordering and string
normalization in **one place** next to the hash function — and hashed with SHA-256. A change in a
Waldur API field we do not map therefore never triggers an event, and API formatting churn cannot
cause false `Updated` storms.

**No persistence — cold rebuild on start.** Because these entities are modified only by this
integration, the UserOrg database is the single source of truth. On startup the map is rebuilt by
loading the entities from the DB, mapping them to the same DTO shape, and hashing with the same
function. The first iteration after a restart is more expensive; that is acceptable at a
30-minute cadence. No sync-state table, no SHA columns on domain entities, nothing that can drift.

## Consequences

- Two mappings must be deterministic and produce identical DTOs for identical data:
  `Waldur API → DTO` and `UserOrg DB → DTO`. A dedicated test asserts round-trip hash equality
  (write from API-shaped DTO, rebuild from DB, hashes match).
- Map entries are written **only after successful processing** (ADR-0056); a failed item is
  naturally retried next iteration.
- Memory footprint is one hash per synced item — negligible for the expected data volume
  (tens to hundreds of projects per instance).

## Amendments (independent review 2026-07-06)

The review found concrete landmines in today's mappers that the generic "canonical serialization"
rule does not survive. The following rules are **binding** for Phase 0.

### A1 — The hash covers only pure functions of API data + configuration

Any field whose value can be clock-derived is **excluded from the hash** (it may still travel in
the payload for write purposes). Today's mappers contain `Created ?? timeProvider.GetUtcNow()`
fallbacks for several CreatedAt/StartDate fields — if any such field entered the hash, the hash
would change every iteration (perpetual `Updated` storm) and could never be rebuilt from the DB.
Concretely: audit-style fields (`CreatedAt`, `ModifiedAt`) are never hashed; the handler applies
write-time timestamps on create only.

### A2 — Normalization checklist (single place, next to the hasher)

1. **No clock reads** anywhere in payload mapping (A1).
2. **Truncation to DB limits happens in producer mapping** (`Name` → `DbConstants.NameMaxLength`,
   `Description` → 512, …) so the payload equals what lands in the DB — otherwise DB-rebuild
   hash ≠ pull hash after every restart.
3. **Canonical GUID format** pinned (lowercase `"D"` with dashes) — the Waldur API delivers
   dashless GUIDs (`GuidNoDashJsonConverter`); normalize at the API-client boundary.
4. **UTC everywhere, defined precision** (round to seconds unless a field demonstrably needs
   more — DB roundtrip must not lose hashed precision).
5. Null-date conventions preserved from today (`EndDate ?? DateTime.MaxValue`) and applied in
   mapping, not at write time.
6. **Null vs. empty collection are the same value**; collections sorted by a defined key before
   serialization.
7. Strings trimmed; endpoint normalization (`ssh://` strip, case rules) in mapping.
8. Decimals serialized with invariant culture, fixed format.
9. Derived/aggregated values (resource Min/Max date grouping, provider location derivation per
   ADR-0054/A3) must be reproducible from DB rows — that is what the round-trip test asserts.

**Addendum (2026-08-14) — truncate descriptive fields, skip on invalid identity fields.** Rule 2
states truncation-in-producer without qualification; it holds only for **descriptive** fields —
the `Description` of every entity the mapper produces plus the `Name` of provider, provider
location and project, i.e. every call site of `WaldurMapper`'s `Truncate`/`TruncateRequired` whose
value no consumer matches rows by (the set grows with the mapper). They are not all treated alike:
control-character **stripping** applies to exactly two of them, `Provider.Name`
(`WaldurMapper.cs:47`) and `ProviderLocation.Name` (`:277`); `Project.Name` (`:142`) and every
`Description` are trimmed and truncated only. `ProjectResource.Name` is an **identity** field —
deliberately **not** stripped (`:370-377`) and rewritten no further than that same trim and
truncation — because the consumer matches a resource by `(Name, ServiceName)`
(`ProjectUpsertHandler.cs:209-214`), so rewriting the mapped name re-keys it: inserting a second
row and orphaning the stored one instead of updating it. The project slug (`ProjectPayload.ShortName`) is likewise an **identity** field and
is never truncated or rewritten either: a slug failing the shared charset/length rule
(`ShortNameRules.IsValidShortName`, `Waldur.Contracts/ShortNameRules.cs:68`)
**skips the whole project** with a dedicated skip reason (`EMappingSkipReason.ProjectSlugInvalid`,
`Waldur.Sync/Mapping/EMappingSkipReason.cs:35`, applied at `WaldurMapper.cs:115-119`), producing
no payload at all.

The skip is fail-closed in both directions: a skipped project reaches the diff engine with a null
payload but under its own external id (`WaldurSyncWorker.cs:453,479`), so it gets neither an
upsert nor an absence transition (`DiffEngine.cs:69,187`) — the stored row simply freezes at its
last synced state, indefinitely, until the slug is fixed upstream in Waldur. Its members are
untouched for the same reason: with a null payload and no fetch failure no member fetch is issued
at all (`WaldurSyncWorker.cs:499-504`), so the snapshot is null and `DiffMembership` no-ops
(`DiffEngine.cs:98-101`). That is the accepted cost of not inventing a short name: a stale project
is recoverable, a collided or fabricated identity is not.

The asymmetry is intentional. `ShortName` carries a unique index
(`ProjectTableConfiguration.cs:80-82`) and is a live external identifier: HEAppE work items are
keyed by it (`ProjectUpsertHandler.cs:193` → `HeappeSynchronizationService.cs:22`) and API routes
address projects by it (`ProjectEndpoint.cs:155,178`). Folding an invalid slug into a valid one
can collide two distinct Waldur projects onto one stored value, and the loser then fails on the
unique key at every 30-minute iteration.

`main` folds instead: it warns and persists `ProjectShortName.CreateNormalized`
(`WaldurEfpExporter.cs:128-139`, `PuhuriExporter.cs:108-119`). The divergence is deliberate, but
not because `main` is safe — the same unique index exists there
(`origin/main:ProjectTableConfiguration.cs:83-85`, byte-identical), and a new project whose folded
slug equals an existing short name fails on duplicate key there too. `main`'s window is narrower
(the slug is written on insert only; its `SyncFrom` does not touch `ShortName`) and, decisively,
`main` has no hash/diff layer: a value that never converges costs nothing when nothing rebuilds a
hash from the DB and compares it. Here `WaldurShaMapRebuilder` reads the stored short name
verbatim (`WaldurShaMapRebuilder.cs:180`), so a project that never lands keeps re-emitting
`Created`/`Updated` forever. Skipping also keeps the external identifier truthful instead of
inventing a UserOrg-only one.

**Residual — `ProjectResource.Name` is treated as identity, but is still truncated.** It carries a
unique `(ProviderId, Name)` index (`ProjectResourceTableConfiguration.cs:65-68`), HEAppE work-item
synchronization is keyed by it (`ProjectUpsertHandler.cs:193`), and API routes address a resource
by it (`ProjectEndpoint.cs:178,213`) — the same three signals rule 2's `ShortName` exception is
built on, and why it is never control-character stripped. It is nonetheless still truncated in
producer mapping (`WaldurMapper.cs:377`, sourced from `EffectiveId` falling back to `BackendId`),
so the class of collision this addendum exists to prevent for `ShortName` remains possible for an
over-long identifier. This is pre-existing behaviour on both this branch and `main`; it is
deliberately **not** changed here and is tracked separately.

**Standing caution — `Location.Endpoint` is both mapper-transformed and an upsert key.** Locations
are matched by `(Endpoint, ServiceName)`, then by `Endpoint` alone
(`WaldurProviderContextExtensions.cs:117`, `ProviderUpsertHandler.cs:116,129`), while the mapper
writes that endpoint through `NormalizeEndpoint` (`WaldurMapper.cs:547-550`: `ssh://` strip for HPC,
trim otherwise). It is inert today only because that function is stable — any future edit to it
re-keys every stored location exactly as stripping the resource name re-keyed resources, inserting
duplicates rather than renaming. Anyone changing `NormalizeEndpoint` must treat it as a data
migration of the stored `Endpoint` values, not as a mapping tweak.

### A3 — Shared entities: hash instance-owned fields only

The premise "these items are modified only by this integration" is false for **users**: user rows
are keyed by `UserName`, shared between instances (EFP + Puhuri) and touched by the app itself
(OIDC login, admin edits). Hashing mutable shared attributes (e.g. `Email`) would cause spurious
event storms after every restart and a two-instance overwrite ping-pong. Therefore membership
hashes cover **only instance-owned fields**: `(projectExternalId, userName, role, access flags)`.
User-profile fields are written on first create only; the sync is not their owner afterwards.

### A4 — Rebuild scope rules

- The rebuild **includes soft-deleted projects** (`IsRemoved` is part of the hashed payload,
  ADR-0054/A1) — otherwise every restart re-emits `Created` for removed projects.
- `DB → ProjectPayload` mapping **excludes reconciliation-owned rows** — anything with
  `ServiceName = EfpCoreServiceName` (ADR-0061) — or every restart produces spurious
  `ProjectUpdated` for all EFP projects.
- Membership entries: one per non-deleted `AssignedUserToProject` (key per ADR-0054/A2).
- The storage-config hash (ADR-0061) is **not** rebuilt from the DB; the worker always fires one
  `StorageConfigurationChanged` after startup instead.

