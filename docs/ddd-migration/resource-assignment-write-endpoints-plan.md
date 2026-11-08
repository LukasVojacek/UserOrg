# Resource-assignment write endpointy — plán migrace

> **Scope:** Migrace legacy `ProjectResourceAssignmentService` (CRUD nad `AssignedLocation`) do modulárního `Core` jako write endpointy.
> **Stav:** ✅ Implementováno (S1–S6 hotové — ADR-0016). Původní datum: 2026-06-09. Viz [v1-retirement-parity.md](./v1-retirement-parity.md).
> **Navazuje na:** provider/location write endpointy (ADR-0011), `core-modules-target-architecture-and-plan.md`.

---

## 0. Doménový kontext (co „resource-assignment" je)

Legacy „ProjectResourceAssignment" = entita **`AssignedLocation`** — alokace **ProjectResource ↔ Location** s parametry (`AllocationUnitType`, `AllocationUnitCount`, specifikace = `AssignedLocationValue`, `AggregationName`, `Description`). **Není** to vazba resource↔projekt (to je `AssignedProjectResourceToProject`).

Modulární doména (`Core.Domain/Provider/`) entity už má; `Location` má částečné write chování (`AddAssignedLocation`/`Update`/`Remove`), ale bez vazby na `ProjectResource`/`AllocationUnitType`, bez specs zápisu a bez invariantů.

---

## 1. Rozhodnutí (decision log)

