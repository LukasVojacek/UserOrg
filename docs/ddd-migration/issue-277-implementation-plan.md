# Issue #277 — implementační plán

**Vstup:** [work item 277](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/work_items/277) + [analýza](./issue-277-v1-v2-differences-analysis.md)
**Cílová větev:** `feat/refactor-core` (**ne `main`** — modulární Core na mainu nesestaví)
**Datum:** 2026-08-11

---

## Východisko, které řídí celý plán

**v2 je gated za `FeatureManagement:V2Api`, default off** (commit `8be6053f`). Žádný produkční klient dnes v2 kontrakt nekonzumuje. To je zásadní: breaking changes ve v2 jsou **teď levné a po zapnutí flagu drahé**. Plán proto všechny tvarové změny kontraktu dělá naráz a hned, ne postupně.

**Content negotiation se dělat nebude** — rozhodnuto 2026-08-11. v2 vrací vždy JSON objekt, `text/plain` varianta nevznikne. v1 zůstává beze změny až do retirementu. Analýza nákladů zůstává zaznamenaná v [analytickém dokumentu](./issue-277-v1-v2-differences-analysis.md) pro případ, že by se požadavek vrátil.

---

## Rozhodovací brány

| # | Otázka | Stav | Ovlivňuje |
|---|---|---|---|
| **D1** | Má `RequestType` hodnotu `3` obnovit, nebo prohlásit za retired? | **Obnovit** — default plánu (levnější, zavře i read path); lze rozhodnout interně | WP-1 |
| **D2** | Content negotiation pro `version` / `preferences`? | ✅ **Rozhodnuto 2026-08-11: ne, vždy JSON** | — (WP-9 zrušeno) |
| **D3** | Zobrazuje frontend `UserName` / `LastModification` ze `SentRequests`? | ⏳ **Otevřeno** — čeká na @marek.bober / FE, položeno v [note_40462](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/work_items/277#note_40462). Default: **ne** → jen dokumentace | WP-10 (jinak se neprovádí) |

---

## Work packages

### WP-1 — `RequestType`: obnovit `HEAppECreation` (Id 3)

**Problém.** v2 vrací 3 položky místo 4. Navíc `RequestType.IsValid` přijímá persistovaný rozsah 0–10, ale `_names` má jen 0/1/2 → řádek s `Type = 3` se ve `requests` i `Requests/Sent` tiše vypíše jako `"Unknown"` a `TryParseName("HEAppECreation")` selže.

**Změna.**
- `src/Modules/Core.Domain/Request/RequestType.cs` — přidat `[3] = "HEAppECreation"` do `_names` + statickou property `HEAppECreation`
- `src/Modules/Core/Features/Enums/GetRequestTypes/GetRequestTypesEndpoint.cs` — přidat položku do `_types`
- `Tests/Modules/Core/Core.UnitTests/Enums/EnumEndpointsTests.cs:77` — rozšířit `Assert.Collection` o čtvrtou položku

**Vědomě neměníme:** `0` zůstává `Default`, nepřejmenovává se zpátky na `Unknown`. `Name` je součástí wire kontraktu (`Type` v response), `Default` je lepší jméno pro reálnou hodnotu a rozdíl se zdokumentuje v ledgeru (WP-7). Alternativu jsem zvážil a zamítl — parity kvůli parity nestojí za horší jméno v novém kontraktu.

**Ověření.** Unit test zelený; `GET /api/v2/RequestTypes` vrací 4 položky; request s `Type=3` v DB se vypíše jako `"HEAppECreation"`.

**Odhad.** 1 h.

---

### WP-2 — `GET v2/Version` vrací objekt

**Problém.** Vrací holý string přes `Results.Content(version, "text/plain")`. Navíc **OpenAPI snapshot dnes lže**: `API_v2-user.json` deklaruje u `/api/v2/Version` `content: application/json` se `schema: {type: string}`, zatímco runtime posílá `text/plain`. Klient generovaný ze specu dostane něco jiného, než čeká.

**Změna.** `src/Modules/Core/Features/Meta/GetVersion/GetVersionEndpoint.cs`
- zavést `internal sealed record Response(string Version)`
- `HandleAsync` → `Results.Ok(new Response(version))`
- `.Produces<Response>(StatusCodes.Status200OK)`

Schema id kolize nehrozí — ADR-0062 kvalifikuje nested typy podle deklarujícího endpointu (`GetVersionEndpoint.Response` → `GetVersionResponse`), viz `V2CoreExtensions.SchemaReferenceIdFor`.

**Ověření.** `GET /api/v2/Version` → `{"version":"0.0.0.1"}` s `application/json`; snapshot `API_v2-user.json` už nemá `type: string`.

> **Korekce (2026-08-12, po integraci).** Tělo výše je psáno lowercase (`"version"`) jen jako záměr; nasazený kontrakt je `{"Version":"0.0.0.1"}` (PascalCase), protože `HostInfrastructureExtensions.cs:90` nastavuje `PropertyNamingPolicy = null`, stejně jako u všech ostatních v2 `Response` typů.

**Odhad.** 30 min (+ WP-5 na testy).

---

### WP-3 — `v2/Users/Me/Preferences` vrací objekt a je symetrické

**Problém.** GET vrací `Task<string>` → uvozovkovaný JSON řetězec. PUT přitom bere `[FromBody] JsonElement`, tedy skutečný objekt. **Odpověď z GETu nejde poslat zpátky do PUTu bez rozbalení.** Zároveň klient nerozliší „nemá preference" (`"{}"`), „prázdný string" (`""`) a „nepřišlo nic".

**Změna.**
- `GetUserPreferencesEndpoint.cs` — `internal sealed record Response(JsonElement Preferences)`; uložený string parsovat přes `JsonDocument.Parse`; **guard na nevalidní uložený JSON** (historická data) → fallback na prázdný objekt + `LogWarning`, nikdy 500
- `UpdateUserPreferencesEndpoint.cs` — `internal sealed record Request(JsonElement Preferences)`, `.Accepts<Request>("application/json")`; do domény jde dál `preferences.GetRawText()`
- opravit zavádějící XML popisek u GETu (dnes tvrdí „Returns the JSON preferences object", ale `.Produces<string>`)

**Proč měnit i PUT.** Asymetrie je jádro nálezu. Kdybychom obalili jen GET, prohloubíme ji. Jeden koordinovaný řez teď (flag je off) je levnější než dva později.

**Ověření.** Round-trip: `GET` → tělo beze změny do `PUT` → `204` → `GET` vrátí totéž. Uložený nevalidní JSON nevrací 500.

**Odhad.** 3 h včetně testů.

---

### WP-4 — Zavřít díru ve v2 contract standards

**Problém.** `v2-contract-standards-fanout-plan.md` zakazuje bare-string u **chyb** (T1) a předepisuje envelope u **listů** (T5), ale nikde neříká, že každé 200 tělo je JSON objekt. Sweep guards kontrolují jen chybové cesty. `GetVersion` je v PR 7 výslovně odbytý jako *„no list change; T7 only"*. WP-2 a WP-3 proto prošly standardem legálně — bez tohohle WP se stejná vada vrátí u dalšího endpointu.

**Změna.**
- do `v2-contract-standards-fanout-plan.md` přidat transformaci **T9 — „every 200 body is a JSON object"** (výjimka: `204 No Content` a binární download)
  > **Korekce (2026-08-12, při implementaci).** Formulace „every 200 body is a JSON object" výše je nepřesná a ponechána jen jako záznam původního záměru — doslovně vzato by prohlásila ~20 nasazených list endpointů za nekonformní. Skutečné znění je **„no primitive 200 body"**: objekt i holé pole jsou v pořádku, zakázaný je jen skalár (`string` / `number` / `boolean`). Závazná je sekce T9 v [`v2-contract-standards-fanout-plan.md`](v2-contract-standards-fanout-plan.md#t9--primitive-200-body--object-response).
- rozšířit sweep guards o `rg "Results\.(Content|Text)\(" src/Modules/Core/Features` a `rg "Produces<string>" src/Modules/Core/Features`
- **automatizovat to testem, ne jen bash řádkem v dokumentu**: nový test načte commitnuté `API_v2-*.json` a assertuje, že žádná `200` odpověď nemá `content.application/json.schema.type == "string"` (případně jiné primitivum). Snapshoty jsou v repu, takže test nepotřebuje běžící aplikaci ani reflexi.

**Ověření.** Test je červený před WP-2/WP-3 a zelený po nich.

**Odhad.** 3 h.

---

### WP-5 — Testy a OpenAPI snapshoty

**Problém.** WP-2 mění tvar odpovědi, kterou dnes porovnává parity test, a všech 5 snapshotů v rootu se generuje při buildu (`OpenApiDocumentsDirectory = ../../..`) a je commitnutých.

**Změna.**
- `Tests/E2E/IntegrationTests/Parity/StaticParityTests.cs:118` — `Should_MatchStatusAndPlainTextVersion_When_VersionReadThroughGateway` dnes porovnává obě těla jako plain text. Přepsat: v1 tělo je text, v2 se parsuje a porovnává se `version` field. **Nepřeskakovat** — zůstává to jediný cross-stack test na tenhle endpoint.
  > **Korekce (2026-08-12, po integraci).** Metoda byla při přepisu přejmenována; aktuální název je `Should_MatchStatusAndVersionValue_When_VersionReadThroughGateway` (`StaticParityTests.cs:121`).
- regenerovat a commitnout `API_v2-user.json` (dotčené `/api/v2/Version`, `/api/v2/Users/Me/Preferences`, `/api/v2/RequestTypes`); ostatní čtyři zkontrolovat na nulový diff
- readiness probes (`GatewayAttachFixture.cs:261`, `TestcontainersTopologyFixture.cs:243`) kontrolují jen status → **beze změny**, ale ověřit

**Odhad.** 2 h (z toho půlka review snapshot diffu).

---

### WP-6 — Cyclops secret do lokálního Vaultu

**Problém.** `vault.GetExternalCredentials("cyclops")` lokálně selže → `500 "Server secrets configuration error"`. Není to bug forwarderu, je to chybějící seed. Dokud to není, řádek 5 v issue se nedá změřit.

**Změna.** Doplnit `cyclops` credentials do lokálního Vault seedu v AppHost.

**Ověření.** `GET /api/Other/CyclopsUsage/testproject/atr-26-1` přes gateway vrátí buď data, nebo upstream chybu — ne 500 ze secretů.

**Odhad.** 1 h. **Nezávislé na ostatních WP**, může jít první.

---

### WP-7 — Oprava migračního ledgeru

**Problém.** `legacy-to-refactored-endpoint-migration.md` na třech místech slibuje paritu, kterou kód nemá. **Tohle je příčina většiny nálezů v #277** — reportér oprávněně hlásil rozdíl proti tomu, co dokumentace slibuje.

**Změna.**
| Řádek | Co je špatně | Oprava |
|---|---|---|
| 253 | *„v2 includes `{Id:0,"Unknown"}` to match legacy"* — nepravda v obou tvrzeních | popsat skutečný stav po WP-1: 4 položky, `0` = `Default` (vědomá odchylka) |
| 195 | slibuje `{ProjectId, UserId, ProviderId}` | kód vrací `ProjectShortName`, `IdpUserId`, `ProviderName` |
| 195/196 | neuvádí dropnuté `UserName` a `LastModification` | doplnit |
| 211 | *„No shape change — returns raw JSON string"* | přepsat podle WP-3 |
| 263 | *„returns version string. No breaking change"* | přepsat podle WP-2 |
| nová | `/api/Heappe/*` zůstává **neverzované** i po retirementu v1 | doplnit + zmínit ve Scalar dokumentaci v2 |

**Odhad.** 2 h.

---

### WP-8 — Ověření `TemplatesInfo` na devu

**Problém.** Nález není potvrzený — kontrakty jsou shodné pole po poli a prázdné `Nodes` odpovídají nedostupné HEAppE z lokálu.

**Postup.** Změřit `GET /api/v2/Projects/testproject/Templates` na devu **přes gateway**. Pokud vyjde `isAvailable: true` a zároveň `nodes: []`, je to skutečný bug → samostatné issue, mimo tento plán.

**Odhad.** 1 h. Blokováno dostupností dev prostředí.

---

### ~~WP-9 — Content negotiation~~ — **zrušeno (D2)**

Rozhodnuto 2026-08-11: content negotiation se dělat nebude, v2 vrací vždy JSON objekt. Odhad 1–1,5 dne odpadá. Analýza variant a nákladů zůstává v [analytickém dokumentu](./issue-277-v1-v2-differences-analysis.md), kdyby se požadavek vrátil.

Praktický důsledek pro WP-2/WP-3: **žádný `Accept` branching, žádný `Results.Content`** — jen `Results.Ok(record)`. Tím se ty dva WP zjednodušují na to, co je v nich popsané.

---

### WP-10 — `SentRequests`: obnovit pole *(podmíněno D3 = ano)*

Pokud FE `UserName` nebo `LastModification` zobrazuje, doplnit je do `RequestSummary` + `GetSentRequestsEndpoint.Response` + `RequestDetailsQuery` (`UserName` přes existující korelovaný subquery nad `users`, `LastModification` je sloupec na `Request`). **Jinak jen dokumentace ve WP-7.**

`RequestId` se neobnovuje — ADR-0024 (zero surrogate GUIDs) je vědomé rozhodnutí, ne opomenutí.

**Odhad.** 3 h, pokud nastane.

---

## Sekvence a MR struktura

```
MR-A (kontrakt)     WP-1 → WP-2 → WP-3 → WP-4 → WP-5
MR-B (dokumentace)  WP-7                              ← lze paralelně, merge až po MR-A
mimo MR             WP-6 (infra), WP-8 (ověření)      ← nezávislé, hned
podmíněné           WP-10 (D3)
```

**Proč jeden MR na kontrakt:** WP-2 a WP-3 mění snapshoty, WP-4 přidá test, který je bez nich červený, WP-5 ty snapshoty commituje. Rozdělit to znamená mít mezistav s červeným buildem.

**WP-7 se mergeuje až po MR-A**, aby ledger nepopisoval stav, který ještě není v kódu.

---

## Build a test — provozní poznámky

```bash
dotnet build
```

Bez `-warnaserror` na CLI — `TreatWarningsAsErrors` je per-projekt (`Core`/`Core.Domain` ho mají zapnutý), blanket flag falešně shodí build na starším dluhu jinde.

Testy běží na xUnit v3 / MTP s `UseArtifactsOutput=true`. Filtrovat se musí přes `--filter-query "/*/*/*/<Method>"` — **VSTest syntaxe `--filter` na MTP nevybere nic** a skončí `total: 0`, `error: 1`, exit 8, takže vypadá jako zelený běh bez testů. Pouštět exe z `artifacts/bin` s `--filter-class` / `--filter-method` potřeba není; `--filter-query` funguje přímo přes `dotnet test`.

E2E fixture má cestu k seedu napevno na hlavní checkout — běh z worktree potřebuje override `Parameters__GatewayRoleManagementSeedFilePath`.

Živé ověřování v1/v2 vždy **přes gateway** (`https://gateway-aspire.dev.localhost:5800`), nikdy přímo na Core API.

---

## Rizika

| Riziko | Dopad | Ošetření |
|---|---|---|
| Snapshot diff zasáhne i nedotčené endpointy | falešně velký MR, těžký review | zkontrolovat všech 5 souborů; nenulový diff mimo 3 dotčené cesty vyšetřit, ne odmávnout |
| Nevalidní JSON v `UserPreference.Data` | 500 na GET preferences | explicitní guard ve WP-3, fallback + warning |
| Někdo zapne `V2Api` mezi MR-A a nasazením FE | rozbité čtení preferencí | flag nechat off do potvrzení FE; koordinovat s D3 |
| Dev běží jiný build než repo | analýza stojí na chybném základu | ověřit při WP-8 — pokud v1 `preferences` na devu vrací objekt, repo a dev se rozcházejí |

---

## Mimo scope

- Cyclops error branch → ProblemDetails (dnes plain-text 500) — reálné, ale nesouvisí s #277
- **B06/B07 — field-level shape parity.** Tři nálezy v #277 padají přesně do díry, kterou B06/B07 mají zavřít (`RegressedListEndpointsParityTests` dnes testuje jen status + řazení, enumy jen tvar položky). WP-4 zavírá jen podmnožinu „bare-string success". Dokud B06/B07 neproběhne, další ruční review najde další rozdíly — stojí za zvážení, jestli #277 není argument pro její upřednostnění.

---

## Souhrn odhadu

| | Rozsah |
|---|---|
| Jádro (WP-1…WP-8) | **~2 člověkodny** |
| + WP-10, pokud D3 = ano | +0,5 dne |
| ~~WP-9 content negotiation~~ | ~~+1–1,5 dne~~ — zrušeno (D2) |
