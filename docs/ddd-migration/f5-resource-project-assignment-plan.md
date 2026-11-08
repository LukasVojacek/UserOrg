# F5 implementation plan — resource↔project assignment (`AssignedProjectResourceToProject`)

> **✅ Implemented (2026-06-28), all phases A–F.** Suite 585 pass / 0 fail / 10 known-skip; API host clean.
> [ADR-0031](../architecture/adr/0031-projectresource-natural-provider-identity.md) recorded. Two deviations from
> the plan, both documented in ADR-0031: (1) the EF **alternate key on `Provider.Name` was skipped** — the
> `ProjectResource → Provider` FK targets the `Provider.Id` PK, so no alternate principal key is needed; (2) the
> create-provider uniqueness **guard already existed** (`ProviderExistsAsync` → 409), so Phase A1 only added the
> `IX_Provider_Name` index. RBAC under `perm:provider:update` is operational EndpointMap data (same as F4 resource
> writes), not code.

> Output of a grilling session, 2026-06-28. Branch `feat/refactor-core`. TDD throughout (red → green per phase).
> Grounds: [ADR-0016](../architecture/adr/0016-resource-assignment-writes-through-provider-aggregate.md) (HEAppE sync via `IResourceSyncTrigger`, cross-agg invariant via query),
> [ADR-0026](../architecture/adr/0026-projectresource-per-provider-identity.md) / [ADR-0030](../architecture/adr/0030-projectresource-write-model.md) (per-provider resource identity),
> [ADR-0027](../architecture/adr/0027-v2-natural-key-uniqueness-gaps.md) (Provider.Name uniqueness),
> [ADR-0028](../architecture/adr/0028-transactional-outbox-for-domain-events.md) (outbox — deferred to F6),
> [ADR-0029](../architecture/adr/0029-natural-keys-at-contract-and-aggregate-boundaries.md) (natural keys at boundaries).

## Scope

Rebuild the resource↔**project** link (deleted in P1.5): `Project.AssignResource` / `RemoveResource` sync
aggregate methods + `PUT`/`DELETE` endpoints. **Not** the resource↔location allocation — that shipped in F4
Phase D. Legacy surface: [ProjectService.cs:252-310](../../src/Core/Application/UserOrg/Services/ProjectService.cs)
(`AssignedProjectResourceToProjectAsync` / `UnAssignedProjectResourceToProjectAsync`),
[ProjectEndpoint.cs:178-237](../../src/Core/API/API/ProjectEndpoint.cs).

The link entity [AssignedProjectResourceToProject](../../src/Modules/Core.Domain/Project/AssignedProjectResourceToProject.cs)
already exists: composite natural key `(ProjectId, ProjectResourceId)`, `ISoftDeletableEntity`, `Order` ignored
(not in DB). `Project` is the aggregate root; `_assignedResources` is already mapped.

## Decisions taken in the grilling

| # | Question | Decision |
|---|---|---|
| G1 | How is the resource addressed, given ADR-0026 killed global resource-name uniqueness? | **Provider-nested route.** `Projects/{projectShortName}/Providers/{providerName}/Resources/{resourceName}`. Reuses the F4 per-provider seam `GetResourceForAssignmentByNameAsync(providerName, resourceName)` verbatim (ADR-0030 D6). No slug — `ProjectResource` has none; `(provider, name)` is its honest identity. Legacy route shape (`Project/{sn}/ProjectResource/{name}`) is the global-name bug — dropped, same divergence already accepted for F4 create. |
| G2–G4 | `ProjectResource` carries `Provider`'s surrogate `ProviderId` as a **CLR** property — clean it up? | **Yes — promote to natural provider identity (prerequisite, Phase A).** DB stays Guid FK on `Provider.Id` (referential integrity, no schema change); on `ProjectResource` it becomes a **shadow FK**. EF loads the relationship into a **private `Provider? _provider` backing field**; the domain exposes **only** `ProviderName => _provider?.Name`. Mirrors the existing `Project._projectDomainType` → `DomainType` pattern ([Project.cs:28,53](../../src/Modules/Core.Domain/Project/Project.cs)). Satisfies ADR-0029 (surrogate confined to storage §3, natural id at the domain surface §4). Rejected: denormalized `ProviderName` column (schema migration + backfill rewriting week-old ADR-0026 schema, for a Guid that never crosses a contract and no write flow reads); public navigation (embeds the aggregate root). |
| G5 | Authorization model | **`ProviderWriteAuthorization`** (Admin = any provider; provider identity = only its own `providerName` from the route). Faithful to legacy (provider-key + admin bypass), and the provider-nested route scopes it exactly. Not project-membership — the resource belongs to the provider. |
| G6 | Invariant placement + non-shared parity | **Split.** Aggregate (`Project.AssignResource`) enforces what it sees over `_assignedResources`: read-only guard, idempotency/reactivation. Use-case enforces cross-aggregate checks via queries: resource read-only (from the resolver `Mode`), and the **project-level non-shared invariant** (location sharedness lives under `Provider`). Port the legacy non-shared rule **faithfully incl. its asymmetry** (blocks a new assign when an *existing* assigned resource is non-shared; does not inspect the incoming one) — documented as a preserved legacy quirk, not silently fixed. |
| G7 | HEAppE sync + outbox | **In-memory now, outbox → F6.** `Project.AssignResource`/`RemoveResource` raise a new `ProjectResourceAssignmentChangedEvent(ProjectShortName, ResourceName)`; a handler (mirroring `ResourceSyncHandler`) enqueues the sync for **that one project** through the existing `IResourceSyncTrigger` port (no new HEAppE plumbing). Dispatched on the same in-memory post-commit path as the already-shipped ADR-0016 sync — best-effort, faithful to legacy. ADR-0028 "prerequisite for F5" = the *decision* precedes F5, not the full outbox build; F6 swaps both events to durable delivery in one place. |

