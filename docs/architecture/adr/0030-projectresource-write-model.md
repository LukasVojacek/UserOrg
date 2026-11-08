# ADR 0030 — ProjectResource write model: aggregate root, immutable per-provider identity, and the invariant-#5 enforcement seam

- Date: 2026-06-28
- Status: accepted (2026-06-28) — formalises decisions made while implementing F4 (Phases A–D); live on `feat/refactor-core`.
- Refines / amends: **ADR-0026** on two enforcement-seam points — per-provider uniqueness is enforced by a **read query + filtered-unique index backstop**, not a domain port; invariant #5 is enforced by **use-case per-provider resolution**, not an in-aggregate `ProviderId` compare.
- Relates to: ADR-0016 (resource-assignment writes through Provider), ADR-0024 (zero-surrogate v2 contracts), ADR-0027 (natural-key uniqueness gaps), ADR-0028 (transactional outbox — F5/F6 prerequisite), ADR-0029 (natural keys at contract & aggregate boundaries).

## Context

ADR-0026 settled the *model* — a `ProjectResource` belongs to exactly one provider, identity `(ProviderId, Name)`
— and sketched the enforcement: invariant #5 "inside the aggregate method", per-provider name uniqueness "in the
use-case via a domain port". Implementing F4 (Phases A–D) made two of those seam choices concrete, and both
**diverge from ADR-0026's stated wording**. Plus the v2 create contract diverges from legacy. Those are
hard-to-reverse calls and belong in a standing ADR, not buried in a migration plan.