| # | Rozhodnutí | Důvod |
|---|---|---|
| **R1** | **Provider je aggregate root** transakce; `Location → AssignedLocation` jako child; `ProjectResource` referencovaný cross-aggregate přes id. | Existující kód už `AssignedLocation` modeluje pod `Location` pod `Provider`; sedí na shipnuté provider/location writes (stejný repo, auth, error-mapping). |
| **R2** | HTTP surface **nested pod location**: `providers/{providerName}/locations/{locationName}/assignments[/{id:guid}]`. PUT/DELETE adresují přiřazení **surrogate Guidem**. | Kopíruje agregát i URL rodinu location writes; path-scoping nahrazuje legacy „patří location providerovi?". `AssignedLocation` nemá přirozený klíč — non-unique index `(LocationId, ProjectResourceId)` to dokazuje → surrogate je jediná poctivá identita; read strana ten Guid už vystavuje. |
| **R3** | Autorizace **reuse `ProviderWriteAuthorization.Authorize`** (ADR-0011); RBAC entries pod `provider:update`. | Provider-key přes `providerName` claim, admin cokoli — shodné s location writes. |
| **R4** | HEAppE sync přes **domain event + handler**; **Core-side port** `IResourceSyncTrigger`, most na Heappe channel ve wiring vrstvě. | `IUnitOfWork` dispatch po commitu reprodukuje legacy „save→trigger"; Core nezávisí na Heappe internals („jen port"). |
| **R5** | Invarianty **hybridně**: location-local #2 **in-aggregate** (`Location.AssignResource`), cross-aggregate #1 + read-only ProjectResource **use-case query**. Cross-aggregate ref přes **typed `ProjectResourceId`** (už takto v EF), `AllocationUnitTypeId` int lookup. | Sedí na zavedený vzor (country/tag existence v use-case, invarianty nad načtenými kolekcemi). Vystavení shadow FK = jen EF mapping, **žádná migrace**. |
| **R6** | Specifikace **replace-all in-aggregate** (`AssignedLocation.ReplaceSpecifications`), bez dedup invariantu; `AllocationUnitType` **existence-check v use-case**. | Kopíruje legacy (delete-all+insert, „unique check" jen v komentáři). |
| **R7** | **Plná write parita** (POST/PUT/DELETE); reads mimo scope (resource read přiřazení embeduje). `201` → `Location: resources/{projectResourceId}` + body `{ Id }`. Legacy endpoint **netknutý**, oba běží paralelně. | Standing constraint „nezasahovat do legacy"; Gateway forwarduje `/v2/*`. |

**Invarianty (z legacy):** (#1) ProjectResource nesmí mít non-shared location; (#2) non-shared Location nesmí mít přiřazený resource; (#3) read-only mode guardy (ProjectResource/Location/AssignedLocation).

---

## 2. Plán kroků (prioritizovaný, S1–S6)

### S1 — Doména (jádro) ✅ hotovo (2026-06-10)
- ✅ Vystaveno na `AssignedLocation`: `Id` (Guid, klientsky generovaný v7 — entita adresovatelná před save), `ProjectResourceId` (typed VO), `AllocationUnitTypeId` (int), `Mode` (`EOperationMode`, read-only guard). Na `Location` vystaven `Mode` (insert guard zrcadlí legacy `ThrowIfReadOnly(location.Mode)`). EF konfigurace povýšeny ze shadow na mapped — **žádná migrace**; query čtení `EF.Property<int>(…, "Mode")` přepsáno na `(int)x.Mode` (3 místa).
- ✅ `AssignedLocation.ReplaceSpecifications` (clear + add, bez dedup — legacy parita).
- ✅ Aggregate metody: lokální pravidla na `Location` (`AssignResource` — #2 + location read-only + #1 přes `resourceHasNonSharedLocation` flag; `UpdateAssignment(Guid id, …)`; `RemoveAssignment(Guid id)` — oba s assignment read-only guardem), **tenké wrappery na `Provider`** (`AssignResource/UpdateAssignment/RemoveAssignment(LocationName, …)`) — eventy lze zvedat jen z aggregate rootu.
- ✅ `ProviderErrors`/`Codes`: `location_read_only`, `assignment_read_only`, `resource_already_has_nonshared_location`, `nonshared_location_already_has_resource`.
- ✅ `ResourceAssignmentChangedEvent(ProjectResourceId)` v create/update/delete.
- ✅ **Nad rámec plánu (schváleno):** `AddLocation` přestal auto-vytvářet initial `AssignedLocation` — viz §3; z `AddLocationEndpoint` requestu odstraněna 3 mrtvá pole (`AllocationUnitCount`, `AllocationDescription`, `AggregationName`).
- **Verify:** ✅ 13 nových unit testů bez mocků (invarianty, read-only, replace-all, soft-delete, eventy); `Location.cs`/`AssignedLocation.cs` bez `async`/`I*Handler`; build zelený; testy 266 passed / 8 failed (baseline DI selhání, beze změny).

### S2 — Read/queries
- ✅ Include-set `Provider` repa (`_providerIncludes` → `_providerLocations._locationAllocations._assignedLocationValues`) **hotovo v S1** — agregát po loadu vidí `Locations → AssignedLocations → Values`.
- **Nový kontrakt `IResourceAssignmentQuery`** (write-validační čtení; **oddělený** od `IResourceQuery`, který je caller-scoped read). Tři metody:
  - `GetResourceForAssignmentAsync(ProjectResourceId) → ResourceAssignmentTarget?` — `null` = resource neexistuje/soft-deleted (→ 404; `ProjectResource` má `HasQueryFilter(!IsDeleted)`). Jinak nese `Mode` (read-only guard) **a** `HasNonSharedLocation` (#1) → jeden round-trip pro celou assign-validaci. Update/Delete cesta tuto query nepotřebuje.
  - `AllocationUnitTypeExistsAsync(int) → bool` — neznámý typ → 400 (assign + update).
  - `GetProjectsUsingResourceAsync(ProjectResourceId) → IReadOnlyList<ResourceProjectRef(ProjectShortName, ResourceName)>` — pro S3 HEAppE handler; 1:1 mapuje na `WorkItem`y. Zůstává v S2 (čistá read query, S3 ji jen konzumuje).
- **Implementace** `ResourceAssignmentQuery` (query objekt ve stylu `ResourceDetailsQuery` — injektuje `IDataQuery<ProjectResource>`, `IDataQuery<AssignedLocation>`, `IDataQuery<AllocationUnitType>`, `IDataQuery<AssignedProjectResourceToProject>`; staged load + shadow-FK joiny přes `EF.Property`). Registrace `AddScoped<IResourceAssignmentQuery, ResourceAssignmentQuery>()` v `ServiceCollectionExtensions`.
- **#1 filtruje aktivní jen** (`!AssignedLocation.IsDeleted && !Location.IsDeleted`) — **úmyslná odchylka od legacy**, kde `IsDeleted` u #1 chybí (soft-deleted alokace by jinak navždy rezervovala non-shared lokaci). Zaznamenat do ADR-0016 dodatku.
- **Verify:** unit testy `ResourceAssignmentQuery` přes EF in-memory provider (harness dle `RequestQueryTests`): target found/not-found, `Mode` propagace, #1 true/false vč. soft-delete hrany, allocation-type exists/missing, projects-using-resource mapování + `!ap.IsDeleted`.

### S3 — HEAppE port + post-commit handlery ✅ hotovo (2026-06-10)
- ✅ Doménový marker `IResourceScopedEvent { ProjectResourceId }` (`Core.Domain/Provider/ProviderEvents.cs`); `ResourceAssignmentChangedEvent` ho implementuje (zrcadlí `IProjectScopedEvent`).
- ✅ Core-owned port `IResourceSyncTrigger.EnqueueResourceSyncAsync(ProjectShortName, ResourceName, ct)` (public; `Core/Features/Provider/ResourceAssignment/`).
- ✅ **Dva handlery** (grain codebase, oddělená cache evikce od orchestrace): `ResourceSyncHandler` (`IResourceAssignmentQuery.GetProjectsUsingResourceAsync` → per projekt `EnqueueResourceSyncAsync`) + generický `ResourceCacheEvictionHandler<TEvent : IResourceScopedEvent>` (evict `resource:{id}` = `CacheTags.ProjectResource`). Oba explicitně registrovány v `AddCore` (auto-scan běží jen nad `Core.Domain`).
- ✅ Adaptér `ChannelResourceSyncTrigger` v `Userorg.Adapter/Heappe/` přemostí port na existující Heappe `Channel<WorkItem>` (HEAppE modul **netknutý**, jen druhý producent). Nový `ProjectReference Userorg.Adapter → Core.csproj` (jediná třída vidoucí port i kanál); registrace v `HeappeAdapterExtensions`.
- ✅ **Cache:** evikce jen v2 output cache `resource:{id}`. Legacy `HybridCache TemplatesInfo` staleness (~5 min TTL) tolerována — bereme z legacy jen flow, ne implementaci; Core se neváže na legacy cache.
- **Verify:** ✅ 3 unit testy handlerů (sync per projekt + zero-case, cache evict tag) v `Core.UnitTests`; 1 adaptér test (real `Channel<WorkItem>`) v `Heappe.Adapter.Tests`; build zelený; `Core.UnitTests` 276 passed / 8 failed (baseline DB, 0 regresí). **Produkční trigger až s S4** (endpoint zvedá event); S3 verifikováno unit testy.

### S4 — Endpointy ✅ hotovo (2026-06-10)
- ✅ `POST/PUT/DELETE` pod `providers/{providerName}/locations/{locationName}/assignments[/{id:guid}]` (`AssignResourceEndpoint`/`UpdateAssignmentEndpoint`/`RemoveAssignmentEndpoint` v `Features/Provider/ResourceAssignment/`); šablona = `AddLocationEndpoint`.
- ✅ `ProviderWriteAuthorization.Authorize`; `result.Error.ToProblem()` přes `ProviderErrorMapping`; POST `201` → `CreatedAtRoute("GetResourceByIdV2", {id})` + body `{ Id }` (assignment Id je klientsky generovaný → bez read-backu); PUT/DELETE `204`; `DisabledWhen("ReadOnlyMode")`; `MapDocumentation`.
- ✅ **Read-only zpřísnění (odchylka od legacy, schváleno):** per-entity `ProjectResource.Mode == ReadOnly` se vynucuje na **všech třech** modifikačních verbách (legacy jen na POST), z `ResourceAssignmentTarget.Mode`. Na PUT/DELETE kontrola **po** in-memory mutaci a **před** commitem (rejected request nic neuloží). **Soft-deleted/chybějící resource neblokuje** (`GetResourceForAssignmentAsync` → `null` → projde) — orphan assignment musí zůstat smazatelný. POST navíc dědí location read-only z agregátu; resource read-only → `400`.
- ✅ **Doplněno mapování** `AssignedLocationNotFound → 404`, `AssignedLocationAlreadyDeleted → 409` v `ProviderErrorMapping` (chyby zavedené v S1, poprvé vystavené přes endpoint až v S4 — dříve padaly do `_ => 400`).
- ✅ Registrace v `Composition/EndpointsExtensions.cs` (pod v2).
- **Verify:** ✅ build (Core + API) zelený; 18 handler unit testů (`ResourceAssignmentEndpointTests`) — happy path všech tří verbů, auth/403, 404 (provider/resource/assignment), read-only resource 400 + no-save na POST/PUT/DELETE, soft-deleted resource → projde, #1 invariant 400, allocation-type 400, validace; `Core.UnitTests` 294 passed / 8 failed (baseline live-SQL DB, 0 regresí).

### S5 — RBAC ✅ hotovo (2026-06-10)
- ✅ `roleManagmentRBAC.json` `endpointMappings`: 3 v2 entries (POST `…/assignments`, PUT/DELETE `…/assignments/{id}`), perm `perm:provider:update` — zrcadlí location-write entries (plain param names bez route-constraintů, dle konvence souboru). `count` 155 → 158.
- **Verify:** ✅ JSON validní; `count` == `items.Count` (158); entry pro každou ze 3 metod+route; permission a tvar shodné s `v2 Add/Update/Remove Location`.

### S6 — Build + testy ✅ hotovo (2026-06-10)
- ✅ Plný build `UserOrganization.slnx` — 0 errors. `Core.UnitTests` 296 passed / 8 failed (jen známé live-SQL `*DatabaseAccessTests`, 0 regresí). RBAC/route alignment: 3 routy ↔ 3 entries, `count` konzistentní.
- **Verify:** ✅ zelený build, 0 regresí proti baseline. **Celá migrace ADR-0016 (S1–S6) dokončena.**

---

## 3. Otevřené / k pozdějšímu zvážení
- ~~Auto-vytvořený prázdný „initial" `AssignedLocation` z `AddLocation` — řešen jako koexistence (R5 wrinkle i).~~ **Vyřešeno v S1 (2026-06-10): auto-tvorba odstraněna.** Ukázalo se, že nikdy nemohla fungovat — `ProjectResourceId`/`AllocationUnitTypeId` jsou NOT NULL FK sloupce, initial entry by vložil `Guid.Empty`/`0` → FK violation při každém `POST .../locations` proti reálné DB (S7 verifikace běžela jen build+unit testy). Legacy `InsertLocationAsync` žádný entry nevytváří; lokace začíná bez alokací a jediná cesta vzniku přiřazení je `AssignResource` s reálným resourcem.
- **Pro S3 (HEAppE handler):** legacy po save kromě HEAppE sync evictuje i `TemplatesInfo` cache tag per dotčený projekt (`cache.RemoveByTagAsync(CacheTags.TemplatesInfo(...))`). v2 event handler musí evictovat svůj protějšek (output cache `GET v2/Projects/{sn}/Templates`).
- Standalone GET-assignment endpoint (zatím netřeba — resource read přiřazení vrací).
