# Issue #277 — "Api version comparison - differences": analýza

**Datum:** 2026-08-11
**Vstup:** [work item 277](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/work_items/277) (11 řádků porovnání v1 vs. v2)
**Metoda:** čtení zdrojů (kód je zdroj pravdy), křížová kontrola proti `docs/ddd-migration/legacy-to-refactored-endpoint-migration.md`, `v1-retirement-parity.md` a proti parity suite `Tests/E2E/IntegrationTests/Parity/`.
**Poznámka k metodice reportu:** v1 byl měřen proti nasazenému `api.dev.lexis.tech`, v2 proti lokálnímu `gateway-aspire.dev.localhost:5800`. Část rozdílů je proto environmentální, ne kódová.

---

## Souhrn

| # | Řádek issue | Verdikt |
|---|---|---|
| 1 | `Enums/RequestTypes` — chybí `Id:3 HEAppECreation` | **Záměr, ale dokumentace lže + latentní bug na read path** |
| 2 | `Heappe/ListChangedFilesForJob` "missing endpoint" | **Není problém** — re-homed, existuje |
| 3 | `Heappe/Partitions` "missing endpoint" | **Není problém** — re-homed, existuje |
| 4 | `Heappe/CommandTemplates` "missing endpoint" | **Není problém** — re-homed, existuje |
| 5 | `Other/CyclopsUsage` — v1 502, v2 500 | **Prostředí, ne kód** (drobný contract nález) |
| 6 | `Requests/SentRequests` — nevyplněné hodnoty | **Záměr (breaking change), 2 pole nezdokumentovaná** |
| 7 | `User/Preferences` — vrací string místo JSON | **Není v2 regrese** — v1 dělá totéž; oprávněná výtka k designu obou |
| 8 | `Project/TemplatesInfo` — v2 nevrací node | **Prostředí** (HEAppE nedostupná), kontrakt je shodný |
| 9 | `api/version` — má vracet JSON | **Není v2 regrese** — v1 vrací text/plain také; feature request |
| 10 | `Heappe/CommandTemplatePermissions` "missing" | **Není problém** — re-homed, existuje |
| 11 | `Heappe/CommandTemplate/{p}/{r}` "missing" | **Není problém** — re-homed, existuje |

**Skutečné akce k řešení:** #1 (dvě věci), #6 (doplnit dokumentaci), #5 (nasadit cyclops secret do lokálního Vaultu + formát chyby). Zbytek je buď záměr migrace, nebo nerovné srovnání prostředí.

---

## 1. `RequestTypes` — chybí `HEAppECreation` (Id 3)

**Fakta**

- v1 [`ERequestType`](src/Core/Domain/Enums/ERequestType.cs) = `Unknown=0, SelfOnboarding=1, ProjectResourceAssignment=2, HEAppECreation=3`; endpoint enumeruje enum reflexí → **4 položky**.
- v2 [`GetRequestTypesEndpoint.cs`](src/Modules/Core/Features/Enums/GetRequestTypes/GetRequestTypesEndpoint.cs) vrací natvrdo **3 položky**: `0 Default`, `1 SelfOnboarding`, `2 ProjectResourceAssignment`.
- Unit test [`EnumEndpointsTests.cs:77-80`](Tests/Modules/Core/Core.UnitTests/Enums/EnumEndpointsTests.cs:77) tuto trojici zamyká → **jde o záměr, ne o zapomenutí**.
- [`StaticParityTests.cs:22-26`](Tests/E2E/IntegrationTests/Parity/StaticParityTests.cs:22) explicitně píše, že členství těchto enumů se legitimně liší a porovnává se jen *tvar položky*, ne obsah. Reportér tedy našel přesně to, co parity suite vědomě nekontroluje.

**Kde je přesto problém — dvě věci**

1. **Dokumentace lže.** `legacy-to-refactored-endpoint-migration.md:253` tvrdí: *"No breaking change (`{Id, Name}`) — v2 includes `{Id:0,"Unknown"}` to match legacy."* Kód nic takového nedělá — `0` se jmenuje `Default` a `3` chybí. Řádek v ledgeru je v obou tvrzeních nepravdivý. To je jediný důvod, proč tenhle nález vůbec vznikl jako "rozdíl": mapa migrace slibuje parity, kterou kód nedodává.
2. **Latentní bug na read path.** [`RequestType`](src/Modules/Core.DataAccess/../Core.Domain/Request/RequestType.cs) přijímá persistovaný rozsah `0–10` (`IsValid`), ale jméno má jen pro 0/1/2 — vše ostatní vrátí `"Unknown"`. Pokud v DB existuje řádek `Type = 3`, v2 ho v `requests` i `Requests/Sent` vypíše jako `Type: "Unknown"` a `TryParseName("HEAppECreation")` na filtru selže. Zmírňující okolnost: `grep` po celém repu nenašel **žádnou** code path, která by `HEAppECreation` zapisovala — hodnota se vyskytuje pouze v OpenAPI popiscích v1 (`RequestsEndpoint.cs`, `AdministrationEndpoint.cs`). Prakticky jde tedy o mrtvou hodnotu enumu; riziko je jen u historických dat.

