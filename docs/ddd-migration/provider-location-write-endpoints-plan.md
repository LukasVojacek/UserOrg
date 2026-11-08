# Plán — Provider & Location write endpointy (modulární Core)

> **Stav:** ✅ Implementováno (S1–S8 hotové; v2 provider/location write endpointy shipnuté — viz [endpoint migration map](./legacy-to-refactored-endpoint-migration.md)). Rozhodnuto v grilling session 2026-06-09.
> **Scope:** Pouze `src/Modules/` (Core / Core.DataAccess / Core.Domain). **Legacy `src/Core` se nedotýkáme.**
> **Navazuje na:** [core-modules-target-architecture-and-plan.md](./core-modules-target-architecture-and-plan.md) §8 ("P1 před přidáním write endpointů Provider / Location") a [golden-slice.md](./golden-slice.md) (write vzor).

---

## 1. Východiska (ověřeno v kódu)

- **Doména je hotová.** `Provider` agregát (`Core.Domain/Provider/Provider.cs`) má všechny mutace synchronně + `Result<Error>`:
  `Create`, `UpdateDetails`, `SetPrivacy`, `Deactivate`, `Reactivate`, `AddLocation`, `UpdateLocation`, `RemoveLocation`.
- **Location je child entity Provider agregátu** (`Core.Domain/Provider/Location.cs`), ne vlastní aggregate root → Location mutace tečou přes načtený `Provider`.
- **Write port existuje:** `IRepository<Provider, ProviderName>` (impl. `DatabaseContext.ProviderRepository.cs`, registrováno).
- **Routing podle jména:** `GetProvider` jede `Providers/{providerName}`; přirozený klíč je `ProviderName`. Nové endpointy drží **name-based** routy (ne legacy `{Id:GUID}`).
- **Reads nejsou cachované** → write endpointy nepotřebují cache eviction.
- **`Caller.Role`** je jediná efektivní role z `ClaimTypes.Role`; role-jména přes `IOptions<WellKnownRoles>` (`DefaultAdminRoleName="Admin"`, `DefaultProviderAdminRoleName="ProviderAdmin"`).

→ Práce zasahuje **Core (endpoint + composition + doména + queries)** **i Gateway** (providerName claim + permission gate + RBAC mapování). Není to Core-only.

### Zjištění z grilling session (přehlédnuté detaily — ověřeno v kódu)

1. **Gateway nemá Guid→jméno resolver ani referenci na `Core.DataAccess`.** Provider klíč nese ve Vaultu `Data` = Provider **GUID** (`ApiKeyAuthenticationHandler` ho posílá jako `sub` i `provider_id`; legacy `TryGetProviderId` čte `Guid?`). Gateway referencuje jen `ServiceDefaults`, `HashiCorpVault`, `Forge.Core`. Resolver tedy **přidáme do `Forge.Core`** raw-SQL nad `UserOrgDbContext` (stejný vzor jako `GetUserPermissionsAsync`) — žádný nový ProjectReference. ⇒ ADR-0011 „no DB on auth path" platí pro **Core**, ne Gateway (ten DB pro role už dnes volá přes `UserPermissionsCache`).
2. **Gateway permission gate (`AuthorizeRequestAsync`) běží PŘED Core** a má dvě větve. Bez EndpointMap záznamu vrátí `403 "no permission mapping"`. API-key (Provider) větev vyžaduje `perm:service:{jménoKlíče}` — na generické rout nelze. ⇒ nové v2 routy přidat do `roleManagmentRBAC.json` (→ `perm:provider:*`) a **rozšířit gate** o větev: `serviceType==Provider` + endpoint vyžaduje `perm:provider:*` → pustit (Core doškáluje claimem).
3. **`AddLocation` nevrací Id** (EF shadow) a `GetLocationV2` je Guid-based, ne name-based. ⇒ `AddLocation` → `201 CreatedAtRoute("GetLocationV2", { id })`; id získat read-backem `ILocationQuery.GetIdByProviderAndNameAsync(providerName, locationName)` po `SaveChanges` (ne čtením EF entry v handleru).
4. **Natural-key casing:** `ProviderName`/`LocationName` jsou CS record-structy (jen `Trim`). DB porovnání jsou CI (collation), ale Core auth/in-memory match CS → legit vlastník dostane 403 / location 404 / duplicita „GPU"+„gpu". ⇒ **case-insensitive identita** přes `Equals`/`GetHashCode` (`OrdinalIgnoreCase`), `From`/`Create` jen trimují a **zachovají casing** — viz [ADR-0012](../architecture/adr/0012-provider-location-name-canonicalization.md). **Žádná read change**: GET vrací jméno tak, jak je uložené (žádný nový sloupec, migrace zmražené).

