# F0.4 + F4 implementation plan — ProjectResource write model

> Output of a grilling session, 2026-06-27. Branch `feat/refactor-core`.
> Grounds: [ADR-0026](../architecture/adr/0026-projectresource-per-provider-identity.md) (per-provider identity),
> [ADR-0027](../architecture/adr/0027-v2-natural-key-uniqueness-gaps.md) (uniqueness gaps),
> [ADR-0029](../architecture/adr/0029-natural-keys-at-contract-and-aggregate-boundaries.md) (natural-key boundaries),
> [next-phase-plan.md](./next-phase-plan.md), [progress.md](./progress.md).

## Decisions taken in the grilling

| # | Question | Decision |
|---|---|---|
| D1 | Invariant #5 enforcement seam | **Use-case resolution.** Resolve the resource by `(providerName, name)`; a foreign/unknown resource is simply not found → 404. `Provider` has no CLR surrogate id (keyed by `ProviderName`), so an in-aggregate `ProviderId` compare would cost surfacing the shadow id for no extra safety. The per-provider resolver *is* the invariant boundary. |
| D2 | Is `Name` mutable on update? | **Immutable. Rename is disabled.** PUT touches metadata only. A rename is delete + recreate. No second uniqueness path. |
| D3 | Fix the existing `AssignResourceV2` global-name resolver? | **Fold into F4.** Post-split the same name can exist under two providers, so `GetResourceForAssignmentByNameAsync(name)` is now ambiguous — it's the invariant-#5 seam and a latent correctness bug. |
| D4 | Where to record the write-model decisions? | **New ADR-0030** (ProjectResource write model + endpoints + enforcement seam). |

### Phase C grilling (2026-06-27)

| # | Question | Decision |
|---|---|---|
| C1 | How does PUT/DELETE resolve `(providerName, resourceName)` → aggregate, given the repo is keyed by the shadow `ProjectResourceId`? | **Query → id → `FindAsync` → mutate within the aggregate.** Add `IResourceQuery.GetResourceIdAsync(providerName, resourceName)` → `ProjectResourceId?` (filters `IsDeleted = 0`; surrogate stays server-side, ADR-0029), then `repository.FindAsync(id)` loads the aggregate. **Both PUT and DELETE always load the full aggregate and change it through its own methods** (`UpdateDetails` / `Delete`) — no detached or bulk-SQL update. Mirrors the create-context pattern. |
| C2 | What does the PUT body expose? | **Mirror the create body:** `{ Description?, PrimaryInvestigatorEmail, StartDate, EndDate }` (no `Name` — immutable, D2). `operationMode` / `externalId` / `serviceName` are **preserved** from the loaded aggregate (passed back into `UpdateDetails` unchanged) — clients can't wipe fields create never exposed. |
| C3 | Missing / cross-provider / already-deleted on PUT/DELETE? | **One filtered resolution path → 404 for all three.** `GetResourceIdAsync` filters `IsDeleted = 0`, so a deleted row resolves to null exactly like a missing or wrong-provider one. No idempotent-delete second path. |
| C4 | Success status codes? | **PUT → 204 No Content** (name immutable, nothing new to return); **DELETE → 204 No Content**. |

---

## F0.4 — parity documentation (docs-only, do first) — ✅ done

Pure docs; no code. ~20 min quick win, clears the F0 column.

- [x] `v1-retirement-parity.md`: flip the 8 `Administration/*` GET rows to ✅ (now tier-covered by F0.3).
- [x] Add a `GetResources` regression row (outside the Administration-8); correct the stale "Providers/Locations caller-scoped" assumption to the F0.3 tier semantics.
- [x] `next-phase-plan.md`: mark F0 (F0.3 + F0.4) complete in the phase table + the F0 section.
- **Verify:** parity doc has no remaining ⏳ in the F0 scope; links resolve.

---

## F4 — ProjectResource CRUD write model

**Current state:** [ProjectResource.cs](../../src/Modules/Core.Domain/Provider/ProjectResource.cs) is a thin
`BaseAuditableEntity` (`Create` + `Delete`, no aggregate root, no repo, no `Update`, no `ProviderId`). The
physical column + filtered unique `(ProviderId, Name)` index already exist from legacy migration
`20260627143515_AddProjectResourceProviderId` (committed `5a8c20d5`) — this is the **deploy debt**: schema is
live, domain code is unaware. F4 closes that gap.

TDD throughout: write the failing test, then the code. Aggregate logic is tested directly (no mocks);
use-case/port logic gets handler + integration tests.

