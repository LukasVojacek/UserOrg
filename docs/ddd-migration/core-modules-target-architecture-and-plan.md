# Core moduly — cílová architektura a refaktoring plán

> **Scope:** Tři projekty refaktoru v `src/Modules/`: **`Core`**, **`Core.Domain`**, **`Core.DataAccess`**.
> **Účel dokumentu:** Zachytit cílovou architekturu, rozhodnutí o hranicích projektů a konkrétní, ověřitelný plán kroků. Slouží jako referenční bod pro další práci na refaktoru.
> **Stav:** ✅ P0–P3 hotovo (viz §8 a stavové sloupce P3 tabulky). Původní datum: 2026-05-28. Sekce 3 (Zhodnocení současného stavu) je **výchozí assessment** — nálezy D-1/DA-1/C-1 atd. popisují stav *před* refaktorem a §4/§8 je řeší; ber je jako historický kontext, ne otevřený stav.

---

## 1. Cílová architektura

CQRS-laděný hexagon rozdělený do tří projektů s **lineárním směrem závislostí**:

```
Core  ──►  Core.DataAccess  ──►  Core.Domain
(use-cases / API)   (persistence + read)   (write model)
```

| Projekt | Zodpovědnost | Strana |
|---|---|---|
| **Core.Domain** | Čisté DDD entity / agregáty, value objekty, domain eventy, porty (`IRepository`, `IUnitOfWork`). Žádné EF, žádné HTTP, synchronní invarianty. | write model |
| **Core.DataAccess** | Připojení k DB, EF konfigurace **přímo nad DDD entitami**, write adaptéry (impl. `IRepository`), **read queries** (`IDataQuery`, typované query objekty) + jejich read kontrakty a DTO. | persistence + read |
| **Core** | Use-cases = minimal-API endpointy + Request/Response DTO, projekce/mappery, output cache, composition (`AddCore`, `MapCoreEndpoints`). | API |

### Datové cesty

- **Write:** Endpoint (`Core`) → use-case → `IRepository` port (`Core.Domain`) → `DatabaseContext` repo (`Core.DataAccess`) → mutace agregátu (`Core.Domain`) → `IUnitOfWork` save.
- **Read:** Endpoint (`Core`) → query port (`Core.DataAccess`) → query objekt (`Core.DataAccess`) → read DTO → projekce (`Core`) → Response.

### Princip tranzitivních referencí

ProjectReference je v SDK-style projektech **tranzitivní**. `Core` proto i bez přímé reference na `Core.Domain` uvidí jeho public typy (`IRepository`, value objekty, agregáty, event-handlery) přes `Core.DataAccess`. Write-cesta zůstává funkční, závislost ale teče lineárně.

---

## 2. Rozhodnutí (decision log)

### D1 — Read kontrakty a read DTO patří do `Core.DataAccess`

**Kontext:** `Core` má referencovat pouze `Core.DataAccess`. Query, které use-case volá, musí být viditelné z `Core.DataAccess`, a jeho implementace tam už je.

**Rozhodnutí:** Read query **porty** (`IUserInfoQuery`) i read **DTO** (`UserDetails`, `UserExtendedDetails`, …) přesunout z `Core.Domain` do `Core.DataAccess`. Read strana vlastní své kontrakty.

**Důsledek:** `Core` ztrácí přímou referenci na `Core.Domain` a získává referenci na `Core.DataAccess`.

**Zamítnuté alternativy:**
- *Read DTO do `Core`* — porty by zůstaly sdílené, ale read model by se tříštil mezi dva projekty.
- *Nechat v `Core.Domain`* — doména by věděla o read/endpoint věcech (`EndpointQueries` namespace uvnitř domény je už dnes záměna vrstev).

### D2 — EF repozitáře zůstávají v `Core.DataAccess`