---

## 2. Autorizační model (rozhodnuto — viz [ADR-0011](../architecture/adr/0011-provider-write-authorization.md))

**Claims-only — žádná DB na auth cestě.** Dva write aktéři:

- **Admin** — OIDC uživatel s `caller.Role == "Admin"`. Libovolný provider; **jediný** smí Create.
- **Provider** — provider service-key identita (API-key flow, identity type `provider`), nese **`providerName` claim**. Jen **svůj** provider; nesmí Create.
- jiný principal → `403`.

| Operace | Admin | Provider (API-key) |
|---|---|---|
| CreateProvider | ✅ libovolný | ❌ |
| UpdateProvider (Description/SupportEmail/IsPrivate/Country) | ✅ libovolný | ✅ jen vlastní |
| Deactivate | ✅ libovolný | ✅ jen vlastní |
| Reactivate | ✅ libovolný | ✅ jen vlastní |
| AddLocation | ✅ libovolný | ✅ jen pod vlastním |
| UpdateLocation | ✅ libovolný | ✅ jen pod vlastním |
| RemoveLocation | ✅ libovolný | ✅ jen pod vlastním |

- **Ownership Provider-aktora:** route `{providerName}` == `providerName` claim. `ProviderName` je přirozená identita end-to-end (route = repo klíč = claim) → string equality, žádný name↔Guid most, žádná DB.
- Všechny mutující routy: `.DisabledWhen("ReadOnlyMode")`.
- **Jméno se nemění** (přirozený klíč). UpdateProvider mění Description/SupportEmail/IsPrivate/Country; privacy je součástí Update (žádný samostatný privacy endpoint).

### Mechanismus scopingu (claims-only)

- **Gateway** doplní **aditivní `providerName` claim** při ražbě interního tokenu pro provider identitu: resolve provider Guid (vault `Data`) → `ProviderName`, cacheováno (jako stávající provider-id resolver). Stávající `providerId` Guid claim **zůstává** (drží Request write path + legacy `TryGetProviderId`) — name claim je čistě aditivní.
- **Core**: `Caller` vystaví `ProviderName?` (čte nový claim). Sdílený helper volaný v handleru:

```csharp
// ProviderWriteAuthorization (Shared/Auth) — synchronní, žádná DB:
//   caller.Role == "Admin"              → true (libovolný)
//   caller.ProviderName == targetName   → true (Provider jen svůj)
//   jinak                               → false
if (!ProviderWriteAuthorization.Authorize(caller, request.ProviderName, roleOptions))
    return Results.Forbid();
```

---

## 3. Endpointy (7)

Routa = `v2` group (Gateway). Write přes `IRepository<Provider, ProviderName>` → sync mutace → `IUnitOfWork.SaveChangesAsync`.

| # | Endpoint | Metoda + routa | Doména | Authz | Návrat |
|---|---|---|---|---|---|
| 1 | CreateProvider | `POST Providers` | `Provider.Create` + `repository.AddAsync` | Admin | `201` |
| 2 | UpdateProvider | `PUT Providers/{providerName}` | `UpdateDetails` + `SetPrivacy` + `ChangeCountry` (nová) | Admin \| PA-own | `204` |
| 3 | DeactivateProvider | `DELETE Providers/{providerName}` | `Deactivate` | Admin \| PA-own | `204` |
| 4 | ReactivateProvider | `POST Providers/{providerName}/Reactivate` | `Reactivate` | Admin \| PA-own | `204` |
| 5 | AddLocation | `POST Providers/{providerName}/Locations` | `AddLocation` | Admin \| PA-own | `201` |
| 6 | UpdateLocation | `PUT Providers/{providerName}/Locations/{locationName}` | `UpdateLocation` | Admin \| PA-own | `204` |
| 7 | RemoveLocation | `DELETE Providers/{providerName}/Locations/{locationName}` | `RemoveLocation` | Admin \| PA-own | `204` |

