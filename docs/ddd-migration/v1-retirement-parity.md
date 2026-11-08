# v1 → v2 retirement parity checklist

> **Client-facing guide:** consumers migrating their calls should read
> [Migrating from v1 to v2 endpoints](../guides/v1-to-v2-migration.md) — a normalized route map
> generated from the committed OpenAPI snapshots. This document is the internal ledger behind it.

> **Účel:** Jednoznačná evidence **každé** legacy `/api/*` routy a její dispozice ve světě v2.
> Cíl je [úplná parita (varianta „a")](#cíl): v1 zůstane nějakou dobu jako deprecated, pak se **kompletně odstřihne** — a v ten moment musí být tato tabulka „celá zelená": každá routa je buď **portovaná**, **přesunutá do jiného modulu**, nebo **vědomě zahozená s odůvodněním**.
> **Stav:** Probíhá; F0–F5 + F2/F3 + DDI + durable outbox (#237, ADR-0054) + F7 **reads**-parity (#238, ADR-0055) hotové. **Poslední ověření proti kódu: 2026-07-08** (viz [ověření níže](#ověření-2026-07-07)). Předchozí datum: 2026-06-27.
> **Navazuje na:** [core-modules-target-architecture-and-plan.md](./core-modules-target-architecture-and-plan.md) (interní architektura — P0–P3 hotovo), [ADR-0017](../architecture/adr/0017-ddi-service-api-as-separate-module.md) (DDI modul).

---

## Cíl

„Hotovo" = legacy `src/Core` lze smazat. To je **přísnější** než „každý endpoint reimplementován": každá v1 routa musí být **portovaná NEBO re-homed NEBO formálně zahozená**. Architektonický refaktor (P0–P3) je hotový pro vše, co už migrované je; tento dokument sleduje **druhou osu — pokrytí**.

**Legenda dispozic:**
- ✅ **ported** — existuje v2 ekvivalent pod `/v2/*`.
- 🟦 **re-home** — parita patří do jiného modulu (Heappe / DDI), ne do Core.
- 🟥 **Core backlog** — chybí, musí se postavit v Core v2 (buckety A/C).
- ⚪ **drop** — vědomě neportujeme; odůvodnění v poznámce.

---

## Souhrn

| Bucket | Co | Routy | Vlastník |
|---|---|---|---|
| **F0 — Role-based reads** | `Administration/*` GET (admin-wide) — tier-covered přes `ResolveReadScope` (ADR-0018), F0.3 hotovo | 8 | Core v2 ✅ |
| **A — Core business backlog** | ProjectResource CRUD (F4), resource↔project assignment (F5), ProjectTag delete (F1), admin role mgmt (F3) | 9 | Core v2 ✅ |
| **C — Core (keep) backlog** | CyclopsUsage (drop, Gateway), ResetAllCaches (F2) | 2 | Core v2 ✅ |
| **B1 — Heappe** | file download `ListChangedFilesForJob` | 1 | Heappe modul ✅ **hotovo** (relokováno do modulu, legacy smazán — ADR-0053, #236) |
| **B2+B3 — DDI** | DataStaging keys, SyncDetails, ProjectsInfo — modul `Modules/Services/Ddi/UserOrg.Ddi` existuje, všech 5 rout namapováno pod `api/v2/DataStaging` + `api/v2/Service` | 5 | DDI ✅ (ADR-0017) |
| **drop** | standalone GET assignment (list + by-id) | 2 | ⚪ pokryto resource-readem |
| **ported** | zbytek | ~42 | Core v2 ✅ |

**Verdikt (2026-07-07, aktualizováno): všechny legacy routy portovány / re-homed / vědomě zahozeny — B1 dokončeno.** Oproti stavu 2026-06-27 přibyly hotové: **F2** (ResetAllCaches `DELETE api/v2/Caches`, CyclopsUsage doc-only), **F3** (admin role mgmt — oba endpointy `PUT Users/{idp}/ProviderAdmin/{pn}` + `DELETE Users/{idp}/ProviderAdmin`, wired + 10 testů), **F4/F5** (ProjectResource CRUD + assignment), **DDI** (celý modul), **B1** (Heappe `ListChangedFilesForJob` relokován do modulu, legacy smazán — ADR-0053, #236). **Zbývá k cutoffu:** (1) ~~durable outbox~~ ✅ **hotovo (#237, ADR-0054)** — `IDurableEvent` + `OutboxDispatcherService`, (2) ~~F7 parity suite~~ ✅ **F7 reads-parity HOTOVÁ (#238, ADR-0055)** — live side-by-side v1↔v2 přes Gateway na reálném SQL (`Tests/E2E/ParityTests`), všechny GET/list read páry; **writes + DDI/S2S reads zbývají jako Phase 2** (viz [F7 findings](#f7-findings)), (3) aplikace deferred unique indexů po dekomisi legacy writerů. Sekvenci řeší [next-phase-plan.md](./next-phase-plan.md).

---

## Kompletní mapa rout

### Project (`/api/Project`)
| v1 | disp | v2 / pozn. |
|---|---|---|
| `GET Project/` | ✅ | `GetProjects` |
| `POST Project/` | ✅ | `CreateProject` |
| `PUT Project/` | ✅ | `UpdateProject` |
| `GET Project/{sn}/Users` | ✅ | `GetProjectUsers` |
| `GET Project/{sn}/ValidRoles` | ✅ | `GetValidRoles` |
| `GET Project/TemplatesInfo/{sn}` | ✅ | `GetProjectTemplates` |
| `GET Project/ExtendedProjectInfo` | ✅ | `GetExtendedProject` |
| `PUT Project/{sn}/User/{idp}` | ✅ | `AssignProjectUser` |
| `DELETE Project/{sn}/User/{idp}` | ✅ | `UnassignProjectUser` |
| `PUT Project/{sn}/User/{idp}/Assign/{roleId}` | ✅ | `ChangeProjectUserRole` |
| `PUT Project/{sn}/ProjectResource/{name}` | ✅ **A2 (F5)** | `PUT v2/Projects/{sn}/Providers/{pn}/Resources/{name}` (provider-nested, ADR-0026) |
| `DELETE Project/{sn}/ProjectResource/{name}` | ✅ **A2 (F5)** | `DELETE v2/Projects/{sn}/Providers/{pn}/Resources/{name}` |
| `PUT Project/{sn}/ProjectTag/{id}` | ✅ | `AssignProjectTag` |
| `DELETE Project/{sn}/ProjectTag/{id}` | ✅ | `UnassignProjectTag` |
| `DELETE Project/{sn}` | ✅ | `DeleteProject` |

### ProjectResource (`/api/ProjectResource`)
| v1 | disp | v2 / pozn. |
|---|---|---|
| `GET /` | ✅ | `GetResources` — nested pod providera: `GET v2/Providers/{providerName}/Resources` (ADR-0022). **F0.3 tier (2026-06-27):** admin vidí vše, user caller-scoped ∩ provider ([ADR-0018](../architecture/adr/0018-read-scope-tiers.md)) — dřív byl jen caller-scoped (admin neviděl nic). *(2026-06-16 byl chvíli `500` kvůli `orderby ….Value`, opraveno týž den — viz [systemic note](./legacy-to-refactored-endpoint-migration.md#verification-notes-gateway-round-trip-project-testproject-2026-06-16--systemic-order-by-value-objectvalue-regression-fixed); live ověřeno)* |
| `GET /{id}` | ✅ | `GetResource` — natural key `(providerName, resourceName)`: `GET v2/Providers/{providerName}/Resources/{resourceName}` (ADR-0022). Jméno není unikátní napříč providery, proto provider kvalifikuje (wrong provider → `404`). Live ověřeno. |
| `POST /` | ✅ **A1 (F4)** | `POST v2/Providers/{pn}/Resources` (per-provider identita, ADR-0030) |
| `PUT /` | ✅ **A1 (F4)** | `PUT v2/Providers/{pn}/Resources/{name}` (name immutable) |
| `DELETE /{id}` | ✅ **A1 (F4)** | `DELETE v2/Providers/{pn}/Resources/{name}` (soft-delete) |

### ProjectResourceAssignment (`/api/ProjectResourceAssignment`) — resource↔location
| v1 | disp | v2 / pozn. |
|---|---|---|
| `GET /` | ⚪ drop | přiřazení vrací resource-read (ADR-0016) |
| `GET /{id}` | ⚪ drop | dtto |
| `POST /` | ✅ | `AssignResource` (S4) |
| `PUT /` | ✅ | `UpdateAssignment` (S4) |
| `DELETE /{id}` | ✅ | `RemoveAssignment` (S4) |

### ProjectTag (`/api/ProjectTag`)
| v1 | disp | v2 / pozn. |
|---|---|---|
| `GET /` | ✅ | `GetProjectTags` (`GET v2/ProjectTags`) |
| `GET /{id}` | ✅ | `GetProjectTag` (`GET v2/ProjectTags/{tagName}`, GUID→name) |
| `POST /` | ⚠️ částečně | v2 tvoří tag jen v kontextu projektu (`POST v2/Projects/{sn}/Tags`); samostatný globální `POST /project-tags` v2 nemá — vytvoření mimo projekt se nepoužívá |
| `PUT /` | ⚪ **drop (G3)** | v2 tag je name-only; rename = delete + recreate ([f1 plan](./f1-projecttag-delete-plan.md)) |
| `DELETE /{id}` | ✅ **A3 (F1)** | `DELETE v2/ProjectTags/{tagName}` (blocks-if-in-use → 409, G2) |

### Provider (`/api/Provider`)
| v1 | disp | v2 / pozn. |
|---|---|---|
| `GET /` · `GET /{id}` · `POST /` · `PUT /` · `DELETE /{id}` | ✅ | `GetProviders` / `GetProvider` / `CreateProvider` / `UpdateProvider` / `DeactivateProvider` (+`ReactivateProvider` navíc) |

### Location (`/api/Location`)
| v1 | disp | v2 / pozn. |
|---|---|---|
| `GET /` · `GET /{id}` · `POST /` · `PUT /` · `DELETE /{id}` | ✅ | `GetLocations` / `GetLocation` / `AddLocation` / `UpdateLocation` / `RemoveLocation` |

### User / UserInfo
| v1 | disp | v2 / pozn. |
|---|---|---|
| `GET User/Preferences` · `PUT User/Preferences` | ✅ | `GetUserPreferences` / `UpdateUserPreferences` |
| `GET UserInfo/` · `GET UserInfo/Extended` | ✅ | `GetUserInfo` / `GetExtendedUserInfo` |

### Requests (`/api/Requests`)
| v1 | disp | v2 / pozn. |
|---|---|---|
| `GET /` · `GET SentRequests` · `POST /` · `PUT {id}/Accept` · `PUT {id}/Reject` | ✅ | `GetRequests` / `GetSentRequests` / `CreateRequest` / `AcceptRequest` / `RejectRequest` |

### Public (`/api/Public`)
| v1 | disp | v2 / pozn. |
|---|---|---|
| `POST Handle/{actionLink}` · `GET RequestInfo/{id}` | ✅ | `HandleActionLink` / `GetPublicRequestInfo` |

### Administration (`/api/Administration`)
| v1 | disp | v2 / pozn. |
|---|---|---|
| `GET Project` · `GET ProjectTag` · `GET User` · `GET Location` · `GET Location/{id}` · `GET Provider` · `GET Provider/{id}` · `GET Requests` | ✅ **F0** (hotovo) | **F0.3 dokončeno (2026-06-27):** všech 8 rout je tier-covered přes `caller.ResolveReadScope(roles)` ([ADR-0018](../architecture/adr/0018-read-scope-tiers.md)) — admin vidí vše, provider-identita svá data, user project-scope. `Project`/`Requests` rozšířeny o admin větev; `Provider`/`Location` (vč. by-id `GetProvider`/`GetLocation`) **zúženy** — private data už neunikají non-adminovi, reachable přes uživatelovy projekty (404-hiding: missing == out-of-scope). `ProjectTag` (global) a `User` (admin-gated) beze změny — parita. Detaily: [f0-role-based-reads.md](./f0-role-based-reads.md). |
| `PUT PromoteToProviderAdmin` | ✅ **A4 (F3)** | `PUT v2/Users/{idpUserId}/ProviderAdmin/{providerName}` — Admin-only, `DomainUser.PromoteToProviderAdmin`, 5 testů |
| `PUT ResetToPlatformUser` | ✅ **A4 (F3)** | `DELETE v2/Users/{idpUserId}/ProviderAdmin` — unassign providers + `ResetToPlatformUser`, idempotentní, 5 testů |
| `DELETE Cache` | ✅ **C2 (F2)** | `DELETE v2/Caches` — Admin-only, evict-all přes `CacheTags.All` |

### Enums (`/api/Enums`)
| v1 | disp | v2 / pozn. |
|---|---|---|
| `GET LocationTypes` · `GET RequestStates` · `GET RequestTypes` · `GET OperationModeResponses` | ✅ | `GetLocationTypes` / `GetRequestStates` / `GetRequestTypes` / `GetOperationModes` (+`GetProjectDomainTypes` navíc) |

### Other (`/api/Other`)
| v1 | disp | v2 / pozn. |
|---|---|---|
| `GET Country` | ✅ | `GetCountries` |
| `GET Statistics` | ✅ | `GetStatistics` |
| `GET CyclopsUsage/{sn}/{res}` | ⚪ **drop (C1/F2)** | žádný reálný v2 endpoint — servíruje **Gateway** (`CyclopsForwardingExtensions` → externí `cyclops-api`); jen OpenAPI záznam ve v2-User dokumentu přes `CyclopsUsageDocumentTransformer`. Legacy 202 handler je mrtvý stub. |

### Service (`/api/Service`) — S2S, provider API-key
| v1 | disp | v2 / pozn. |
|---|---|---|
| `GET api/version` | ✅ | `GetVersion` |
| `GET SyncDetails` | ✅ **B3 (DDI)** | `GET v2/Service/SyncDetails` — `GetSyncDetailsEndpoint`, `ISyncDetailsQuery` + Forge.Core `IRoleListService`, věrný wire-shape (vč. překlepu `Descritpion`); issue #224 |
| `GET ProjectsInfo` | ✅ **B3 (DDI)** | `GET v2/Service/ProjectsInfo` — `GetProjectsInfoEndpoint`, `IDdiProjectsInfoQuery` |

### Heappe (`/api/Heappe`)
| v1 | disp | v2 / pozn. |
|---|---|---|
| `GET ListChangedFilesForJob/{sn}/{res}/{jobId}` | ✅ **B1 → Heappe (hotovo)** | relokováno do `src/Modules/Integrations/Heappe/Features/LogRetrieval/DownloadChangedFilesEndpoint.cs` (Path A: `IUserorgService.GetProjectResourceAllocationWithLocationsAsync` → `HeappeInstance.Uri` + `IHeappeService`); token přes sdílený `OriginalBearerToken` binder (ADR-0053); legacy `HeappeEndpoint.cs` smazán. #236 |

### DataStaging (`/api/DataStaging`) — Vault, DDI
| v1 | disp | v2 / pozn. |
|---|---|---|
| `GET WorkerData` | ✅ **B2 (DDI)** | `GET v2/DataStaging/WorkerData` |
| `GET DataStagingLocations` | ✅ **B2 (DDI)** | `GET v2/DataStaging/DataStagingLocations` |
| `POST DataStagingLocations` | ✅ **B2 (DDI)** | `POST v2/DataStaging/DataStagingLocations` (bulk Vault update) |

---

## Core backlog (A + C) — ✅ VŠE HOTOVO (2026-07-07)

| ID | Práce | Routy | Stav |
|---|---|---|---|
| **A1** | ProjectResource CRUD writes | `POST/PUT/DELETE ProjectResource` | ✅ F4 (ADR-0030) |
| **A2** | resource↔project assignment | `PUT/DELETE Project/{sn}/ProjectResource/{name}` | ✅ F5 (ADR-0031) |
| **A3** | ProjectTag delete | `DELETE ProjectTag` | ✅ F1; `PUT` (update) ⚪ drop (G3) |
| **A4** | admin role management | `PUT Promote…` / `Reset…` | ✅ F3 (oba endpointy, 10 testů) |
| **C1** | CyclopsUsage | `GET Other/CyclopsUsage/…` | ⚪ drop — Gateway servíruje, jen OpenAPI záznam (F2) |
| **C2** | ResetAllCaches | `DELETE Administration/Cache` | ✅ F2 (`DELETE v2/Caches`) |

## Mimo-Core parita (před cutoffem)

| ID | Práce | Cíl | Stav |
|---|---|---|---|
| **B1** | Heappe file download `ListChangedFilesForJob` | Heappe modul | ✅ **hotovo** — relokováno do modulu, legacy smazán (ADR-0053, #236) |
| **B2** | DataStaging Vault keys | DDI modul (ADR-0017) | ✅ hotovo |
| **B3** | SyncDetails, ProjectsInfo | DDI modul (ADR-0017) | ✅ hotovo |

---

## Ověření 2026-07-07

Plošné ověření celého `api/*` (61 legacy rout, 15 souborů) proti `api/v2/*` + modulům, čtené přímo ze zdrojů (`src/Core/API/`, `EndpointsExtensions.cs`, `DdiModuleExtensions.cs`, `HeappeAdapterExtensions.cs`, `Program.cs`). Klíčová zjištění, která **měnila** předchozí stav docu:

- **F2, F3, F4, F5, DDL/DDI hotové** — parity doc je měl označené jako `🟥`/`🟦` pending. Opraveno výše.
- **B1 dokončeno (2026-07-07, #236):** `GET api/Heappe/ListChangedFilesForJob` relokován do Heappe modulu, legacy `HeappeEndpoint.cs` smazán. Ostatní legacy-only řádky (`GET ProjectResourceAssignment/{id}`, `PUT/POST ProjectTag` global) jsou **drop-by-design**, ne backlog — žádná Core-scope legacy routa už nezbývá.
- **F7 reads-parity suite HOTOVÁ (2026-07-08, #238, ADR-0055).** Postaveno `Tests/E2E/ParityTests`: **live side-by-side** porovnání v1 `/api/*` ↔ v2 `/api/v2/*` proti **reálnému SQL přes Gateway**. Sdílená `E2E.Fixtures` (vyextrahovaná z `EndpointsTests`, které zůstávají zelené), deklarativní `ParitySpec`/`Divergence` model, rekurzivní `JsonShapeComparer` (structural shape + must-match ordering keys). Pokryty všechny GET/list read páry vč. 5 regresních list endpointů (regression proof pro `ORDER BY <VO>.Value` 500). Suite našla 2 reálné produktové defekty (viz [F7 findings](#f7-findings)). **Zbývá Phase 2:** write-parity + DDI/S2S read-parity.
- **Oba stacky pořád běží** — `Program.cs:183-222` registruje legacy `AddUserOrgDataAccess` + 21 repozitářů + 11 services; `:393-397` mapuje všechny legacy `api/*`; `:400-401` mapuje v2 + moduly. Diskriminátor = prefix `api/v2/`.
- **⚠️ Live regrese:** `CoveredEndpointRoleMatrixTests.cs:343` hard-skipuje test s TODO, že legacy `POST /api/Project/` vrací **404 místo 201** v E2E prostředí. Legacy create-project je rozbitý (nízká priorita — v1 se stejně odstřihuje, ale je to signál).

---

## F7 findings

Živá reads-parity suite (#238, ADR-0055) odhalila **2 reálné produktové defekty**. Oba jsou **separátní Gateway-config fixy, mimo scope F7 test-authoringu** — úkolem F7 bylo je chytit, což udělal. Zaznamenáno zde, aby se neztratily:

1. **v2 location-by-id → 403.** `GET /api/v2/Providers/{pn}/Locations/{ln}` je na Gateway blokován: `roleManagmentRBAC.json` stále mapuje odstraněnou flat routu `/api/v2/Locations/{id}` a **nemá žádný GET mapping pro nested routu** → Gateway fail-closed 403, zatímco v1 `GET /api/Location/{id}` vrací 200. **Fix = přidat nested GET mapping** do RBAC souboru. V suite zakódováno jako fail-forward `StatusRemap(200→403)` known-divergence (`ResourceProviderLocationByIdParityTests`) — suite zůstává zelená, divergence zůstává viditelná.
2. **v2 anonymous public → 401.** `GET /api/v2/Public/*` chybí v Gateway `PublicEndpointsOptions` anonymous allow-listu (uveden jen `/api/Public`) → 401 vs. v1 200. **Fix = přidat prefix `/api/v2/Public`.** Test v suite skipnut s důvodem (`PublicParityTests`) dokud se allow-list neopraví.

---

## Otevřené / rizika

- **⚠️ Parita je jen bodově ověřená, ne plošně testovaná.** Vše migrované bylo ověřeno **unit** testy, ne automatizovanými **v1↔v2 behavioral parity** testy. **Bodové živé porovnání (2026-06-16, projekt `testproject`, přes Gateway)** potvrdilo datovou paritu pro `projects`(list)/`extended`/`users`/`valid-roles`/`countries`/`statistics` (jen dokumentované shape změny) a **odhalilo systémovou regresi: pět list endpointů (`resources`, `providers`, `locations`, `requests`, `Requests/Sent`) vracelo `500`** kvůli netranslatovatelnému `orderby <valueObject>.Value` v paged queries. **Opraveno** (řazení podle value-objektu přímo + `IComparable` na `ResourceName`/`ProviderName`/`LocationName`/`ProjectId`), živě ověřeno `200`. Závěr: před odstřižením v1 je potřeba **automatizovaný kontraktní/shape srovnávací test** (proti reálnému SQL, ne EF in-memory) aspoň pro vysoko-frekventované endpointy — in-memory unit testy tuhle SQL-translation regresi nechytily, ruční bodové porovnání ano. Detaily: [systemic orderby note](./legacy-to-refactored-endpoint-migration.md#verification-notes-gateway-round-trip-project-testproject-2026-06-16--systemic-order-by-value-objectvalue-regression-fixed).
- **✅ vyřešeno (potvrzeno jako gap).** Admin-wide read sémantika: ověřeno, že v2 reads jsou **vždy caller-scoped bez admin bypassu** → admin nevidí vše. Eskalováno na **F0** v [next-phase-plan.md](./next-phase-plan.md) (explicitní per-role větvení v `HandleAsync`, Admin + ProviderAdmin tiers).
- **Routy mimo `/api/Heappe` a `/api/DataStaging`** (CommandTemplates, Partitions, Cyclops forwarding) jsou už dnes v modulech Heappe / Gateway — mimo tento dokument (sledují legacy Core, ne moduly).
