# Refactor progress tracker

> Living checklist of remaining work toward v1 cutoff. Plan & rationale: [next-phase-plan.md](./next-phase-plan.md).
> Last updated: **2026-07-07 (code-verified sweep)** · Branch: `feat/refactor-core`.

## Status snapshot

| Area | State |
|---|---|
| F0.3 read-scope tiers | **complete** — 5/5 list endpoints + by-id tiering (GetProvider/GetLocation/GetResource) |
| F0.4 parity doc | **complete** — Administration-8 rows ✅, GetResources tier note, next-phase-plan F0 closed |
| **F2 CyclopsUsage + ResetAllCaches** | **complete** — `DELETE api/v2/Caches` (evict-all); CyclopsUsage doc-only (Gateway serves it) |
| **F3 admin role mgmt (A4)** | **complete** — `PUT Users/{idp}/ProviderAdmin/{pn}` + `DELETE Users/{idp}/ProviderAdmin`; domain-owned (`DomainUser`), 13 tests, no Forge role-write |
| Resource per-provider (ADR-0026) | **F4 complete (A–E); ADR-0030 recorded** |
| ProjectResource natural provider identity (ADR-0031) | **F5 Phase A complete** — shadow FK + `ProviderName` projection |
| resource↔project assignment | **F5 complete (A–F)** — assign/unassign endpoints + HEAppE sync |
| **DDI module (B2+B3)** | **complete** — `Modules/Services/Ddi/UserOrg.Ddi` wired (`AddDdi`/`MapDdiEndpoints`); WorkerData, DataStagingLocations GET/POST, ProjectsInfo, SyncDetails under `api/v2` |
| Natural-key uniqueness gaps (ADR-0027) | guards done+tested; Provider #3 **closed**; tag/role indexes **deferred** to legacy-writer retirement; slug **staged** (runbook) — checkpoint [ADR-0039](../architecture/adr/0039-adr-0027-remediation-checkpoint.md) |
| Transactional outbox (ADR-0028) | **complete (#237, ADR-0054)** — `IDurableEvent` events routed through `outbox` schema in the aggregate transaction; `OutboxDispatcherService` polls + delivers to HEAppE with retry/backoff + telemetry |
| **B1 Heappe file download** | **✅ done (#236, ADR-0053)** — `ListChangedFilesForJob` relocated to `Modules/Integrations/Heappe/Features/LogRetrieval/DownloadChangedFilesEndpoint.cs`; legacy `HeappeEndpoint.cs` deleted; token via shared `OriginalBearerToken` binder |
| **F7 reads-parity suite** | **✅ done (#238, ADR-0055)** — live side-by-side v1↔v2 through the Gateway on real SQL (`Tests/E2E/ParityTests`); all GET/list read pairs covered. **Writes + DDI/S2S reads remain Phase 2** (explicitly still open) |
| Tests | Core.UnitTests **705 pass / 0 fail / 11 skip** (716 total) — re-run & confirmed 2026-07-07; build clean (0 errors) |

---

## Done (committed & pushed)

- [x] ADR-0026 ProjectResource per-provider + 3 EF migrations + data-split SQL + deployment runbook — `5a8c20d5`
- [x] ADR-0027 natural-key uniqueness gaps (tag/role/provider) + aggregate-first enforcement — `603cae36`
- [x] ADR-0028 transactional outbox + grilling decisions captured — `f690304e`
- [x] F0.3 widen endpoints: GetProjects, GetRequests, GetResources + per-tier tests — `03c60229`
- [x] F0.3 GetLocations tier (fixes pre-F0 private-location leak) + tests — `db9f8914`

### Done (uncommitted — F0.3 Phase 1)
- [x] **ADR-0029** natural keys at contract/aggregate boundaries; surrogate ids stay in read-query internals
- [x] **F0.3 GetProviders tier** — admin→all · provider→pin own · user→public ∪ project-reachable-private
  - `ProviderFilter.ScopeShortNames`; `ProviderDetailsQuery` resolves the inverse chain (`…→Location→Provider`) and ORs reachable into the `OnlyPublic` predicate; endpoint tier switch
  - 3 handler tier tests + 5 in-memory reachability tests + 1 real-SQL translation guard (skips w/o `USERORG_TEST_DB`)

---

## Remaining

### F0.3 — read-scope tiers ✅ complete
- [x] **GetProviders** list — admin→all · provider→`Name==pn` · user→public ∪ project-reachable (Phase 1)
- [x] **By-id tiering (Phase 2)** — only the user tier is scoped; admin/provider resolve any active entity (ADR-0029: natural-key + scope query, no surrogate in the contract). 404-hiding parity preserved (missing == out-of-scope)
  - [x] `GetProvider` — `GetProviderDetailsAsync(name, scope)`; private resolves for a user only when reachable (reuses Phase 1 chain); admin/provider unscoped
  - [x] `GetLocation` — collapsed the `GetIdByProviderAndNameAsync → GetLocationAsync(Guid)` two-step into a single `GetLocationByProviderAndNameAsync(provider, name, scope)`; removed a surrogate round-trip (ADR-0029)
  - [x] **`GetResource` by-id — discovery: was NOT tiered** (passed `[.. caller.Scope]` unconditionally → admin/provider got 404). Fixed: nullable scope, endpoint passes null for admin/provider; added the missing tier tests
  - [ ] _Deferred:_ nested location list inside `GetProviderDetail` is not scoped (pre-existing; follow-up, not F0.3)

### F0.4 — parity documentation ✅ complete
- [x] `v1-retirement-parity.md`: marked 8 `Administration/*` GET rows ✅ (now tier-covered); summary + verdikt recounted (backlog 19→11)
- [x] GetResources tier note added + corrected the "Providers/Locations not caller-scoped" assumption (F0.3 narrowed them)
- [x] Updated F0 status in `next-phase-plan.md` (F0.3 + F0.4 ✅)

### F1 — ProjectTag catalogue delete — **✅ complete** ([plan](./f1-projecttag-delete-plan.md))
Phases: A ✅ · B ✅ · C ✅ · D ✅. Core.UnitTests green (608: 597 passed, 11 skipped integration).
- [x] **Phase A (domain + overlay):** `ProjectTag` → `AggregateRoot` (natural key `TagName`) + `Delete()` (soft-delete, idempotent `AlreadyDeleted` guard); `ProjectTagErrors`/`ProjectTagErrorCodes` (`TagNotFound`/`TagInUse`/`AlreadyDeleted`); `IRepository<ProjectTag, TagName>` on `DatabaseContext` (`FindAsync` excludes soft-deleted → missing reads as 404), DI registered.
- [x] **Phase B:** `IProjectTagQuery.IsTagAssignedToAnyProjectAsync` — block-if-in-use gate over the `_projectTags` skip-nav `SelectMany` (in-memory + real-SQL guard tests); Project soft-delete filter + `!t.IsDeleted` exclude inactive links.
- [x] **Phase C:** `DELETE ProjectTags/{tagName}` (Admin group, `DisabledWhen("ReadOnlyMode")`) — validate (400) → Admin-only (403) → find (404) → in-use (409) → soft-delete (204). No cache evict (listing uncached). Handler tests; API host builds clean.
- **Parity divergences (record for the F7 gate — intended, not regressions):** (1) **no rename / no description** — v2 `ProjectTag` is name-only (G3); a name change is delete + recreate. (2) **delete blocks-if-in-use → 409** vs legacy's unconditional delete (G2).
- _RBAC: route under `perm:projecttag:*` = operational EndpointMap data (same as F4/F5 writes), not code._
- _Out of scope (unchanged): `Tag.Name` filtered unique index + dedup — still gated on legacy tag-writer retirement (ADR-0027)._

### F4 — ProjectResource CRUD (foundational; ADR-0026)
Plan: [f4-projectresource-crud-plan.md](./f4-projectresource-crud-plan.md). Phases: A ✅ · B ✅ · C ✅ · D ✅ · E ✅.
- [x] **Phase A (domain + overlay):** `ProjectResource` → `AggregateRoot` carrying nullable `ProviderId`;
  `Create` requires it; `UpdateDetails` (name/provider immutable, rename disabled); `Delete`;
  `IRepository<ProjectResource, ProjectResourceId>` + DI; config maps `ProviderId` + swaps the stale
  `(ServiceName,Name)` index for the real filtered unique `IX_ProjectResource_ProviderId_Name`.
  6 aggregate tests; suite 525 pass / 0 fail / 9 skip.
  - Overlay probe verified: `ProjectResource`-table ops = `AddColumn ProviderId` + unique
    `IX_ProjectResource_ProviderId_Name` (filter `[IsDeleted]=0 AND [ProviderId] IS NOT NULL`), exactly matching
    the deployed schema. Config faithfully reproduces physical reality.
  - ⚠️ **Overlay-migration debt (pre-existing, not F4):** the rest of the probe is a whole-model rewrite — the
    snapshot is frozen at legacy CLR names and drifted across all tables (unfinished aggregates). Modular
    migrations aren't applied at runtime, so this is tooling debt; snapshot **not** regenerated. Own task.
- [x] **Phase B (uniqueness + create):** `IResourceQuery.GetResourceCreateContextAsync(providerName, name)` →
  `(ProviderId, NameTaken)?` (resolves owning id server-side + per-provider uniqueness; query-based per ADR-0030,
  index = concurrency backstop). `POST Providers/{providerName}/Resources` (`CreateResourceV2`, ProviderAdmin,
  `DisabledWhen("ReadOnlyMode")`): faithful legacy body, `ProviderWriteAuthorization`, 404/409/201. 7 handler +
  4 query tests (incl. same-name-under-two-providers — the ADR-0026 proof). Suite 536 pass / 0 fail / 9 skip;
  API host builds clean.
- [x] **Phase C (update + delete):** `IResourceQuery.GetResourceIdAsync(providerName, name)` → `ProjectResourceId?`
  (resolves owning id server-side; `!IsDeleted` filter ⇒ missing/wrong-provider/soft-deleted all null, C1/C3).
  `PUT Providers/{providerName}/Resources/{resourceName}` (`UpdateResourceV2`) — create-shaped body, name immutable,
  `operationMode`/`externalId` preserved from the loaded aggregate (C2), 204. `DELETE …/{resourceName}`
  (`DeleteResourceV2`) — soft-delete through the aggregate, 204. Both ProviderAdmin + `DisabledWhen("ReadOnlyMode")` +
  `ProviderWriteAuthorization`; both load the aggregate and mutate through it. 4 query + 5 update + 3 delete tests.
  Suite 548 pass / 0 fail / 9 skip; API host builds clean.
- [x] **Phase D (per-provider assign resolution):** `IResourceAssignmentQuery.GetResourceForAssignmentByNameAsync`
  gained the `providerName` qualifier; the impl is one method-syntax read query with an `EXISTS`/`Any`
  provider-ownership gate (non-null-`==`-nullable `ProviderId`, no inner `.Join`). `AssignResourceEndpoint` passes
  the route provider in → a cross-provider / NULL-`ProviderId` / unknown resource gives the existing
  `provider.resource_not_found` 404 (invariant #5 at the boundary, D1). Suite **552 pass / 0 fail / 9 skip**; API
  host builds clean.
- [x] **Phase E (ADR-0030 + trackers):** ADR-0030 (ProjectResource write model) recorded — aggregate-root
  promotion, immutable name (D2), uniqueness via read query + index backstop and invariant #5 via use-case
  resolution (both refine/amend ADR-0026), create-contract divergence, D5, D6. Trackers flipped.
- _Deferred (tooling debt, not folded into F4):_ `GetResourceIdAsync` does a redundant `providerName → ProviderId`
  round-trip (binary outcome — could be a single `EXISTS`-gated query like D6). Not a correctness risk.

### F5 — resource↔project assignment — **✅ complete** ([plan](./f5-resource-project-assignment-plan.md), [ADR-0031](../architecture/adr/0031-projectresource-natural-provider-identity.md))
- [x] **Phase A (ADR-0031):** `ProjectResource` natural provider identity — public CLR `ProviderId` → private `_provider` nav over a shadow FK + `ProviderName` projection; `Create(Provider)`; repo load includes `_provider`; create use-case via `IProviderRepository.FindAsync` (404) + `IsResourceNameTakenAsync` (409); filtered unique `IX_Provider_Name` (ADR-0027). Own commit; F4 suite green (557).
- [x] **Phase B:** `Project.AssignResource`/`RemoveResource` — sync mutations over `_assignedResources` (reactivate-or-409 / soft-delete-or-404, matched by `ProjectResource` reference); raises `ProjectResourceAssignmentChangedEvent(ShortName, ResourceName)`; new error codes.
- [x] **Phase C:** `IProjectResourceAssignmentQuery.ProjectHasNonSharedResourceAsync` — cross-aggregate non-shared invariant (G6), EXISTS-gated shadow-FK joins; in-memory + real-SQL guard tests.
- [x] **Phase D:** `PUT/DELETE Projects/{projectShortName}/Providers/{providerName}/Resources/{resourceName}` — provider-nested route (ADR-0026), `ProviderWriteAuthorization`, read-only (422) + non-shared (409) guards, cache evict project + templates-info, 204. Handler tests.
- [x] **Phase E:** `ProjectResourceSyncHandler` → one HEAppE sync for the affected project (in-memory path; outbox F6).
- _RBAC: routes under `perm:provider:update` = operational EndpointMap data (same as F4 resource writes), not code._

### F6 — DDI + HEAppE (in cutoff scope, separate modules, parallel to F4/F5)
- [x] **DDI module (B2+B3) — complete.** `Modules/Services/Ddi/UserOrg.Ddi` exists, wired via `UserorgAdapterExtensions` (`AddDdi`/`MapDdiEndpoints`). Routes under `api/v2`: `DataStaging/WorkerData`, `DataStaging/DataStagingLocations` (GET+POST), `Service/ProjectsInfo`, `Service/SyncDetails`. Queries `SyncDetailsQuery`/`DdiProjectsInfoQuery` in `Core.DataAccess`; tests in `tests/Modules/Services/Ddi/`.
- [x] **Relocate `Heappe/ListChangedFilesForJob` (B1)** from Core.API to the Heappe module — **✅ done (#236, ADR-0053)**. New `DownloadChangedFilesEndpoint` in `Modules/Integrations/Heappe/Features/LogRetrieval/` (Path A via `IUserorgService` + `IHeappeService`, no legacy Core app dependency); bearer token via shared `OriginalBearerToken` binder in `UserOrg.Shared`; legacy `HeappeEndpoint.cs` deleted.
- [x] **Transactional outbox (ADR-0028) — core done at `e8b84efd`.** Both `ResourceAssignmentChangedEvent` and `ProjectResourceAssignmentChangedEvent` implement `IDurableEvent`; `UnitOfWork` routes durable events into the `outbox` schema within the aggregate transaction; the `OutboxDispatcherService` polls and delivers them to HEAppE with retry/backoff. This session (ADR-0054) hardened it: observability (`OutboxTelemetry` meters + tracing spans), dispatcher unit tests + real-DB transactional integration test, and retirement of the now-dead `IResourceSyncTrigger` fire-and-forget path. Row-claim locking deferred (single-instance, seam documented — ADR-0054 D4).

### ADR-0027 uniqueness remediations — disposition checkpoint [ADR-0039](../architecture/adr/0039-adr-0027-remediation-checkpoint.md) (2026-06-28)
Guards for all three gaps are **DONE + tested** (98 guard tests green, 2026-06-28). Remaining = the DB-index half only.
- [ ] **ProjectTag.Name** index + dedup (`DraTag2` ×2, `acsac` ×2) — **DEFERRED**. Guard DONE (`Project.AssignExistingTag` + mint/assign use-case). Index lift-condition: retire the legacy writer `POST /api/ProjectTag` (`src/Core/API/API/ProjectTagEndpoint.cs`), then dedup + filtered unique (F1).
- [ ] **UserPermission** `(UserId, ProjectId)` index + dedup (1 violating user) — **DEFERRED**. Guard DONE (`Project.UpsertMemberRole`). Index lift-condition: retire the legacy writers `PUT /api/Project/{ProjectShortName}/User/{IdpUserId}` + `.../Assign/{RoleId}` (`src/Core/API/API/ProjectEndpoint.cs`), then dedup + filtered unique (F3).
- [x] **Provider.Name** filtered unique `WHERE IsDeleted=0` + create-provider guard — **DONE**. Guard (`ProviderExistsAsync` 409, tested `ProviderWriteEndpointTests`), index (`20260628081747_AddProviderNameUniqueIndex`); no dedup needed (active set clean).
- [ ] **ADR-0024 slug migration** (`AddRequestAndAssignedLocationSlug`) — **STAGED**, not applied. Owned by the deployment runbook (`0026-deployment-runbook.html` phase ④, after the M1→data-fix→M2 bracket; SQL `sql/0026-slug-add-request-assignedlocation.sql`). No agent applies it.

### F7 — reads-parity gate — **✅ done (reads)** ([ADR-0055](../architecture/adr/0055-f7-v1-v2-parity-suite.md), #238)
- [x] Per-feature real-SQL integration tests + data-integrity sweep (decided: verification moved forward)
- [x] **v1↔v2 reads-parity suite** — `Tests/E2E/ParityTests`, live side-by-side through the Gateway on real SQL. Shared `E2E.Fixtures` library (extracted from `EndpointsTests`), declarative `ParitySpec`/`Divergence` model, recursive `JsonShapeComparer` (structural + must-match ordering keys). Covers every GET/list read pair, including the 5 regressed lists (`resources`/`providers`/`locations`/`requests`/`Requests/Sent`) with must-match ordering-key assertions — the regression proof for the 2026-06-16 `ORDER BY <VO>.Value` 500.
- [ ] **Phase 2 (still open):** write-parity (needs `ResetToSeedAsync`, side-effectful) + DDI/Service/DataStaging S2S reads (API-key auth, separate dimension). `CyclopsUsage` drop-by-design.

**F7 findings — two real product defects the suite caught (separate fixes, out of F7's test-authoring scope):**
1. **v2 location-by-id → 403.** `GET /api/v2/Providers/{pn}/Locations/{ln}` is blocked at the Gateway: `roleManagmentRBAC.json` still maps the removed flat `/api/v2/Locations/{id}` and has **no GET mapping for the nested route** → the Gateway fails closed (403) while v1 `GET /api/Location/{id}` returns 200. Fix = add the nested GET mapping to the RBAC file. Encoded in the suite as a fail-forward `StatusRemap(200→403)` known-divergence (`ResourceProviderLocationByIdParityTests`), so the suite stays green while the divergence stays visible.
2. **v2 anonymous public → 401.** `GET /api/v2/Public/*` is missing from the Gateway `PublicEndpointsOptions` anonymous allow-list (only `/api/Public` is listed) → 401 vs v1 200. Fix = add the `/api/v2/Public` prefix. Suite test skipped with reason (`PublicParityTests`) until the allow-list is fixed.

_Both are separate Gateway-config product fixes, not F7 code — F7's job was to catch them, which it did._

---

## Remaining before cutoff (2026-07-07 verified — the short list)
1. ~~**B1** — relocate `Heappe/ListChangedFilesForJob` into the Heappe module.~~ ✅ **done (#236, ADR-0053)**.
2. ~~**Durable outbox** — route the two HEAppE sync events through the existing outbox.~~ ✅ **done (#237, ADR-0054)**.
3. ~~**F7 parity suite** — build it; it does not exist.~~ ✅ **reads-parity done (#238, ADR-0055)** — `Tests/E2E/ParityTests`. Writes + DDI/S2S reads remain Phase 2.
4. **Deferred unique indexes** (`ProjectTag.Name`, `UserPermission.(UserId,ProjectId)`) — apply after retiring their legacy writers.
5. **ADR-0024 slug migration** — apply in prod (runbook phase ④); EF migration `20260627145647_AddRequestAndAssignedLocationSlug` already committed.
6. **Legacy teardown** — remove `src/Core/` stack + legacy `api/*` mapping once the above lands.

> ⚠️ Live regression flagged in the sweep: `CoveredEndpointRoleMatrixTests.cs:343` hard-skips a test — legacy `POST /api/Project/` returns 404 (not 201) in E2E. Low priority (v1 is being retired) but a real signal.

## Cutoff definition of done
All v1 routes migrated/re-homed (F1–F6) · [v1-retirement-parity.md](./v1-retirement-parity.md) fully green · uniqueness remediations applied after their legacy writers retire · parity suite green.