**Umístění složek:** všech 7 pod `Features/Provider/<Verb><Resource>/` — i Location mutace, protože **načítají a mutují Provider agregát** (hranice agregátu určuje feature-slice). Location *reads* zůstávají v `Features/Location/` (mají vlastní query). Routy Location jsou vnořené pod `Providers/{providerName}/Locations`.

**Konvence (golden-slice):** `internal static <Verb><Resource>Endpoint`, `HandleAsync`, `Request`/`Response` records, `extension(IEndpointRouteBuilder)` mapping, `MapDocumentation()`. Validace VO v handleru (jako `CreateProjectEndpoint`). Mapping registrovat v `Composition/EndpointsExtensions.cs`.

---

## 4. Kroky a verify

| Krok | Akce | Verify |
|---|---|---|
| **S0** ADR | ✅ Autorizační model = [ADR-0011](../architecture/adr/0011-provider-write-authorization.md); ✅ name canonicalizace = [ADR-0012](../architecture/adr/0012-provider-location-name-canonicalization.md). | Hotovo. |
| **S1a** Gateway resolver | Do `Forge.Core` přidat `IProviderIdentityResolver.GetProviderNameAsync(Guid)→string?` nad raw-SQL na `UserOrgDbContext` (`select Name from Provider where Id={id}`, **bez** `IsDeleted` filtru — i deaktivovaný provider potřebuje jméno k reaktivaci); cache `HybridCache` 30s. | Guid→jméno funguje i pro deaktivované; žádný nový ProjectReference (vše ve Forge.Core). |
| **S1b** Gateway claim + gate | API-key větev `InternalTokenTransformExtensions` doplní **aditivní `providerName` claim** (přes resolver). Rozšířit `AuthorizeRequestAsync`: `serviceType==Provider` + endpoint vyžaduje `perm:provider:*` → pustit. Přidat v2 routy do `roleManagmentRBAC.json` (→ `perm:provider:*`). `provider_id` Guid claim ponechat. | Provider token nese `providerName`; provider projde gate na v2 routách; Admin přes role perms; bez mapování → 403. |
| **S2** Doména | `Provider.ChangeCountry(int countryId)` (**jen `CountryId = countryId`** — bez eventu/validace, jako UpdateDetails/SetPrivacy); **error kódy** do `ProviderErrors` (+ `ProviderErrorCodes`) **vč. nové `NameAlreadyExists(name)`** (`provider.name_already_exists`); **canonicalizace** `ProviderName.From`/`Create` a `LocationName.From`/`Create` (lower+trim, viz ADR-0012). | `ChangeCountry` sync + test; každý `ProviderErrors` má `.WithCode()`; VO testy: různý casing → stejná hodnota. |
| **S3** Read queries + write load | `IProviderQuery.ProviderExistsAsync(ProviderName)` (přes `IgnoreQueryFilters`, vč. deaktivovaných); `ICountryQuery.FindIdByCodeAsync(string)→int?` (match `where Name == code`, CI); `ILocationQuery.GetIdByProviderAndNameAsync(ProviderName, LocationName)→Guid?` (read-back pro 201); **write repo `FindIncludingDeactivatedAsync(ProviderName)` (`IgnoreQueryFilters`)** — nutné, protože `FindAsync` filtruje `!IsDeleted` a Reactivate by jinak deaktivovaného nenačetl. | Existence vidí i deaktivované; neplatný kód → `null`; nová location vrátí svůj Guid; Reactivate načte deaktivovaného. |
| **S4** Caller + helper + mapping | `Caller` vystaví `ProviderName?` (čte claim); `ProviderWriteAuthorization.Authorize(...)` (sync, žádná DB; canonical VO → rovnost už CI); `ProviderErrorMapping.ToProblem()` keyed na `Code`: `already_deactivated`/`not_deactivated`/`name_already_exists`/`location_already_exists`/`location_already_deleted` → **409**, `location_not_found` → **404**, default → **400** (`Lukdrasil.Result.State` nemá Conflict → 409 musí z mapování). | Helper: Admin=true, Provider-own=true (i jiný casing), Provider-cizí=false, jinak=false. |
| **S5** CreateProvider | Endpoint #1 (Admin-only). Request: `Name, CountryCode, Description?, SupportEmail?, IsPrivate`. Country přes `FindIdByCodeAsync` (neplatný → 400); duplicita jména (vč. deaktivovaných) → 409. | Admin → `201 CreatedAtRoute(GetProviderByNameV2)`; ne-Admin → `403`; duplicitní jméno → `409`; neplatný kód → `400`; `ReadOnlyMode` → zakázáno. |
| **S6** Provider lifecycle | Endpointy #2–#4. UpdateProvider mění Description/SupportEmail/IsPrivate/Country. **Reactivate (a idempotentní Deactivate) načítá přes `FindIncludingDeactivatedAsync`** (běžný `FindAsync` deaktivovaného nevidí). | Admin libovolný → `204`; Provider vlastní → `204`; Provider cizí → `403`; neznámý provider → `404`; Deactivate už deaktivovaného → `409`; **Reactivate deaktivovaného → `204`**; Reactivate aktivního → `409`. |
| **S7** Location mutace | Endpointy #5–#7. AddLocation → `201 CreatedAtRoute("GetLocationV2", { id })` (id přes `GetIdByProviderAndNameAsync`). | Add → `201` + Location hlavička; Update/Remove → `204`; Provider cizí → `403`; neexistující location → `404`; duplicitní location (i jiný casing) → `409`. |
| **S8** Build + testy | `dotnet build` (Forge.Core + Gateway + Core) + `Core.UnitTests`. | 0 errors; testy na baseline (8 preexisting DI-gap failů), 0 regresí. |