### Phase A — domain + overlay sync (foundational)

1. **Promote to aggregate root.** `ProjectResource : AggregateRoot` (was `BaseAuditableEntity`). Keep
   `ISoftDeletableEntity, IServiceIntegrationEntity, IHasValidityPeriod`.
2. **Add `ProviderId? ProviderId`** (nullable — legacy standalone rows stay NULL; v2 `Create` requires it).
3. **`Create` requires a non-null `ProviderId`.** New required param; keep the `EndDate >= StartDate` guard.
4. **`UpdateDetails(...)`** mutator for the immutable-name metadata set (description, start/end dates,
   operation mode, PI email, externalId). Re-checks `EndDate >= StartDate`. **No** name parameter (D2).
5. **`IRepository<ProjectResource, ProjectResourceId>`** — domain port; EF adapter in `Core.DataAccess`.
6. **Overlay sync** in [ProjectResourceConfiguration.cs](../../src/Modules/Core.DataAccess/Configurations/ProjectResourceConfiguration.cs):
   map `ProviderId`; **replace** the stale `IX_ProjectResource_ServiceName_Name` unique index (never physically
   in the DB per ADR-0026) with the real filtered unique `(ProviderId, Name) WHERE IsDeleted = 0`.

- **Verify (A):** ✅ done
  - Aggregate unit tests (`ProjectResourceDomainTests`, 6): `Create` carries the owning provider; rejects
    `EndDate < StartDate`; `UpdateDetails` mutates metadata, re-guards dates, leaves name + provider fixed;
    `Delete` sets `IsDeleted`. Full suite **525 pass / 0 fail / 9 skip**.
  - `dotnet build` clean (Core.DataAccess 0 warn/0 err).
  - **Overlay probe — config verified against physical schema.** The real acceptance (per the overlay note) is
    that the probe's operations *on the aggregate's own table* match the deployed schema. They do, exactly:
    `AddColumn ProviderId (uniqueidentifier, null)` + `CreateIndex IX_ProjectResource_ProviderId_Name UNIQUE
    ([ProviderId],[Name]) WHERE [IsDeleted] = 0 AND [ProviderId] IS NOT NULL` — byte-for-byte the legacy
    migration `20260627143515` + manual `0026-m2` SQL. No stale `(ServiceName,Name)` ops remain. The probe is
    *not* empty overall (it also rewrites FKs/indexes across `User`, `Project`, … ) but that is **pre-existing
    whole-model snapshot drift** from unfinished aggregates, documented in the overlay memory and unrelated to
    F4. Probe discarded; snapshot **not** regenerated (whole-model rewrite is its own task). The modular runtime
    does not apply these migrations, so write correctness does not depend on the snapshot.

### Phase B — per-provider name uniqueness + create ✅ done

> Implemented: `IResourceQuery.GetResourceCreateContextAsync` + `CreateResourceEndpoint` (`CreateResourceV2`).
> 7 handler tests + 4 in-memory query tests (incl. same-name-under-two-providers). Suite 536 / 0 / 9; API host
> clean. Same-name-different-provider verified at both the query level and end-to-end the create path.


> Grilling decisions (2026-06-27): **one read query** for resolution+uniqueness (not a domain port);
> **faithful legacy body** (PI email required); the **divergence from legacy is accepted and recorded in
> ADR-0030**. The v2 create is *not* a flat port — it is provider-nested, stamps `ProviderId`, enforces
> per-provider uniqueness, and **drops** legacy's provider-less standalone creation.

