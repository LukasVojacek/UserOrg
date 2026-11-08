# Další fáze refaktoru — sekvenční plán k odstřižení v1

> **Účel:** Prioritizovaný plán zbývající práce, aby bylo dosaženo [úplné parity](./v1-retirement-parity.md) a legacy `src/Core` šlo odstřihnout.
> **Princip řazení (rozhodnuto):** **risk-first, pak vzestupně dle velikosti.** Nejdřív levné de-riskování, pak quick-wins, těžké foundational kusy až po nasbírání jistých výher, parity-testy jako brána před cutoffem.
> **Stav (2026-07-08, ověřeno proti kódu):** F0, F1, F2, F3, F4, F5 hotové; **F6 = DDI modul + B1 Heappe relokace (#236, ADR-0053) + durable outbox (#237, ADR-0054) hotové**; **F7 reads-parity suite HOTOVÁ** (#238, ADR-0055 — writes + DDI/S2S reads zbývají jako Phase 2). Datum původního plánu: 2026-06-10.
> **Navazuje na:** [v1-retirement-parity.md](./v1-retirement-parity.md), [core-modules-target-architecture-and-plan.md](./core-modules-target-architecture-and-plan.md), [ADR-0017](../architecture/adr/0017-ddi-service-api-as-separate-module.md).

---

## Pořadí fází

| Fáze | Co | Bucket | Velikost | Proč tady |
|---|---|---|---|---|
| **F0** | Role-based reads (Admin + Provider scope) — *✅ hotovo (F0.0–F0.4)* | nový nález | **L** (cross-cutting) | de-risk: ruší **falešné ✅** u `Administration/*`; blokuje cutoff |
| **F1** | ProjectTag catalogue delete — *✅ hotovo* (update/description dropped, viz G3) | A3 | S–M | quick win, šablona existuje |
| **F2** | CyclopsUsage (drop/doc-only) + ResetAllCaches — *✅ hotovo* | C1+C2 | S | malé, dávka |
| **F3** | Admin role management (promote/demote) — *✅ hotovo* | A4 | M | oba endpointy wired + 10 testů |
| **F4** | ProjectResource CRUD writes — *✅ hotovo* | A1 | **L** | foundational write model |
| **F5** | resource↔project assignment — *✅ hotovo* | A2 | M–L | po F4 (referencuje resources) |
| **F6** | DDI modul — *✅ hotovo*; Heappe file download (B1) — *✅ hotovo*; durable outbox — *✅ hotovo (#237, ADR-0054)* | B2+B3+B1 | M–L | DDI modul namapovaný; B1 relokován do modulu (legacy smazán, ADR-0053, #236); durable outbox live (`IDurableEvent` + `OutboxDispatcherService`, ADR-0054) |
| **F7** | v1↔v2 behavioral **reads**-parity testy — *✅ hotovo (#238, ADR-0055)* | riziko | M | **brána před cutoffem — reads-parity postavena; writes + DDI/S2S reads = Phase 2** |

> Zbývá do zelené tabulky: **aplikace deferred unique indexů** po dekomisi legacy writerů + **F7 Phase 2** (write-parity + DDI/S2S read-parity). (B1 Heappe relokace ✅ #236; durable outbox ✅ #237/ADR-0054; F7 reads-parity ✅ #238/ADR-0055.) Pak lze v1 deprecovat a po okně odstřihnout.

---

## F0 — Role-based reads (de-risk, první) — **✅ hotovo**

> **Mechanismus:** [ADR-0018](../architecture/adr/0018-read-scope-tiers.md). Detailní plán, audit a review: [f0-role-based-reads.md](./f0-role-based-reads.md). Tady jen stav a co zbývá.

**Tři tiers v přísné precedenci** (zrcadlí `ProviderWriteAuthorization`): Admin (vše) > Provider-identita (`providerName` claim → data providera) > Caller (project scope). Resolved jedním sdíleným uzavřeným union `ReadScope` + `caller.ResolveReadScope(roles)` v `Core/Shared/Auth`. Každý endpoint **explicitně větví v `HandleAsync`** se shodným Response tvarem; admin = no-op scope, ne separátní endpoint.

**Audit (F0.0) přepsal původní předpoklady:**
- `GetProjects`/`GetResources`/`GetRequests` byly caller-scoped → admin neviděl nic → **rozšířit** (admin tier). 8 `Administration/*` GET bylo falešně ✅.
- `GetProviders`/`GetLocations` **nebyly scoped vůbec** → vracely vše vč. private → **zúžit** (regrese + únik private dat).
- `provider → projects` větev **zahozena** (bez legacy precedentu); `GetUsers`/`GetProjectTags`/`GetStatistics` **bez větvení** (parita).
- RBAC bez gapů; `GetRequests` nemá provider tier (RBAC ho nepustí).

| Krok | Co | Stav |
|---|---|---|
| **F0.0** | Audit (join cesty, RBAC, rozhodnutí Requests/Statistics) | ✅ hotovo |
| **F0.1** | `ReadScope` resolver + `IsAdmin`/`ResolveReadScope` + testy | ✅ hotovo |
| **F0.2** | Tier vstupy do filtrů + queries (Providers/Locations/Resources) + EF in-memory testy | ✅ hotovo |
| **review** | 2× adversariální review; opraven pre-existující soft-delete bug (Stage B), doplněno soft-delete pokrytí | ✅ hotovo |
| **F0.3** | Per-endpoint větvení v `HandleAsync` (vč. by-id endpointů) | ✅ hotovo (list + by-id, 2026-06-27) |
| **F0.4** | Parity rows zelené + dokumentace | ✅ hotovo (2026-06-27) |

**Rozhodnuto pro F0.3:** GetProviders user tier = **relevant** (public + private dosažitelné přes uživatelovy projekty, full parita — viz f0 doc); by-id endpointy (`GetResource`/`GetProvider`/`GetLocation`) potřebují tiering také; private lokace zůstávají viditelné user tier přes reachability (parita s legacy relevant-locations).

---

## F1 — ProjectTag catalogue delete (A3, quick win) — ✅ hotovo

Plán: [f1-projecttag-delete-plan.md](./f1-projecttag-delete-plan.md). Fáze A–D ✅. Realizováno **delete only**.

- Doména: `ProjectTag` → `AggregateRoot` (`TagName`) + `Delete()` (soft-delete, idempotentní guard); `ProjectTagErrors`/`ProjectTagErrorCodes`; `IRepository<ProjectTag, TagName>` (`FindAsync` filtruje soft-deleted).
- Endpoint `DeleteProjectTagEndpoint` (`DELETE ProjectTags/{tagName}`, Admin group, `DisabledWhen("ReadOnlyMode")`); block-if-in-use přes `IProjectTagQuery.IsTagAssignedToAnyProjectAsync` (in-memory + real-SQL guard).
- **Parity divergence (pro F7 gate, záměr):** (1) **žádný rename/description** — v2 tag je name-only (G3); (2) **delete blokuje při použití → 409** vs. legacy bezpodmínečné smazání (G2).
- **Verify:** ✅ unit testy domény + query (in-memory) + handler testy (204/404/409/403/400); Core.UnitTests green (597 passed). RBAC = EndpointMap data.

## F2 — CyclopsUsage + ResetAllCaches (C1+C2) — ✅ hotovo

- **C1 CyclopsUsage:** **žádný reálný v2 endpoint** — request reálně intercepuje **Gateway** (`CyclopsForwardingExtensions` → externí `cyclops-api`, Vault API-key), Core ho neservíruje. Legacy 202 placeholder je mrtvý kód. **Rozhodnuto (s vývojářem):** jen **OpenAPI záznam** pro viditelnost ve Scalar — `CyclopsUsageDocumentTransformer` injektne `GET /api/Other/CyclopsUsage/{ProjectShortName}/{ResourceName}` do dokumentu **v2-User** (path/params/responses + popis, že servíruje Gateway). Document-only, obejde v2 `ShouldInclude` filtr. Sjednoceno s [parity docem](./legacy-to-refactored-endpoint-migration.md) (řádek byl už „Not migrated by design").
- **C2 ResetAllCaches:** `DELETE api/v2/Caches` (Admin only, in-endpoint role check) → evict-all nad v2 output cache. Mechanismus: globální tag `CacheTags.All`, který nese **každá** cacheable odpověď (přidáno v `CoreCachePolicy`), eviktovaný jedním `IOutputCacheInvalidator.EvictAsync`. Zúženo na output cache (legacy HybridCache concerns — RBAC snapshot/JWKS — nejsou Core output cache). Vrací 204.
- **Verify:** ✅ handler testy (204+evict / 403+no-evict) — Core.UnitTests green; build (Core modul + API kompiluje). **RBAC follow-up (operační, mimo code task):** přidat `DELETE /api/v2/Caches` do `roleManagmentRBAC.json` s admin permission — stejně jako F1 `DELETE /api/v2/ProjectTags/{tagName}`, který v seedu také chybí.

## F3 — Admin role management (A4) — **✅ hotovo**

- **`PromoteToProviderAdmin`:** `PUT api/v2/Users/{idpUserId}/ProviderAdmin/{providerName}` — Admin-only (403), load user (404), resolve provider přes `IProviderQuery.FindActiveProviderIdByNameAsync` (404), pak **doménová** mutace `DomainUser.PromoteToProviderAdmin` + `IUnitOfWork.SaveChangesAsync`. 409 na admin-target přes `Result<Error>`.
- **`ResetToPlatformUser`:** `DELETE api/v2/Users/{idpUserId}/ProviderAdmin` — unassign všech providerů → `DomainUser.ResetToPlatformUser`, idempotentní.
- **Rozřešeno zjištění z původního plánu:** zápis role **NEžije** ve Forge — provider-admin promote/demote je čistě doménová operace v `Core.Domain/User/User.cs` (`PromoteToProviderAdmin`/`ResetToPlatformUser`/`UnassignProvider`, raisuje domain events). Forge `IRoleListService` se používá jen pro read-side role names (SyncDetails/UserInfo), ne pro tento zápis.
- **Verify:** ✅ oba mapované v `EndpointsExtensions.MapCoreEndpoints`; 5+5 handler testů (`PromoteToProviderAdminEndpointTests`/`ResetProviderAdminEndpointTests`), žádné skips.

## F4 — ProjectResource CRUD writes (A1, foundational) — **✅ hotovo**

> ✅ hotovo — viz [ADR-0030](../architecture/adr/0030-projectresource-write-model.md) + [f4-projectresource-crud-plan.md](./f4-projectresource-crud-plan.md). Write model rozhodnut a zapsán (ADR-0030); per-provider `(ProviderId, Name)` identita, immutable name, CRUD endpointy (A–E). Suite 552 / 0 / 9.

- **Největší kus.** `ProjectResource` je dnes tenká entita (`Create`+`Delete`, není aggregate root, bez repo, bez `Update`). Povýšit na zapisovatelný agregát: `IRepository<ProjectResource, ProjectResourceId>`, `Update`, validity invarianty (`StartDate`/`EndDate`), events, error kódy.
- Endpointy `POST/PUT/DELETE ProjectResource`; auth dle role; read-only guard.
- **Pozn.:** write model ProjectResource zapsán v [ADR-0030](../architecture/adr/0030-projectresource-write-model.md) (aggregate root + per-provider identita; hard-to-reverse rozhodnutí formalizovaná po implementaci A–D).
- **Verify:** doménové unit testy bez mocků; handler testy; migrace? (ověřit shadow→mapped, držet migrations-overlay freeze); build.

## F5 — resource↔project assignment (A2) — **✅ hotovo**

> ✅ hotovo — viz [f5-resource-project-assignment-plan.md](./f5-resource-project-assignment-plan.md) + [ADR-0031](../architecture/adr/0031-projectresource-natural-provider-identity.md). Fáze A–F zelené; suite 585 / 0 / 10.

- **Phase A (ADR-0031):** `ProjectResource` povýšen na natural provider identitu — public CLR `ProviderId` nahrazen private `_provider` navigací nad shadow FK + `ProviderName` projekcí; `Create` bere `Provider`; create use-case přes `IProviderRepository.FindAsync` + `IsResourceNameTakenAsync`; filtrovaný unique `IX_Provider_Name` (ADR-0027). Vlastní commit, F4 suite zelená.
- **Phase B:** `Project.AssignResource`/`RemoveResource` synchronní mutace nad načtenou `_assignedResources` (reactivate-or-409 / soft-delete-or-404, match dle reference jako role/tag); event `ProjectResourceAssignmentChangedEvent`.
- **Phase C:** `IProjectResourceAssignmentQuery.ProjectHasNonSharedResourceAsync` (cross-agg non-shared invariant, EXISTS-gated, in-memory + real-SQL guard).
- **Phase D:** `PUT/DELETE Projects/{projectShortName}/Providers/{providerName}/Resources/{resourceName}` (provider-nested per ADR-0026, ne legacy global-name tvar); `ProviderWriteAuthorization`; read-only (422) + non-shared (409) guardy; cache evict project + templates-info; 204.
- **Phase E:** `ProjectResourceSyncHandler` → 1 HEAppE sync pro dotčený projekt (in-memory path; outbox F6).
- RBAC: routes pod `perm:provider:update` (stejně jako F4 resource writes) — provozní EndpointMap data, ne kód.

## F6 — DDI modul + Heappe file download (B2+B3+B1)

- **DDI modul — ✅ hotovo.** `src/Modules/Services/Ddi/UserOrg.Ddi` ([ADR-0017](../architecture/adr/0017-ddi-service-api-as-separate-module.md)) existuje, wired přes `Userorg.Adapter/UserorgAdapterExtensions.cs:37` (`AddDdi`) + `:50` (`MapDdiEndpoints`). Endpointy pod `api/v2`: `DataStaging/WorkerData`, `DataStaging/DataStagingLocations` (GET+POST), `Service/ProjectsInfo`, `Service/SyncDetails`. Backed `SyncDetailsQuery` (347 ř.) + `DdiProjectsInfoQuery` (225 ř.) v `Core.DataAccess/Queries`; role names přes Forge.Core `IRoleListService`. Testy: `tests/Modules/Services/Ddi/`.
- **B1 Heappe — ✅ hotovo (#236, ADR-0053).** `ListChangedFilesForJob` relokován do `src/Modules/Integrations/Heappe/Features/LogRetrieval/DownloadChangedFilesEndpoint.cs` (Path A: `IUserorgService.GetProjectResourceAllocationWithLocationsAsync` → `HeappeInstance.Uri` + `IHeappeService`, žádná legacy Core app závislost); bearer token přes nový sdílený `OriginalBearerToken` binder v `UserOrg.Shared`; legacy `src/Core/API/API/HeappeEndpoint.cs` smazán. 5 handler testů + 6 binder testů.
- **Durable outbox — ✅ hotovo (#237, ADR-0054).** `ResourceAssignmentChangedEvent` + `ProjectResourceAssignmentChangedEvent` implementují `IDurableEvent`; `UnitOfWork` je routuje do `outbox` schématu ve stejné agregátní transakci; `OutboxDispatcherService` je pollem doručuje do HEAppE s retry/backoff. ADR-0054 přidal observability (`OutboxTelemetry`), dispatcher unit testy + real-DB transakční integrační test a odstranil mrtvou `IResourceSyncTrigger` fire-and-forget cestu (row-claim locking odložen — single-instance, seam zdokumentován, ADR-0054 D4).

## F7 — v1↔v2 behavioral **reads**-parity testy (brána před cutoffem) — **✅ hotovo (2026-07-08, #238, ADR-0055)**

- **Postaveno:** `Tests/E2E/ParityTests` — **live side-by-side** porovnání v1 `/api/*` ↔ v2 `/api/v2/*` proti **reálnému SQL přes Gateway** (ne EF in-memory — ta prošvihla `ORDER BY <VO>.Value` 500-regresi). Sdílená `E2E.Fixtures` knihovna (vyextrahovaná z `EndpointsTests`), deklarativní `ParitySpec`/`Divergence` model, rekurzivní `JsonShapeComparer` (structural shape + must-match ordering keys), s allow-listem záměrných divergencí (value-object envelope, nested route, GUID→natural-key, 404-hiding).
- **Pokrytí:** všechny GET/list read páry, včetně **5 regresních list endpointů** (`resources`/`providers`/`locations`/`requests`/`Requests/Sent`) s must-match ordering-key aserceni — regression proof pro `ORDER BY <VO>.Value` 500.
- **F7 našel 2 reálné produktové defekty** (separátní fixy, mimo scope F7 test-authoringu — F7 je měl chytit, což udělal):
  1. **v2 location-by-id → 403:** `GET /api/v2/Providers/{pn}/Locations/{ln}` blokován na Gateway — `roleManagmentRBAC.json` stále mapuje odstraněnou flat `/api/v2/Locations/{id}` a nemá GET mapping pro nested routu → fail-closed 403 vs. v1 200. Fix = přidat nested GET mapping. V suite jako fail-forward `StatusRemap(200→403)` divergence.
  2. **v2 anonymous public → 401:** `GET /api/v2/Public/*` chybí v Gateway `PublicEndpointsOptions` anonymous allow-listu (jen `/api/Public`) → 401 vs. v1 200. Fix = přidat prefix `/api/v2/Public`. Test v suite skipnut s důvodem.
- **Phase 2 (zbývá):** write-parity (potřebuje `ResetToSeedAsync`, side-effectful) + DDI/Service/DataStaging S2S read-parity (API-key auth, separátní dimenze); `CyclopsUsage` drop-by-design.
- **Verify:** parity suite zelená; rozhodnutí o cutoffu s daty, ne z paměti.

---

## Otevřené / rizika (průběžně)

- **F0 — ✅ hotovo (vč. F0.3 endpointů + F0.4 parity rows).** Provider→projects větev zahozena; provider→resources/locations queries hotové (F0.2). Pozor na soft-delete predikáty na link entitách (bez global filtru) — review odhalil pre-existující bug.
- **ProjectResource write model (F4) — ✅ hotovo: [ADR-0026](../architecture/adr/0026-projectresource-per-provider-identity.md) + [ADR-0030](../architecture/adr/0030-projectresource-write-model.md).** Per-provider identita `(ProviderId, Name)`; datová oprava (split 3 cross-provider resources) jednorázová. Enforcement seam nakonec přistál jako **read query + use-case resolution** (ADR-0030 zpřesňuje původní "domain port / in-aggregate compare" znění ADR-0026: pure create nemá resolve-then-mutate, `Provider` nemá CLR surrogate id).
- **Admin role write — Core vs Forge (F3):** ověřit vlastnictví před návrhem. Navíc: **role uniqueness** `(UserId, ProjectId)` — viz [ADR-0027](../architecture/adr/0027-v2-natural-key-uniqueness-gaps.md), data mají violujícího uživatele, fix gated dekomisí legacy role-write (F3).
- **Verifikace — ✅ rozhodnuto: posunuta dopředu, průběžně (ne až F7 gate).** Live-data + real-SQL integration testy + data-integrity sweep **per feature**. Důvod: data audit našel 4 latentní bugy (ADR-0026/0027) + dřívější `ORDER BY <VO>.Value` 500-regrese, všechny neviditelné pro in-memory EF testy. F7 zůstává finální brána, ale není jediná verifikace.
- **`ProjectTag.Name` uniqueness — tracked v [ADR-0027](../architecture/adr/0027-v2-natural-key-uniqueness-gaps.md).** Cíl: globální sdílený katalog, unique `Name`. Legacy tag-create povoluje duplicity (data mají DraTag2 2× na 1 projektu) → filtrovaný unique `WHERE IsDeleted = 0` + dedup až při **dekomisi legacy tag writes** (F1). Guard primárně v use-case/agregátu, index jen backstop.
- **Outbox pro HEAppE resource sync — ✅ rozhodnuto: [ADR-0028](../architecture/adr/0028-transactional-outbox-for-domain-events.md); build odložen na F6.** Transactional outbox (event ve stejné transakci = at-least-once, separátní dispatcher s retry, idempotentní konzumenti). Nahrazuje at-most-once in-memory `DomainEventChannel` pro durability-requiring eventy (HEAppE sync). **F5 dodán na in-memory path (parita s ADR-0016)** — prerekvizita = *rozhodnutí* před F5, ne plný build; F6 přepne oba sync eventy (`ResourceAssignmentChangedEvent` + `ProjectResourceAssignmentChangedEvent`) na durable delivery na jednom místě. Best-effort eventy (cache eviction) zůstávají in-memory.