**Doporučení:** rozhodnout jednou a zapsat — buď `3 → "HEAppECreation"` doplnit do `RequestType._names` (levné, uzavře i read path), nebo hodnotu prohlásit za retired a opravit řádek 253 v ledgeru. Současný stav "kód říká A, dokumentace B" je horší než obě varianty.

---

## 2, 3, 4, 10, 11. HEAppE endpointy označené jako "missing endpoint"

**Verdikt: falešný nález. Všech pět endpointů existuje a je namapovaných.**

Během migrace nešly do `Core` v2, ale byly **re-homed do samostatného modulu** `src/Modules/Integrations/Heappe` (ADR-0053, #236). Mapují se v [`HeappeAdapterExtensions.cs:52`](src/Userorg.Adapter/Heappe/HeappeAdapterExtensions.cs:52) pod prefix **`api/Heappe`** — tedy **bez `v2`**, a to bezpodmínečně (volá se z `UserorgAdapterExtensions.cs:51`, mimo `FeatureManagement:V2Api` gate).

| Issue hledal | Skutečná routa | Soubor |
|---|---|---|
| ListChangedFilesForJob | `GET api/Heappe/ListChangedFilesForJob/{projectShortName}/{resourceName}/{submittedJobInfoId}` | `Features/LogRetrieval/DownloadChangedFilesEndpoint.cs:95` |
| Partitions | `GET api/Heappe/Partitions/{projectShortName}/{resourceName}` | `Features/GetLocationPartitions/GetPartitionsEndpoint.cs:87` |
| CommandTemplates | `POST api/Heappe/CommandTemplates` | `Features/CreateProjectCommandTemplate/CreateTemplateEndpoint.cs:115` |
| CommandTemplatePermissions | `GET api/Heappe/CommandTemplatePermissions/{HeappeIdentifier}` | `Features/CommandTemplatePermissions/CommandTemplatePermissionsEndpoint.cs:29` |
| CommandTemplate/{p}/{r} | `GET api/Heappe/CommandTemplate/{ProjectShortName}/{ProjectResourceName}` | `Features/CommandTemplateManagement/GetAll/GetCommandTemplatesEndpoint.cs:33` |

Je to zdokumentované: ledger řádek 336 + 358 ("Re-homed to another module (DDI / Heappe) | 6"), `v1-retirement-parity.md:199` výslovně říká, že routy pod `/api/Heappe` jsou mimo scope v1→v2 mapy.

**Kde je reálná mezera:** ne v kódu, ale v *očekávání*. Modul Heappe není verzovaný, takže při retirementu v1 tyhle cesty prostě zůstanou na `api/Heappe`. Pokud má konzument (frontend) předpoklad "všechno se přesune pod `/api/v2/`", je to nutné explicitně vyvrátit — ideálně řádkem v ledgeru "these routes are intentionally unversioned" a v Scalar dokumentaci v2.

---

## 5. `Other/CyclopsUsage` — v1 502 / v2 500

**Verdikt: environmentální, ne migrační.**

Cyclops **nikdy nebyl a nebude Core endpoint** — ledger 311 + `v1-retirement-parity.md:130,161` (F2/C1): request zachytává Gateway a proxuje na externí `cyclops-api`; legacy Core handler je mrtvý stub. URL v issue to ostatně potvrzuje — "v2" sloupec obsahuje `/api/Other/CyclopsUsage/...`, tedy stejnou cestu, žádnou v2 routu.

- **v1 502** = nedostupná / chybující upstream `cyclops-api` na devu.
- **v2 500 "Server secrets configuration error"** = [`CyclopsForwardingExtensions.cs:71-77`](src/Modules/Services/Gateway/UserOrg.Gateway/Startup/CyclopsForwardingExtensions.cs:71) — `vault.GetExternalCredentials("cyclops")` neuspěl. Lokální Vault nemá naseedovaný cyclops secret. Není to bug forwarderu, je to konfigurace prostředí.

**Drobný reálný nález mimo zadání:** ta chybová větev píše `text/plain` string a HTTP 500 tam, kde jde o chybu konfigurace serveru; zbytek API mluví RFC 9457 ProblemDetails. Za opravu stojí, ale s issue #277 to nesouvisí.

---

## 6. `Requests/SentRequests` — v2 nemá vyplněné některé hodnoty

**Verdikt: záměrný breaking change, částečně nezdokumentovaný.**

Srovnání kontraktů:

| v1 `RequestModel` | v2 `GetSentRequestsEndpoint.Response` |
|---|---|
| `RequestId` | ❌ **dropped** — ADR-0024 (zero surrogate GUIDs) |
| `IdpUserId` | ✅ `IdpUserId` |
| `UserName` | ❌ **dropped** |
| `Email` (účet žadatele, `User.Email`) | ⚠️ nahrazeno `ContactEmail` (`Request.ContactEmail`) — **jiná sémantika** |
| `ProjectShortName` | ✅ |
| `State` (int) | ✅ `State` (PascalCase název) |
| `Type` (int) | ✅ `Type` (PascalCase název) |
| `LastModification` | ❌ **dropped** |
| — | ➕ `ProviderName`, `ProjectResourceName` |

To, co reportér vidí jako "nevyplněné hodnoty", je téměř jistě `contactEmail: null`: ledger 196 to popisuje výslovně — *"`ContactEmail` is the request's own contact email (empty for legacy rows that predate the field), whereas legacy `Email` returned the requestor's account email"*. Starší řádky v DB pole nemají, takže v2 legitimně vrací `null` tam, kde v1 dosazoval e-mail účtu. Stejně tak `providerName`/`projectResourceName` jsou pro self-onboarding requesty `null` z podstaty.

**Reálné mezery:**

1. **`UserName` a `LastModification` nejsou v ledgeru zmíněné vůbec.** `RequestId` kryje ADR-0024, `Email→ContactEmail` je popsané, ale tyhle dvě prostě zmizely. Pokud je frontend zobrazuje, je to tichý breaking change.
2. **Ledger popisuje jiný response shape než kód.** Řádek 195 slibuje `{..., ProjectId, UserId, ProviderId?, ...}`; kód vrací `ProjectShortName`, `IdpUserId`, `ProviderName` (viz [`RequestDetailsQuery.cs:96-104`](src/Modules/Core.DataAccess/Queries/RequestDetailsQuery.cs:96)). Kód je lepší (přirozené klíče), dokumentace zaostala.
3. **Parity suite tuhle třídu nálezů z definice nechytá.** [`RegressedListEndpointsParityTests.cs:40-42`](Tests/E2E/IntegrationTests/Parity/RegressedListEndpointsParityTests.cs:40) říká doslova: *"Whole-body cross-stack shape parity is deferred to B06/B07."* `requests-sent` se tam testuje jen na status parity + správné řazení v2. Field-level rozdíl je přesně ta odložená díra — reportér ji našel ručně dřív, než ji zavřel B06/B07.

---

## 7. `User/Preferences` — v2 vrací string s obsahem JSONu

**Verdikt: není to v2 regrese. Obě verze se chovají identicky.**

- v1 [`UserEndpoint.cs:19-31`](src/Core/API/API/UserEndpoint.cs:19): `IUserService.GetUserPreferences` → `Task<string>` → `Results.Ok(result)`.
- v2 [`GetUserPreferencesEndpoint.cs`](src/Modules/Core/Features/User/GetUserPreferences/GetUserPreferencesEndpoint.cs): `IUserInfoQuery.GetUserPreferencesAsync` → `Task<string>` → `Results.Ok(preferences)`.

Obojí serializuje `string` → JSON **řetězec** (uvozovkovaný, escapovaný). Ledger 211 to potvrzuje jako záměr: *"No shape change — returns raw JSON string."* Data se v DB drží jako `UserPreference.Data` (`nvarchar`), ani jedna vrstva je neparsuje.

**Zbývající nesrovnalosti, které stojí za opravu:**

- v2 XML popisek endpointu tvrdí *"Returns the JSON preferences object"*, ale `.Produces<string>` a runtime vrací řetězec. Popis je zavádějící.
- Výtka reportéra je věcně správná jako **API-design požadavek** (vracet `JsonElement`/`application/json` objekt), jen se týká **v1 i v2 stejně** — není to blocker migrace. Pokud se má měnit, dělat to jako vědomý breaking change v2 kontraktu, ne jako "parity fix".
- Malá výhrada: v1 byl měřen na nasazeném devu, který může běžet starším buildem. Pokud tam skutečně vrací nezaescapovaný objekt, znamená to, že dev běží jiný kód než `main` — stojí za rychlé ověření.

---

## 8. `Project/TemplatesInfo` — v2 nevrací `node`

**Verdikt: reportér má pravdu ve své vlastní poznámce — je to prostředím.**

Kontrakty jsou **identické**:

- v1 `TemplateInfoResponse(bool IsAvailable, string ClusterName, string LocationId, long? ClusterId, IEnumerable<NodeInfo> Nodes)`
- v2 `GetProjectTemplatesEndpoint.Response(bool IsAvailable, string ClusterName, string LocationId, long? ClusterId, IReadOnlyList<NodeResponse> Nodes)`

v2 endpoint volá živou HEAppE přes `IHeappeConnectionService`. Když volání selže, položka se vrátí s `IsAvailable=false` a prázdnými `Nodes` a [output cache se pro tu odpověď potlačí](src/Modules/Core/Features/Project/GetProjectTemplates/GetProjectTemplatesEndpoint.cs) (`GetProjectTemplatesEndpoint.HandleAsync`, `ResponseCacheControl.SuppressStorage`) — tedy přesně pozorované chování při nedostupné HEAppE z lokálu. Prázdné `Nodes` mohou vzniknout i legitimně: žádné HEAppE linky na projektu, nebo všechny mimo aktivní datové okno (obojí se loguje jako warning).

**Akce:** přeměřit na devu, jak si issue sám poznamenává (*"nutné ověřit na devu"*). Do té doby nález nepotvrzený. Pokud i na devu chybí `nodes` při `isAvailable: true`, je to skutečný bug a stojí za samostatné issue.

---

## 9. `api/version` — má vracet JSON, ne text

**Verdikt: není to v2 regrese, je to feature request na obě verze.**

- v1 [`ServiceEndpoint.cs:83-87`](src/Core/API/API/ServiceEndpoint.cs:83): `Results.Content(version, "text/plain")`
- v2 [`GetVersionEndpoint.cs`](src/Modules/Core/Features/Meta/GetVersion/GetVersionEndpoint.cs): `Results.Content(version, "text/plain")`

Řádek za řádek stejné, včetně fallbacku `APP_VERSION ?? "0.0.0.1"`. Parity je dokonce **testovaná**: [`StaticParityTests.cs:118-140`](Tests/E2E/IntegrationTests/Parity/StaticParityTests.cs:118) porovnává obě odpovědi jako plain text a vyžaduje shodu. Ledger 263: *"Anonymous in both; returns version string. No breaking change."*

Přechod na `{"version": "..."}` je legitimní přání, ale je to **breaking change pro současné konzumenty v1 i v2** a musí projít stejným rozhodovacím procesem jako ostatní změny kontraktu — ne se propašovat jako oprava parity.

---

## Závěr

Z 11 řádků issue je **6 falešných nálezů** (5× HEAppE re-homing, 1× Cyclops = Gateway), **2 environmentální** (Cyclops secret, HEAppE nedostupná z lokálu), **2 designové výtky platné pro obě verze** (preferences, version) a **1 skutečný rozdíl s reálným dopadem** (`RequestTypes`).

Napříč tím se ale ukazuje jeden systémový vzorec, který stojí za víc než jednotlivé řádky:

> **Migrační ledger na několika místech tvrdí parity, kterou kód nemá** (řádek 253 o `RequestTypes` je nepravdivý ve dvou tvrzeních; řádek 195 popisuje jiný response shape než kód; `UserName`/`LastModification` chybí úplně).
> A **parity suite tuhle třídu rozdílů z definice netestuje** — field-level shape je vědomě odložený na B06/B07, takže mezi dokumentací a testy zůstává pásmo, kde se rozdíly hromadí až do ručního review.

Kdo issue #277 zavírá, měl by kromě konkrétních oprav probrat řádek po řádku i ten ledger — jinak stejný typ hlášení přijde znovu.

### Navržené kroky

| Priorita | Akce |
|---|---|
| **1** | Rozhodnout osud `HEAppECreation` (doplnit do `RequestType._names` vs. retire) a **opravit ledger řádek 253** |
| **2** | Doplnit do ledgeru dropnuté `UserName` + `LastModification` u `requests`/`Requests/Sent`; srovnat řádek 195 s reálným shape kódu |
| **3** | Naseedovat `cyclops` secret do lokálního Vaultu (odblokuje smysluplné měření) |
| **4** | Přeměřit `TemplatesInfo` na devu — potvrdit / vyvrátit řádek 8 |
| **5** | Opravit zavádějící popis u `GET v2/Users/Me/Preferences` (`.Produces<string>` vs. "JSON object") |
| **6** | Doplnit do ledgeru explicitní větu, že `/api/Heappe/*` zůstává neverzované i po retirementu v1 |
| **7** | *(mimo scope #277)* Cyclops error branch → ProblemDetails; `version`/`preferences` jako JSON = samostatný návrh na změnu kontraktu |