1. **One create-context query** on `IResourceQuery`:
   `GetResourceCreateContextAsync(ProviderName providerName, ResourceName name, CancellationToken ct)` →
   `ResourceCreateContext?` where the record is `(ProviderId ProviderId, bool NameTaken)`.
   - Returns **null** when no active provider has that name → endpoint emits **404**.
   - `NameTaken` is true when an active resource already carries `name` under that provider → **409**.
   - Maps the provider natural key → its surrogate `ProviderId` server-side (ADR-0029: read-query internal,
     never in the contract). Mirrors `CreateProvider`'s query-based `ProviderExistsAsync` rather than a domain
     port — there is no resolve-then-mutate of an existing aggregate here, so a port adds nothing (this refines
     ADR-0026's "domain port" wording; ADR-0030 records the rationale). The filtered unique index
     `IX_ProjectResource_ProviderId_Name` remains the **concurrency backstop** for the check-then-insert race.
2. **`CreateProjectResourceEndpoint`** — `POST Providers/{providerName}/Resources`. Audience `ProviderAdmin`,
   `DisabledWhen("ReadOnlyMode")`. Blends `CreateProvider` (create + uniqueness + 201) with `AssignResource`
   (provider-nested route + `ProviderWriteAuthorization`).
   - `Request([FromRoute] string ProviderName, Body)` where the body is the faithful legacy set:
     `{ string Name, string? Description, string PrimaryInvestigatorEmail, DateTimeOffset StartDate,
     DateTimeOffset EndDate }`. No `ProviderId` (ADR-0029); `ServiceName`/`OperationMode`/`ExternalId` default.
   - Validate: `ResourceName.TryParse`; `Description` ≤ 150; `PrimaryInvestigatorEmail` **required** + valid
     email ≤ 100 (legacy parity); `EndDate > StartDate`. Bad input → 400 *before* `Create` (the aggregate
     throws on bad dates — the endpoint guards first).
   - `ProviderWriteAuthorization.Authorize(caller, providerName, roles)` — admin any, owning provider identity
     only → 403.
   - Use-case: `GetResourceCreateContextAsync` → 404 (no provider) / 409 (`NameTaken`) →
     `ProjectResource.Create(name, ctx.ProviderId, start, end, description, primaryInvestigatorEmail: …)` →
     `repository.AddAsync` → `IUnitOfWork.SaveChangesAsync`.
   - `201` via `Results.CreatedAtRoute("GetResourceByNameV2", new { providerName, resourceName })`;
     `Response(string Name)`.

- **Verify (B):** handler tests (201 happy; 409 duplicate-under-provider; **201 same name under a *different*
  provider** — the per-provider win; 404 unknown provider; 403 wrong provider identity; 400 missing PI email /
  bad dates). The same-name-different-provider case is the regression that proves ADR-0026. **Integration test**
  (gated by `USERORG_TEST_DB`): the filtered unique index rejects a concurrent duplicate insert under one
  provider but allows it across providers. Build clean.

### Phase C — update + delete — ✅ done

**Resolution seam (C1, shared by both endpoints).** Add one read method:
`IResourceQuery.GetResourceIdAsync(ProviderName providerName, ResourceName name, CancellationToken ct)` →
`ProjectResourceId?`. Joins the resource's `ProviderId` to the route provider, filters `IsDeleted = 0`; returns
null for missing / wrong-provider / already-deleted alike (C3). Impl mirrors `GetResourceCreateContextAsync`
(`EF.Property<ProjectResourceId>` / server-side surrogate, ADR-0029). The endpoint then `repository.FindAsync(id)`
loads the aggregate and mutates **through it** (C1) — never a detached or bulk update.

1. **`UpdateResourceEndpoint`** — `PUT Providers/{providerName}/Resources/{resourceName}`.
   `Request([FromRoute] string ProviderName, [FromRoute] string ResourceName, Body)` where
   `Body = { string? Description, string PrimaryInvestigatorEmail, DateTimeOffset StartDate, DateTimeOffset EndDate }`
   (C2 — mirrors create, no `Name`).
   - Validate first (same as create, 400 before any load): `ProviderName.TryCreate`, `ResourceName.TryParse`
     (route key), `Description` ≤ 150, `PrimaryInvestigatorEmail` required + valid email ≤ 100,
     `EndDate > StartDate`.
   - `ProviderWriteAuthorization.Authorize(caller, provider, roles)` → 403 (auth as B).
   - `GetResourceIdAsync` → null → 404. `repository.FindAsync(id)` → aggregate.
   - `resource.UpdateDetails(StartDate, EndDate, Description, resource.OperationMode, email, resource.ExternalId)`
     — `operationMode` / `externalId` passed back **unchanged** from the loaded aggregate (C2). `serviceName` is
     identity, never touched.
   - `IUnitOfWork.SaveChangesAsync` → **204 No Content** (C4). `DisabledWhen("ReadOnlyMode")`, audience
     `ProviderAdmin`.
2. **`DeleteResourceEndpoint`** — `DELETE Providers/{providerName}/Resources/{resourceName}`. Same provider/name
   parse + `ProviderWriteAuthorization` (403) + `GetResourceIdAsync` → null → 404 + `FindAsync` → aggregate →
   `resource.Delete()` (sets `IsDeleted`) → `SaveChangesAsync` → **204** (C4). `DisabledWhen("ReadOnlyMode")`.
3. **Register both** in `EndpointsExtensions.cs` (`MapUpdateResourceEndpoint` / `MapDeleteResourceEndpoint`).

- **Verify (C):** handler tests —
  - PUT: 204 happy (metadata changes, `operationMode`/`externalId` preserved); 404 cross-provider; 404 missing;
    404 already-deleted; 403 wrong provider identity; 400 missing PI email / bad dates / over-long description.
  - DELETE: 204 happy (`IsDeleted` set); 404 cross-provider/missing/already-deleted; 403.
  - Query test (in-memory): `GetResourceIdAsync` returns the id under the right provider, null under a wrong
    provider and for a deleted row.
  - Full suite green (`Core.UnitTests.exe`); Core API host builds clean.

### Phase D — fold-in fix: per-provider assign resolution (invariant #5 seam, D3)

The existing [AssignResourceEndpoint.cs:65](../../src/Modules/Core/Features/Provider/ResourceAssignment/AssignResourceEndpoint.cs)
resolves the resource **globally** by name — now ambiguous after the split.

> Grilling decisions (2026-06-28):
> - **D5 — NULL-`ProviderId` legacy rows become unreachable via v2 assign. Accept.** The v2 assign route is
>   provider-nested; a provider-less resource has no provider to address it under. The 0026 split backfills every
>   *allocation-bearing* resource (74 clean + 3 split), so nothing previously assignable is stranded. The residual
>   NULL population = **zero-allocation legacy rows** (no `Location` ⇒ no derivable provider) — already documented
>   as the ADR-0026 edge (lines 77–80); it is bounded by F1/F3 legacy-writer retirement, **out of F4 scope**.
>   ADR-0030 cross-links the ADR-0026 edge rather than claiming "all legacy rows fixed". The alternative
>   (`ProviderId == route || ProviderId == null`) is rejected: it resurrects the cross-provider ambiguity D3
>   exists to kill and leaks ownership.
> - **D6 — Resolver shape: single query, not the Phase C two-step.** The assign resolver's outcome is *binary*
>   (target or null → the endpoint's existing 404), and the endpoint already 404s on a missing provider at
>   `AssignResourceEndpoint.cs:59` *before* the resolver runs — so a separate `providerName → ProviderId` lookup
>   (justified in `GetResourceCreateContextAsync`, which needs the 404-vs-409-vs-create three-way split) buys
>   nothing here. One query, **method-syntax LINQ**, provider-ownership as an `EXISTS` (`Any`) predicate. Keeps the
>   by-name path at 2 round-trips total (resolve + the unavoidable non-shared-location check), vs 3 for two-step.
>   `ProjectResource.ProviderId` is a nullable CLR property, so an inner `.Join` (mismatched key type `ProviderId?`
>   vs `ProviderId`) is avoided in favour of the `Any(... EF.Property<ProviderId>(prov,"Id") == pr.ProviderId)`
>   predicate — the same non-null-`==`-nullable comparison `GetResourceCreateContextAsync` already uses.

1. **`IResourceAssignmentQuery.GetResourceForAssignmentByNameAsync`** → add the `ProviderName` qualifier:
   `GetResourceForAssignmentByNameAsync(ProviderName providerName, ResourceName name, …)`. Impl is **one
   method-syntax query** that returns the resource only when an active provider with `providerName` **owns** it
   (`EXISTS` provider-ownership gate); a name under another provider — or a NULL-`ProviderId` legacy row — never
   matches → null → 404. Uniqueness now from `IX_ProjectResource_ProviderId_Name` (not the old `(ServiceName,
   Name)`); update the XML doc accordingly. The `(ProjectResource !IsDeleted)` + deactivated-provider query
   filters still apply.

   ```csharp
   var row = await resources.FirstOrDefaultAsync<Domain.Provider.Provider, IdModeRow>(
       (res, providers) => res
           .Where(pr => pr.ResourceName == resourceName
               && providers.Any(prov =>
                   prov.Name == providerName
                   && EF.Property<ProviderId>(prov, "Id") == pr.ProviderId))
           .Select(pr => new IdModeRow(EF.Property<ProjectResourceId>(pr, "Id"), pr.OperationMode)),
       "GetResourceForAssignmentByName.IdMode",
       ct);
   ```
2. **`AssignResourceEndpoint`** passes the route provider into the resolver:
   `GetResourceForAssignmentByNameAsync(name, resourceName, ct)`. A cross-provider resource → the existing
   `provider.resource_not_found` 404. This is invariant #5 enforced at the boundary (D1).

- **Verify (D):** ✅ done
  - **Query tests** (`ResourceAssignmentQueryTests`): resolves the id under the *owning* provider; null under a
    *wrong* provider (the split's canonical regression); null for a NULL-`ProviderId` legacy row; null for a
    soft-deleted row; null for an unknown name under a known provider.
  - **Endpoint test** (`ResourceAssignmentEndpointTests`): the handler forwards the route provider into the
    per-provider resolver (`GetResourceForAssignmentByNameAsync(name, …)` verified, 201). Existing assign/update/
    remove tests stay green — the mock setup gained the `ProviderName` argument.
  - `Core.UnitTests.exe --filter-class "*ResourceAssignmentQueryTests" "*ResourceAssignmentEndpointTests"` → 33/0.
    Full suite **552 pass / 0 fail / 9 skip**; Core API host builds clean (0 err).
  - Implementation: `IResourceAssignmentQuery.GetResourceForAssignmentByNameAsync` gained the `ProviderName`
    qualifier; the impl is one method-syntax query with an `EXISTS`/`Any` provider-ownership gate (the
    non-null-`==`-nullable `ProviderId` comparison, no inner `.Join`). `AssignResourceEndpoint` passes the route
    provider in. XML doc updated to the `IX_ProjectResource_ProviderId_Name` uniqueness + invariant-#5 wording.

### Phase E — ADR-0030 + tracker — ✅ done

1. **ADR-0030** — ProjectResource write model: aggregate-root promotion, `ProviderId` ownership, **immutable
   name / rename disabled** (D2), **per-provider uniqueness via a create-context read query + filtered-unique
   index backstop** (refines ADR-0026's "domain port" wording — a pure create has no resolve-then-mutate),
   **invariant #5 enforced by per-provider resolution in the use-case** (D1, amends ADR-0026's "in-aggregate"
   wording: `Provider` has no CLR id), and the **create-contract divergence from legacy** (provider-nested
   route, `ProviderId` stamped, legacy flat provider-less creation dropped). Endpoint surface. Cross-link
   ADR-0026/0027/0029. Record **D5** (v2 provider-nested assign cannot reach NULL-`ProviderId` legacy rows —
   cross-link the ADR-0026 edge, lines 77–80; population bounded by F1/F3) and **D6** (per-provider assign
   resolution via a single `EXISTS`-gated read query).
2. **progress.md / next-phase-plan.md** — mark F4 done; clear the "deploy debt" + "overlay sync" items;
   note F5 (resource↔project assignment) now unblocked. **Deferred (one line):** `GetResourceIdAsync` does a
   redundant `providerName → ProviderId` round-trip (binary outcome — could be a single `EXISTS`-gated query like
   D6); tooling debt, not a correctness risk, not folded into F4.

- **Verify (E):** ✅ done — [ADR-0030](../architecture/adr/0030-projectresource-write-model.md) written; cross-links
  (ADR-0016/0024/0026/0027/0028/0029) resolve; the two "refines ADR-0026" seam points stated explicitly. Trackers
  (`progress.md`, `next-phase-plan.md`, this plan) show F4 A–E ✅; F5 marked unblocked; `GetResourceIdAsync`
  deferral noted once. Docs-only — no code touched (suite already 552 / 0 / 9, API host clean from Phase D).

---

## Sequencing & risk

- **Order:** F0.4 (docs) → A → B → C → D → E. A is the gate (nothing compiles against `ProviderId` until the
  aggregate + overlay land). D depends only on A (the resolver join needs mapped `ProviderId`), so D could run
  right after A if B/C slip.
- **Migrations-overlay debt (discovered in Phase A):** the modular `DatabaseContextModelSnapshot` is frozen at
  legacy CLR names and has drifted from the modular model across *all* tables, so a Core.DataAccess
  `migrations add` produces a 45 KB whole-model rewrite — the planned "empty probe" check cannot pass and is
  **not** a signal about the `ProviderId` change specifically. The modular migrations are **not applied at
  runtime** (legacy owns schema), so this is documentation/tooling debt, not a correctness risk. Regenerating
  the snapshot (or retiring the modular migration chain) is its own task — **do not** fold it into F4.
- **Deferred (not F4):** nested location list inside `GetProviderDetail` is unscoped (pre-existing F0.3 carry-over).
- **Out of scope:** `ProviderId` NOT-NULL tightening (legacy NULL rows persist by design, ADR-0026); ADR-0027
  uniqueness remediations gated on legacy-writer retirement (F1/F3).
</content>
</invoke>