Write *implementace* (`DatabaseContext.*Repository`) jsou adaptéry a patří do `Core.DataAccess`. V `Core.Domain` zůstává jen entita + port `IRepository`. (Pozn.: dřívější formulace „Core.Domain = entity + write impl" znamená write *model*, ne EF kód.)

---

## 3. Zhodnocení současného stavu

Legenda závažnosti: 🔴 blokující čistotu architektury · 🟠 střední · 🟡 drobné.

### 3.1 Core.Domain — vs. „čistý write model"

**Drží:** typované ID value objekty, factory `Create`, `private set`, domain eventy, `AggregateRoot`.

| # | Sev | Nález | Cíl |
|---|---|---|---|
| D-1 | 🔴 | Async + DB I/O uvnitř agregátu — `Project.AddTagAsync(TagName, IProjectTagHandler, UserId)`, `RemoveTagAsync`, `AddProjectResource` awaitují handler sahající do DB (`Core.Domain/Project/Project.cs:140-199`). | Agregát mutuje už načtené kolekce synchronně (`AddTag(ProjectTag)`); „ensure/load" patří do use-case nebo repo. |
| D-2 | 🟠 | Persistence porty modelované jako doménové — `IProjectTagHandler`, `IProjectResourceHandler`, `IProjectCreationHandler`. | Po fixu D-1 většina zmizí. |
| D-3 | 🟠 | Read kontrakty ve write modelu — `EndpointQueries/IUserInfoQuery.cs` + `User/UserInfoContracts.cs`. Název `EndpointQueries` v doméně = záměna vrstev. | Přesunout do `Core.DataAccess` (viz D1, krok P0-A). |
| D-4 | 🟡 | `WellKnownRoleOptions.cs` (Options/konfigurace) v rootu domény. | Přesunout mimo doménu (Core nebo DataAccess composition). |

### 3.2 Core.DataAccess — vs. „connection + config nad DDD entitami + read"

**Drží:** read side (`IDataQuery<T>` + `DataQuery<T>` + `UserDetailsQuery`) je čistá CQRS-read implementace; write repo `DatabaseContext.ProjectRepository` načítá agregát s includy.

| # | Sev | Nález | Cíl |
|---|---|---|---|
| DA-1 | 🔴 | Duplicitní konfigurace/entity — `Configurations/*Configuration` (nad DDD entitami) vs `Persistence/Configurations/*RecordConfiguration` + `Persistence/Entities/*Record`; dva kontexty `DatabaseContext` vs `MigrationDbContext`. | Ponechat jen config přímo nad DDD entitami; smazat celou `Persistence/Entities/*Record` + `*RecordConfiguration` sadu + `MigrationDbContext` po ověření, že je nikdo nepoužívá. |
| DA-2 | 🔴 | csproj vyřazuje většinu `Queries/` z kompilace — `<Compile Remove="Queries/**"/>` + selektivní include jen `DataQuery.cs` a `UserDetailsQuery.cs`. `*QueryExtensions` se fyzicky nekompilují. | Buď dokončit a zahrnout, nebo smazat. Projekt kompiluje všechny své soubory. |
| DA-3 | 🟠 | Read side z poloviny — stavěná cesta `IDataQuery<T>` + typované query objekty; vyřazené `*QueryExtensions` jsou nejspíš starší přístup. | Vybrat jeden tvar read-query a aplikovat konzistentně. |
| DA-4 | 🟡 | Magic-string includy `.Include("_projectTags")` nad privátními poli (`DatabaseContext.ProjectRepository.cs:9-11`). | Centralizovat include-set + komentář. |
| DA-5 | 🟡 | `EnableSensitiveDataLogging()` + `EnableDetailedErrors()` natvrdo. | Env-only guard (dev). |

### 3.3 Core — vs. „use-cases / endpointy"

| # | Sev | Nález | Cíl |
|---|---|---|---|
| C-1 | 🔴 | Dvě konvence + dva kořeny složek — `Domain/Project/Endpoints/GetProject.cs` (ns `Core.Domain.Project.Endpoints`, třída `GetProject`, metoda `Handler`, klasická extension) vs `Endpoints/UserInfo/.../GetUserInfoEndpoint.cs` (ns `Core.Endpoints.UserInfo`, `…Endpoint`, `HandleAsync`, `extension(...)`). Složka `Domain/` v use-case projektu předstírá doménu. | Zrušit `Domain/` root, vše jako feature-slice pod jedním schématem (doporučeno UserInfo styl). |
| C-2 | 🔴 | Není konzistentní use-case jednotka — Project: endpoint → repo přímo, vrací **agregát** (`GetProject.cs:16`, leak write modelu + serializace `ImmutableList` projekcí). UserInfo: endpoint → statický `UserInfoLoader` (7-param pass-through) → statická projekce. | Jeden tvar: tenký endpoint deleguje na injektovaný use-case; mapování v mapperu; Response DTO pro `GetProject`. |
| C-3 | ✅ | ~~Mrtvý kód — `UserInfoCacheHelpers`.~~ Neaktuální: `BuildPermissionsVersion` je po centralizaci output cache (`1fa0c00f`) opět používán z `UserInfoCache.DescribeAsync` jako `permissions` vary-by. Ponecháno. | Žádná. |
| C-4 | 🟠 | Viditelnost — `UserInfoProjection`, `UserInfoExtendedProjection` jsou `public`, zbytek `internal`. (`UserInfoCacheHelpers` převeden na `internal`.) | Public jen composition (`AddCore`/`MapCoreEndpoints`), zbytek `internal`. |
| C-5 | 🟠 | Composition — `AddCoreIntegration` (nejasný název), `MapV2Endpoints` (verzování v názvu metody), 9× ruční `AddScoped<IDomainEventHandler<…>>`. | `AddCore` + `MapCoreEndpoints` ve složce `Composition/`; event-handlery scanem nebo na DataAccess straně. |
| C-6 | 🟡 | Cache policies roztroušené — `Domain/Project/ProjectOutputCachePolicy.cs` vs `Endpoints/UserInfo/UserInfoOutputCachePolicy.cs`. | Každou k jejímu feature-slice, invalidátor nech v `Shared`. |

---

## 4. Plán kroků (prioritizovaný)

Každý krok má ověřovací kritérium. Doporučené pořadí; P0 nejdřív.

### P0-A — Reorientace závislostí `Core → Core.DataAccess → Core.Domain`

Realizuje **D1**. Detailní change-set:

**1. Přesun souborů (read kontrakty → DataAccess):**

| Z | Do | Nový namespace |
|---|---|---|
| `Core.Domain/EndpointQueries/IUserInfoQuery.cs` | `Core.DataAccess/Queries/Contracts/IUserInfoQuery.cs` | `Core.DataAccess.Queries.Contracts` |
| `Core.Domain/User/UserInfoContracts.cs` (`UserDetails`, `UserExtendedDetails`, `UserProjectResourceData`, `AssignedLocationData`) | `Core.DataAccess/Queries/Contracts/UserInfoContracts.cs` | `Core.DataAccess.Queries.Contracts` |

Read DTO dál referencují doménové VO (`Email`, `ProjectShortName`, …) — OK, protože `Core.DataAccess → Core.Domain`.

**2. csproj reference:**
- `Core.csproj`: **odebrat** `<ProjectReference ..\Core.Domain\Core.Domain.csproj />`, **přidat** `<ProjectReference ..\Core.DataAccess\Core.DataAccess.csproj />`.
- `Core.DataAccess.csproj`: beze změny.

**3. `using` v dotčených souborech (8):**
- `Core/Endpoints/UserInfo/*` (4 soubory): `using Core.Domain.EndpointQueries;` → `using Core.DataAccess.Queries.Contracts;`.
- `Core.DataAccess/Queries/UserDetailsQuery.cs` + `ServiceCollectionExtensions.cs`: lokální namespace.

**4. `Core/GlobalUsings.cs`:** ponechat doménové usings (fungují tranzitivně), přidat `global using Core.DataAccess.Queries.Contracts;`.

**Verify:** `dotnet build` zelený; `Core.csproj` nemá přímou referenci na `Core.Domain`; `dotnet test --no-build` projde.

### P0-B — Collapse duplicitní DataAccess (DA-1, DA-2)

- Ověřit, že `Persistence/Entities/*Record`, `Persistence/Configurations/*RecordConfiguration` a `MigrationDbContext` nikdo nepoužívá.
- Smazat je; ponechat jedinou sadu konfigurace nad DDD entitami.
- Opravit `Core.DataAccess.csproj` — odstranit `<Compile Remove="Queries/**"/>` žonglování; buď dokončit, nebo smazat vyřazené `*QueryExtensions`.

**Verify:** `dotnet build` zelený bez vyloučených souborů; jediný `DbContext`; `dotnet ef migrations list` funguje.

### P0-C — Zrušit `Core/Domain/` a sjednotit endpoint konvenci (C-1, C-2)

- Přesunout `GetProject` mimo `Domain/` do feature-slice (`Features/Project/GetProject/`), přejmenovat na konvenci (`GetProjectEndpoint`, `HandleAsync`, `Map...Endpoint`).
- Přidat `GetProjectResponse` DTO; přestat vracet agregát.
- Vybrat jednu extension syntax (doporučeno `extension(...)`).

**Verify:** `dotnet build` zelený; žádná složka `Core/Domain/`; `GET /v2/Projects/{shortName}` vrací DTO, ne agregát.

### P1 — Čistota write modelu (D-1, D-2)

- Vyndat `async`/DB porty z agregátu `Project`; mutace synchronní nad načtenými kolekcemi.
- „Ensure/load" logiku přesunout do use-case.

**Verify:** agregát `Project` nemá `async` metody ani parametry typu `I*Handler`; unit testy bez mocků.

### P2 — Use-case jednotka + úklid Core (C-2, C-3, C-4, C-5)

- Injektovaný use-case místo statického `UserInfoLoader`.
- ~~Smazat `UserInfoCacheHelpers`.~~ Neaktuální — viz C-3, je používán pro vary-by cache klíč; převeden na `internal`.
- `internal` všude kromě composition.
- `AddCore`/`MapCoreEndpoints` v `Composition/`; event-handlery scanem nebo na DataAccess straně.

**Verify:** jediný public typ v `Core` mimo composition = Response DTO (kvůli OpenAPI); build zelený.

### P3 — Konvence a onboarding (C-6, DA-4, DA-5, D-4)

- Cache policies k feature-slice; invalidátor v `Shared`.
- Centralizovat EF include-set; env-guard sensitive logging.
- `WellKnownRoleOptions` mimo doménu.
- Jeden „golden" referenční řez + krátký „jak přidat endpoint" checklist.

**Verify:** nový endpoint lze vytvořit zkopírováním golden řezu beze změny struktury.

---

## 5. Cílový layout složek

```
Core.Domain/                         ← jen write model
  Project/  Provider/  Request/  User/   (entity, VO, eventy, IRepository port)
  Primitives/   Shared/
  ── pryč: EndpointQueries/, read DTO, WellKnownRoleOptions, async I*Handler porty

Core.DataAccess/                     ← persistence + read
  Persistence/   (DatabaseContext, Configurations nad DDD entitami — jedna sada)
  Repositories/  (write adaptéry pro IRepository)
  Queries/
    Contracts/   (IUserInfoQuery + read DTO)        ← přesunuto z Core.Domain
    (IDataQuery + typované query objekty)           ← vše se kompiluje
  Composition/   (AddCoreDataAccess)
  ── pryč: Persistence/Entities/*Record, *RecordConfiguration, MigrationDbContext

Core/                                ← use-cases (vertikální řezy)
  Features/
    Project/
      GetProject/   (GetProjectEndpoint.cs, GetProjectResponse.cs)
      ProjectOutputCachePolicy.cs
    UserInfo/
      GetUserInfo/   GetExtendedUserInfo/
      UserInfoUseCase.cs (ex-Loader), projekce/mappery
      UserInfoOutputCachePolicy.cs
  Shared/OutputCache/   (ICoreOutputCacheInvalidator)
  Composition/   (AddCore, MapCoreEndpoints)
```

---

## 6. Otevřené otázky / budoucí upřesnění

- **Read DTO vs. doménové VO:** read DTO dnes vystavují `Email`, `ProjectShortName` apod. Pro čistší read model je časem zploštit na primitiva (oddělí read od write modelu úplně). Není nutné pro reorientaci závislostí.
- **Registrace domain event-handlerů:** dnes v `Core/ServiceCollectionExtensions`. Konceptuálně patří k write/DataAccess composition. Po P0-A funguje tranzitivně, přesun je volitelný (P2).
- **Write přes use-case:** zvážit, zda i write use-cases nemají jít přes explicitní command porty (dnes sahají na `IRepository` přímo). Mimo scope tohoto plánu.

---

## 7. Konvence (cílové)

- **Endpoint soubor:** `<Verb><Resource>Endpoint.cs`, jeden feature-slice = složka.
- **Třída:** `internal static <Verb><Resource>Endpoint`; handler `HandleAsync`; mapování `Map<Verb><Resource>Endpoint` přes `extension(IEndpointRouteBuilder)`.
- **Request/Response:** `sealed record` u endpointu; nikdy nevracet doménový agregát.
- **Viditelnost v `Core`:** vše `internal` kromě `AddCore`/`MapCoreEndpoints` a Response DTO.
- **Read:** query port v `Core.DataAccess/Queries/Contracts`, impl. v `Core.DataAccess/Queries`.
- **Doména:** synchronní, bez EF/HTTP, mutace nad načtenými kolekcemi.

---

## 8. Pokračování osy A — rozhodnutý change-set (2026-06-08)

> **Stav po P0:** P0-A / P0-B / P0-C jsou hotové (ověřeno v kódu — `Core.csproj` referencuje `Core.DataAccess`, ne `Core.Domain`; duplicitní `Persistence/Entities/*Record` + `MigrationDbContext` neexistují; `Core/Domain/` zrušeno, vše je `Features/<Feature>/<UseCase>/`). Tato sekce fixuje **rozhodnutý** tvar P1–P3 z grilling session.
>
> **Pořadí:** P1 **před** přidáním dalších write endpointů (Provider / Location / ProjectTag mutace), jinak async-handler dluh roste s každým novým zápisem.

### Rozhodnutí (doplňuje decision log §2)

**D5 — Project role je načtená kolekce na Project agregátu, ne async handler.**
Vlastnictví zůstává na Projectu (ADR-0008 beze změny), ale **mechanismus** se mění: `Project` dostane `_memberRoles : ICollection<UserPermission>` hydratovanou repozitářem; agregát mutuje načtenou kolekci **synchronně**. `IProjectRoleHandler` se ruší. Viz dodatek v [ADR-0008](../architecture/adr/0008-core-owns-project-role-writes.md).

**D6 — `UserPermission` se reuse-uje napříč dvěma agregáty (DDD-pragmaticky).**
Tentýž EF typ navigují User (`_systemPermissions`, filtr `ProjectId == null`) i Project (`_memberRoles`, FK `ProjectId`). Distinktní typ `ProjectMemberRole` zamítnut — dva CLR typy → jedna tabulka vyžadují TPH diskriminátor = změna schématu (kolize s migrations-overlay rule). Globální filtr `ProjectId == null` **zůstává pro User**; Project ho při hydrataci obchází `IgnoreQueryFilters()`.

**D7 — Cílového uživatele předáváme jako `User` agregát, ne `UserId`.**
`User` dál nevystavuje svůj surrogate `UserId` (shadow PK, DB detail řešený `EF.Property`). Mutace `_memberRoles` zrcadlí `AssignedUserToProject`: prvek dostane `internal User User` navigaci, FK dopočítá EF. Reference-by-identity (`UserId`) zavádět jen kdyby to EF nezvládl.

**D8 — Event-handlery scanem na DataAccess straně.**
9× ruční `AddScoped<IDomainEventHandler<…>>` nahradit assembly-scanem v `AddCoreDataAccess` (tam je dispatchuje `IUnitOfWork`). Cache-eviction handlery (sahají na output cache) zůstanou v `AddCore`.

### P1 — Čistota write modelu (jádro)

| Krok | Akce | Verify |
|---|---|---|
| **P1.1** Modelovat `_memberRoles` | `Project` dostane `ICollection<UserPermission> _memberRoles` (read-only projekce aktivních); `UserPermission` dostane `internal User User` navigaci; EF relationship `Project (1)→(N) UserPermission` přes FK `ProjectId`; `DatabaseContext.ProjectRepository` hydratuje přes `.IgnoreQueryFilters().Include(…)`. Globální filtr `ProjectId == null` zůstává pro User. | Repo vrátí Project s naplněnou `_memberRoles`; integrační test vidí role členů bez handleru. |
| **P1.2** Sync role-metody | `AssignUserWithRoleAsync`/`UnassignUserAsync`/`ChangeUserRoleAsync` → synchronní (`HasActiveOwner`/`GetActiveRole` jako in-memory dotazy nad `_memberRoles`; hierarchy invariant je už čistý přes `ProjectRoleAssignment`). Smazat `IProjectRoleHandler` + `DatabaseContext.ProjectRoleHandler.cs`. Upravit 5 volání (CreateProject, AssignProjectUser, UnassignProjectUser, ChangeProjectUserRole, SelfOnboardingAccept). | `Project.cs` nemá `async` ani `I*Handler` parametr; unit testy role-invariantů **bez mocků**. |
| **P1.3** Cascade delete + eviction | `SoftDeleteAllRolesAsync` nahradit: agregát soft-deletuje `_memberRoles`; `IdpId` pro eviction z hydratované `User` navigace, nebo nést dotčené `UserId` v `ProjectDeletedEvent`. | Delete cascade-soft-deletuje role a evictuje cache dotčených userů (chování drží). |
| **P1.4** Tag bez handleru | ✅ Hotovo (ADR-0078): use-case resolve-or-create Tagu přes generický repository port `IRepository<ProjectTag, TagName>` (`FindAsync ?? AddAsync(ProjectTag.Create(…))`, adapterův `AddAsync` = *ensure* na natural-key — revive soft-deleted stejnojmenného řádku místo duplicitního insertu) + existující sync `AssignExistingTag(ProjectTag)`. `IProjectTagHandler` + impl + DI smazány; `CreateProjectTagEndpoint`/`AssignProjectTagEndpoint` přepnuty na repository port. | `IProjectTagHandler` zmizel (grep 0×), `Core.Domain` nemá žádný `*Handler` port; create-and-assign vrací `201 Created`. |
| **P1.5** Smazat mrtvý resource kód | `AddResourceAsync`/`RemoveResourceAsync` + `IProjectResourceHandler` mají **0 volání** (resource-write odložen, ADR-0009). Smazat. | Build zelený; grep bez referencí. |

### P2 — Úklid composition

| Krok | Akce | Verify |
|---|---|---|
| **P2.1** Composition/ + rename | `AddCoreIntegration`→`AddCore`, `MapV2Endpoints`→`MapCoreEndpoints` (route group „v2" **zůstává** — Gateway routing). Přesun do `Core/Composition/`. | `Program.cs` volá `AddCore`/`MapCoreEndpoints`; build zelený. |
| **P2.2** Event-handlery scanem | Scan `IDomainEventHandler<>` v `AddCoreDataAccess` (D8); cache-eviction handlery zůstávají v `AddCore`. | Nový handler se zaregistruje bez ručního zápisu; eventy se dispatchují jako dnes. |
| **P2.3** Viditelnost | ✅ Hotovo (ADR-0079): 12 uniklých typů zúženo na `internal` (`PageRequest`/`PaginationOptions`/`PagedResponse`, `Caller`, Workflow `ServiceCollectionExtensions`, 7 UserInfo Response recordů). Response DTO jsou `internal` (konvence — OpenAPI funguje). Zamrzlá public surface `Core` vynucena allowlist reflection testem `PublicSurfaceTests` (`Core.ArchitectureTests`). | Public typy `Core` = přesně allowlist: `AddCore`/`MapCoreEndpoints`/`ApiAudience` + 2 dokumentované výjimky `IResourceSyncRunner` a `ConditionalGetApplicationBuilderExtensions`; `PublicSurfaceTests` zelený. |

### P3 — Konvence a onboarding

| Krok | Akce | Verify | Stav |
|---|---|---|---|
| **P3.1** WellKnownRoleOptions | ✅ Decoupling byl hotový už dřív (typ je čisté POCO bez `Microsoft.Extensions.Options`; binding `services.Configure<>` v composition). Dokončeno přejmenováním `WellKnownRoleOptions` → `WellKnownRoles` a přesunem do `Core.Domain/User/` (commit `4f120489`). | Doména nereferencuje `Microsoft.Extensions.Options`; binding v composition. | ✅ hotovo |
| **P3.2a** Cache policies k feature-slice | ✅ Hotové už P0-C reorgem — `ProjectCache`/`CountryCache`/`UserInfoCache`/`StatisticsCache` jsou v `Features/<F>/`, invalidátor v `Shared/`. | Žádné cache policies mimo feature-slice. | ✅ hotovo |
| **P3.2b** Centralizovat EF include-set | ✅ Hotové už P1.1 — každý repo má jediné `_xincludes` (`DatabaseContext.ProjectRepository.cs` aj.). | Žádné magic-string includy roztroušené. | ✅ hotovo |
| **P3.2c** Env-guard sensitive logging | `EnableDetailedErrors`/`EnableSensitiveDataLogging` jen v `IsDevelopment()` (commit `f7c1983d`). | Sensitive logging jen v dev. | ✅ hotovo |
| **P3.3** Golden řez | Jeden referenční vertikální řez (read `GetProject` + write `AssignProjectUser`) + „jak přidat endpoint" checklist → [golden-slice.md](./golden-slice.md). | Nový endpoint lze vytvořit zkopírováním golden řezu. | ✅ hotovo |