Minor decisions (legacy/convention defaults, not grilled): unassign = **soft-delete** + reactivate on re-add;
assign active→**409**, deleted→reactivate, delete missing→**404**; success **204 No Content** (not legacy
`Ok(string)`); group `ProviderAdmin`; both routes `DisabledWhen("ReadOnlyMode")`; cache evict the project +
templates-info tags.

---

## Phase A — prerequisite: `ProjectResource` → natural provider identity (ADR-0031) — ✅ done

> **Own commit, own ADR, F4 suite green before Phase B.** Reworks shipped F4 code, so it lands and is verified
> independently. Amends ADR-0030 §2.

1. **`Provider.Name` uniqueness.** Add filtered unique index `IX_Provider_Name WHERE IsDeleted = 0` as a **legacy
   migration** (active set already unique per ADR-0027 → additive, no dedup); mirror in the modular
   `ProviderConfiguration`; promote `Provider.Name` to an EF **alternate key** (needed as the FK principal key).
   Add the create-provider use-case guard (ADR-0027 item).
2. **`ProjectResource` model:** drop the public CLR `ProviderId`; add `private Provider? _provider` backing field;
   expose `public ProviderName? ProviderName => _provider?.Name`. `Create(resourceName, **Provider** provider, …)`
   sets `_provider` (EF derives the shadow FK).
3. **`ProjectResourceConfiguration`:** map `ProviderId` as a **shadow FK** to `Provider.Id` (keep
   `FK_ProjectResource_Provider_ProviderId` + `IX_ProjectResource_ProviderId_Name` — no schema change); wire the
   `HasOne<Provider>().WithMany().HasForeignKey("ProviderId")` relationship to the `_provider` field.
4. **F4 create use-case:** replace the `GetResourceCreateContextAsync` id-resolution with
   `IProviderRepository.FindAsync(providerName)` (→ 404); narrow the uniqueness query to **NameTaken-only** (→ 409);
   pass the loaded `Provider` into `Create`.
5. **Repository load** of `ProjectResource` `Include`s `_provider` so the identity is whole after materialisation.
6. **ADR-0031** — supersedes ADR-0030 §2 (nullable CLR `ProviderId` → nullable shadow FK + `ProviderName`
   projection); records the in-context private-navigation choice (surrogate in storage, natural id at the surface).

- **TDD / Verify (A):** existing F4 aggregate + create-endpoint + query tests adapted (`Create` now takes
  `Provider`; assert `ProviderName` projection); a new test that a materialised resource exposes its
  `ProviderName`; `Provider.Name` duplicate-insert hits the unique index. Full suite green; API host clean.

## Phase B — `Project` aggregate: assign / remove + event (TDD first) — ✅ done

1. **Write the aggregate unit tests first** ([ProjectAggregateTests](../../tests/Modules/Core/Core.UnitTests/ProjectAggregateTests.cs)),
   no mocks, over a loaded `Project`:
   - assign a resource → appears in `Resources`; assign again (active) → `409`-mapped error; assign a
     soft-deleted link → **reactivated**, not duplicated; remove an active link → soft-deleted, gone from
     `Resources`; remove a missing/already-removed link → not-found error; any mutation when the project is
     read-only → `ProjectIsReadOnly`.