**Commit granularita:** S0 (ADRy) → 1; S1a+S1b (Gateway resolver + claim + gate + RBAC) → 1; S2+S3+S4 (scaffolding: doména vč. canonicalizace, queries, Caller/helper/mapping) → 1; S5 → 1; S6 → 1; S7 → 1; testy průběžně v rámci každého. Conventional commits, bez `Co-Authored-By`.

---

## 4.5 Request DTO tvary (rozhodnuto)

| Endpoint | Request pole |
|---|---|
| CreateProvider | `Name` (req), `CountryCode` (req, string — viz pozn.), `Description?`, `SupportEmail?`, `IsPrivate` |
| UpdateProvider | `CountryCode`, `Description?`, `SupportEmail?`, `IsPrivate` (jméno = route, nemění se) |
| AddLocation | `Name` (req), `Endpoint` (req), `IsPrivate`, `IsShared`, `Description?`, `AllocationUnitCount?`, `AllocationDescription?`, `AggregationName?` |
| UpdateLocation | `Endpoint` (req), `Description?` (jméno = route) |

- **Country = `countryCode` (prostý string), legacy-faithful resoluce.** „Code" je fyzicky sloupec `Country.Name` (projekce mapuje `Name→Code`). `FindIdByCodeAsync` dělá `where Name == code` (CI, přesně jako legacy `CountryRepository.GetByCountryCodeAsync`) → `countryId`; nenalezeno → `400`. **Bez** alpha-2 `CountryCode` VO validace: ten VO (ISO alpha-2, 2 znaky) je orphan — `Country` entita ho nepoužívá (drží `CountryName`, max 100, bez formátu) — a produkční seed `Country.Name` není v repu (externí reference data), takže formát nelze autoritativně potvrdit. (Dřívější „3 znaky" v plánu bylo chybné.)
- **`Mode` / `ServiceName` / `ExternalId`** se v requestu nezadávají — defaultované doménou (service-integration detail), jako v legacy.
- **CreateProvider odpověď:** `201 CreatedAtRoute("GetProviderByNameV2", new { providerName })`.

---

## 5. Mimo scope

- **AssignedLocation / alokace zdrojů** (resource-write) — odloženo (ADR-0009). `Provider.AddLocation` zakládá iniciální `AssignedLocation` automaticky; samostatná správa alokací není součástí.
- **`providerId` Guid claim** — ponechán beze změny (drží Request write path + legacy `TryGetProviderId`); `providerName` je aditivní (S1).
- **Legacy `src/Core`** — beze změny.
