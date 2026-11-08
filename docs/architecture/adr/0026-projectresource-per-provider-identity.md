# ADR 0026 - ProjectResource is per-provider: a resource belongs to exactly one Provider, unique by `(ProviderId, Name)`

- Date: 2026-06-27
- Status: accepted (2026-06-27) — domain-owner decision, live-data-verified against `UserOrgDBContext`. Cross-provider sharing is a **data bug**, not a valid model.
- Supersedes: the **resource model** of ADR-0024 — "Resources are addressed by their unique name (data-verified)", the "Data verification … resource model REVISED" section, and the **dropping of invariant #5**. ADR-0024's slug decisions for Request and AssignedLocation are **unchanged**.
- Relates to: ADR-0016 (resource-assignment writes through Provider), ADR-0011/0012 (provider/location auth & naming)

## Decision

A `ProjectResource` **belongs to exactly one Provider**. Its identity is `(ProviderId, Name)`, not a
global name. The same human name may legitimately exist under **different** providers (e.g. `OPEN-33-82`
under IT4Innovations and under LUMI are **two distinct resources**), but never twice within one provider.

- Add a **`ProviderId` FK** (nullable column, additive) on `ProjectResource`.
- Enforce a **filtered unique index `(ProviderId, Name) WHERE IsDeleted = 0`**. The previous global
  `(ServiceName, Name)` uniqueness (ADR-0024) is dropped (and was never physically present in the verified
  DB — only `PK` + non-unique `IX_ProjectResource_Name` exist).
- **Restore invariant #5:** every `AssignedLocation` of a resource must point to a `Location` whose
  `ProviderId` equals the resource's `ProviderId`. A resource is allocated only under its own provider.
- A v2 `AssignResource` resolves a resource by `(providerName, resourceName)` — the provider comes from the
  route, so there is no chicken-and-egg.

This reverses ADR-0024's "Resource↔Provider is M:N / globally-unique-name / no invariant #5" conclusion.
ADR-0024 reached that conclusion by treating the cross-provider rows it found as *valid*. The domain owner
has ruled they are **invalid data** to be corrected — see Data verification.

## Context

ADR-0024 found resource `OPEN-33-82` allocated under two providers and concluded resources are M:N to
providers. A fresh live audit (2026-06-27) confirmed the data but **reclassified it as a bug**: in the
real domain an HPC resource is operated by a single provider; a resource reachable under two providers is
a modelling error from the legacy global-name catalog, where a name collision across centers produced one
shared row instead of two.

The blast radius is small and the fix is mechanical (see Consequences). The legacy global-name lookup is
the only consumer that assumes one row per name; per the domain owner this single use case is acceptable
to adjust during the fix.

## Considered options

- **Per-provider via `ProviderId` FK + unique `(ProviderId, Name)` (chosen).** Encodes ownership directly,
  makes uniqueness enforceable in the DB, and restores invariant #5. The `ProviderId` column is additive
  (nullable); the unique index is additive (no prior unique index to drop in the verified DB).
- **Per-provider via `ProviderName` natural key (rejected here).** Consistent with ADR-0024's natural-key
  direction and avoids a surrogate FK, but `Provider.Name` has no DB unique constraint (app-level only,
  ADR-0011), so `(ProviderName, Name)` could not be a trustworthy DB key without first adding provider-name
  uniqueness. `ProviderId` gives referential integrity now; the public contract still addresses by
  `providerName` at the route (no GUID leaks).
- **Keep ADR-0024's global M:N model (rejected by domain owner).** Treats `OPEN-33-82`-style rows as valid;
  the owner ruled they are data errors.
- **Differentiating slug only, name left non-unique (rejected).** Differentiates the split rows but does
  not encode provider ownership into the index and leaves uniqueness unenforced.

## Consequences

- **Schema (additive where it counts):** new nullable `ProjectResource.ProviderId` FK + filtered unique
  `(ProviderId, Name) WHERE IsDeleted = 0`. Adding the column and the index is additive; restoring the
  invariant is enforced in the use-case + the index. No NOT-NULL tightening (a standalone resource with no
  provider keeps `ProviderId` NULL until assigned — see edge below).
- **One-shot data fix** splits the 3 offending resources and repoints children — see
  `docs/ddd-migration/sql/0026-projectresource-per-provider-split.sql`.
- **Domain model:** `ProjectResource` becomes an aggregate carrying its owning `ProviderId`; F4
  (ProjectResource CRUD) and F5 (project↔resource assignment) build on this.
- **Enforcement is aggregate-first, with tests; the DB index is only a concurrency backstop:**
  - **Invariant #5** (a resource's allocations stay under its own provider) is enforced **inside the
    aggregate method** — `AssignResource` receives the resolved `ProjectResource` and `Location` (both carry
    `ProviderId`) and rejects a mismatch. Covered by a **unit test on the aggregate** (no mocks).
  - **Per-provider name uniqueness** `(ProviderId, Name)` is cross-aggregate (two resources), so it is
    enforced in the **use-case via a domain port** (resolve-or-reject by name within the provider) per the
    CLAUDE.md resolve-then-sync-mutate pattern, covered by an **integration test**; the filtered unique index
    catches only the concurrent-writer race.
- **The data split (`0026-…-split.sql`) is one-time remediation** of existing legacy rows — not a runtime
  path. Once the aggregate/use-case guards are in place and legacy resource writes retire, the cross-provider
  state cannot recur.
- **Legacy:** the global-name lookup may now find a per-provider row; verified to be the single affected
  use case, adjusted as part of the fix.
- **Edge — zero-allocation / standalone resources:** legacy `InsertProjectResourceAsync`, Waldur sync, and
  `ConvertToEntity` insert resources with no allocation, so their provider cannot be back-derived. Those
  rows keep `ProviderId = NULL` and are unaddressable per-provider until a provider is set; v2 create must
  set `ProviderId` explicitly.

## Data verification (2026-06-27, populated `UserOrgDBContext` @ 127.0.0.1,1433)

77 active resources, 14 providers. Exactly **3** resources are allocated under more than one provider; **no
duplicate active names** exist (so the per-provider rule introduces zero in-provider collisions).

| Resource | Provider A (allocations) | Provider B (allocations) | Projects assigned | Request refs |
|---|---|---|---|---|
| `OPEN-33-82` | IT4Innovations (3) | LUMI (1) | 1 | 1 |
| `VLQ-FI` | IT4Innovations (1) | LRZ (1) | 3 | 1 |
| `VLQ-PL-P0001` | IT4Innovations (1) | LRZ (1) | 1 | 0 |

- **Allocations split deterministically:** each `AssignedLocation` belongs to a `Location` with exactly one
  `ProviderId`, so each allocation maps unambiguously to one split.
- **Project assignments duplicate to both splits:** every affected project independently reaches *both*
  providers, so there is no signal to pick one. Preserving today's access means assigning the project to
  both per-provider splits. (5 assignment rows total.)
- **Requests (2) are resolved manually:** historical records with no provider signal; listed by the script,
  not auto-repointed.
- **`UserProjectAccess` FK exists but the table is empty (0 rows)** — ignored by the split.
- `Request.Slug` / `AssignedLocation.Slug` columns are **absent** in this DB — ADR-0024's slug migration is
  not yet physically applied here (tracked separately; see the status report's "Slug additive migration").