| ADR-0026 said | What shipped (A–D) | Why |
|---|---|---|
| Per-provider name uniqueness enforced "in the use-case **via a domain port**" + integration test | A **read query** `GetResourceCreateContextAsync` (no port); filtered unique index = concurrency backstop | A pure *create* has no resolve-then-mutate of an existing aggregate, so a port adds nothing over a read (mirrors `CreateProvider`'s `ProviderExistsAsync`). The resolve-then-mutate port pattern still applies where it's real (F5). |
| Invariant #5 enforced "**inside the aggregate method**" (`AssignResource` compares the resource's and location's `ProviderId`) | Invariant #5 = **per-provider resolution in the use-case** (D1/D6): resolve `(providerName, resourceName)`; a foreign/unknown resource is simply not found → 404 | `Provider` has no CLR surrogate id (keyed by `ProviderName`); an in-aggregate `ProviderId` compare would force surfacing the shadow id for no extra safety. The per-provider resolver *is* the invariant boundary. |

## Decision

1. **Aggregate-root promotion.** `ProjectResource : AggregateRoot` (was `BaseAuditableEntity`), keeping
   `ISoftDeletableEntity, IServiceIntegrationEntity, IHasValidityPeriod`. It owns its lifecycle: `Create`,
   `UpdateDetails`, `Delete`. `Id` / `CreatedAt` / `ModifiedAt` are EF shadow properties.
2. **`ProviderId` ownership (nullable).** Identity is `(ProviderId, Name)` (ADR-0026). The CLR property is
   nullable **only** to materialise legacy standalone rows; a v2-created resource always carries one.
3. **Immutable name / rename disabled (D2).** PUT touches metadata only (`Description?`,
   `PrimaryInvestigatorEmail`, `StartDate`, `EndDate`); `operationMode` / `externalId` / `serviceName` are
   **preserved** from the loaded aggregate (C2). A rename is delete + recreate — there is no second uniqueness
   path to defend.
4. **Per-provider uniqueness via a create-context read query + index backstop (refines ADR-0026).**
   `GetResourceCreateContextAsync(providerName, name)` → `(ProviderId, NameTaken)?`: null → 404 (no active
   provider under that name), `NameTaken` → 409. The surrogate `ProviderId` is resolved server-side (ADR-0029 —
   never in the contract). `IX_ProjectResource_ProviderId_Name WHERE IsDeleted = 0` is the **concurrency
   backstop** for the check-then-insert race, not the primary guard.
5. **Invariant #5 enforced by per-provider resolution in the use-case (D1, amends ADR-0026).** PUT/DELETE resolve
   `(providerName, resourceName)` → `ProjectResourceId?` via `GetResourceIdAsync` (filters `IsDeleted = 0`); a
   foreign / unknown / soft-deleted resource resolves to null → 404 (C1/C3). The **assign** path (D3/D6) resolves
   the same way through `GetResourceForAssignmentByNameAsync(providerName, resourceName)` — one method-syntax read
   query with an `EXISTS` / `Any` provider-ownership gate (the non-null-`==`-nullable `ProviderId` comparison, no
   inner `.Join`). A name under another provider, or a NULL-`ProviderId` legacy row, never matches → the existing
   `provider.resource_not_found` 404.
6. **Create-contract divergence from legacy (recorded explicitly).** v2 create is **provider-nested**
   (`POST Providers/{providerName}/Resources`), **stamps `ProviderId`** from the route, and **drops** legacy's
   flat provider-less standalone creation. The body is the faithful legacy set
   (`{ Name, Description?, PrimaryInvestigatorEmail, StartDate, EndDate }`); `PrimaryInvestigatorEmail` is
   required (legacy parity).
7. **D5 — NULL-`ProviderId` legacy rows are unreachable via v2 provider-nested assign. Accepted.** A provider-less
   resource has no provider to address it under. The ADR-0026 split backfilled every allocation-bearing resource;
   the residual NULL population = zero-allocation legacy rows (no `Location` ⇒ no derivable provider), already
   documented as the ADR-0026 edge (Consequences "Edge — zero-allocation / standalone resources", lines 77–80).
   Bounded by F1/F3 legacy-writer retirement, **out of F4 scope** — cross-linked there rather than claimed as
   "all legacy rows fixed". The `ProviderId == route || ProviderId == null` alternative is **rejected** — it
   resurrects the cross-provider ambiguity D3 exists to kill and leaks ownership.

## Endpoint surface

| Verb | Route | Name | Success | Auth |
|---|---|---|---|---|
| POST | `Providers/{providerName}/Resources` | `CreateResourceV2` | 201 (`CreatedAtRoute` → `GetResourceByNameV2`) | ProviderAdmin + `ProviderWriteAuthorization` |
| PUT | `Providers/{providerName}/Resources/{resourceName}` | `UpdateResourceV2` | 204 | ProviderAdmin + `ProviderWriteAuthorization` |
| DELETE | `Providers/{providerName}/Resources/{resourceName}` | `DeleteResourceV2` | 204 | ProviderAdmin + `ProviderWriteAuthorization` |

All three `DisabledWhen("ReadOnlyMode")`. Errors: 400 (validation, before any load) · 403 (wrong provider
identity) · 404 (unknown provider / resource / cross-provider / soft-deleted) · 409 (create: name taken under the
provider).

## Considered options

- **Per-provider uniqueness: domain port vs read query.** Chose the **read query** for the pure-create case — a
  create has no existing aggregate to resolve-then-mutate, so a port adds ceremony over a read (mirrors
  `CreateProvider.ProviderExistsAsync`). The resolve-then-mutate port pattern still applies where mutation of an
  existing aggregate is real (F5).
- **Invariant #5: in-aggregate `ProviderId` compare vs use-case resolution.** Chose **use-case resolution** —
  `Provider` has no CLR surrogate id (keyed by `ProviderName`), so an in-aggregate compare would force surfacing
  the shadow id for no extra safety. The per-provider resolver is the invariant boundary.

## Consequences

- **F5 (project↔resource assignment) is unblocked** — it resolves resources by name through the same per-provider
  seam (`GetResourceForAssignmentByNameAsync`).
- **Overlay-migration snapshot debt (whole-model drift) is pre-existing and out of scope** — see the F4 plan's
  "Migrations-overlay debt". Modular migrations aren't applied at runtime, so this is tooling debt, not a
  correctness risk.
- **`ProviderId` NOT-NULL tightening stays deferred** — legacy NULL rows persist by design (ADR-0026).