2. **Implement** `Project.AssignResource(ProjectResource resource)` / `RemoveResource(ProjectResource resource)`:
   read-only guard (`EnsureNotReadOnly`), match the link by the `ProjectResource` reference over
   `_assignedResources` (mirrors `AssignExistingTag` / `UnassignUser`), reactivate-or-409 / soft-delete-or-404,
   raise `ProjectResourceAssignmentChangedEvent(ShortName, resource.ResourceName)`.
3. New `ProjectErrors` codes: `ResourceAlreadyAssigned`, `ResourceNotAssigned`. New event in `ProjectEvents`.

- **Verify (B):** aggregate tests green; no DB needed.

## Phase C — read queries — ✅ done

1. **Reuse** `IResourceAssignmentQuery.GetResourceForAssignmentByNameAsync(providerName, resourceName)` (F4 Phase D)
   to resolve `(provider, name)` → id + `Mode` + provider-ownership gate. A foreign/unknown/NULL-`ProviderId` row
   → null → 404 (invariant #5 at the boundary, no new query).
2. **New** `IProjectResourceAssignmentQuery.ProjectHasNonSharedResourceAsync(projectShortName, ct)` → `bool`
   (cross-aggregate: does the project already hold an active assigned resource with an active non-shared
   location?). Method-syntax, `EXISTS`-gated.

- **TDD / Verify (C):** query tests (in-memory + a real-SQL translation guard, per the project's verification
  rule): non-shared present/absent, soft-deleted allocations excluded.

## Phase D — endpoints + auth + cache + RBAC — ✅ done

1. `AssignProjectResourceEndpoint` (`PUT`) + `UnassignProjectResourceEndpoint` (`DELETE`) under
   `Projects/{projectShortName}/Providers/{providerName}/Resources/{resourceName}` (filename/handler/doc per the
   endpoint conventions; group `ProviderAdmin`; `DisabledWhen("ReadOnlyMode")`).
2. Flow: validate names → `ProviderWriteAuthorization` → load `Project` (404) → resolve resource per-provider
   (404) → resource read-only guard (`Mode`, 422) → **assign only:** non-shared-project query (→ conflict) →
   load the `ProjectResource` aggregate by id → `project.AssignResource/RemoveResource(resource)` → `SaveChanges`
   → evict project + templates-info cache → **204**.
3. RBAC entries under `perm:provider:update` (same as F4 resource writes); register + map in `Composition`.

- **TDD / Verify (D):** handler tests — 204 happy path (assign + remove), 404 (project / resource /
  cross-provider), 409 (already assigned), 422 (read-only resource), 403 (wrong provider identity), non-shared
  conflict. API host builds clean.

## Phase E — HEAppE sync — ✅ done

1. `ProjectResourceSyncHandler : IDomainEventHandler<ProjectResourceAssignmentChangedEvent>` → one
   `IResourceSyncTrigger.EnqueueResourceSyncAsync(projectShortName, resourceName)` (the single affected project).
2. Register the handler in `Composition`.

- **TDD / Verify (E):** handler test (mirrors `ResourceAssignmentEventHandlerTests`) — the event enqueues exactly
  one sync for the affected project.

## Phase F — docs / trackers — ✅ done

- `progress.md` / `next-phase-plan.md`: F5 done; note F6 outbox still pending (will swap both sync events).
- This plan's phases marked ✅; ADR-0031 cross-linked.

---

## Sequencing & risk

- **Order:** A (prerequisite, own commit + ADR-0031, F4 green) → B → C → D → E → F. B/C/E are independent of A's
  schema (they use `ResourceName`); only Phase A touches the identity. D depends on B+C.
- **Phase A is the only risky step** — it reworks just-closed F4 (Create signature, create use-case, config) and
  amends ADR-0030. Land it discretely so a regression is bisectable.
- **Migrations-overlay freeze:** Phase A adds *one* real legacy migration (`IX_Provider_Name`); the modular
  snapshot stays frozen (pre-existing overlay debt, not F5 — see F4 plan).

## Out of scope (do not fold in)

- **Transactional outbox (ADR-0028)** — F6; F5 stays on the in-memory sync path (parity).
- **Overlay-migration snapshot regeneration** — pre-existing, own task.
- **`ProviderId` NOT-NULL tightening** — legacy NULL rows persist by design (ADR-0026).
- **Fixing the legacy non-shared asymmetry** — preserved for parity (G6), documented not corrected.
