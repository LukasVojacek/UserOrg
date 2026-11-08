# F0 — Role-based reads (implementační plán)

> **Cíl:** Doplnit v2 read endpointům **per-role data scoping**, aby admin viděl vše a provider-identita data svého providera — a tím odstranit falešnou paritu u `Administration/*` (admin přes v2 dnes nevidí nic platform-wide). První (de-risk) fáze [next-phase-plan.md](./next-phase-plan.md).
> **Stav:** ✅ Hotovo (F0.0–F0.4, vč. per-endpoint tier větvení a parity rows). Původní datum: 2026-06-10. Autoritativní ledger: [v1-retirement-parity.md](./v1-retirement-parity.md).

---

## Rozhodnutý mechanismus

- **Explicitní per-role větev v `HandleAsync`**, ale **shodný request/response tvar** pro endpoint. Autorizační/scoping rozhodnutí je viditelné na use-case hranici (kde žije `ProviderWriteAuthorization`), ne skryté v query vrstvě.
- **Scope-precedence resolver** (ne tři role-name checky):

  ```
  admin     caller.IsAdmin(roles)              → vše (žádný scope filtr)
  provider  caller.ProviderName is { } pn      → scope na providera pn
  user      jinak                              → caller project scope
  ```

- **Klíč provider scope = `ProviderName` claim (service-key identita), NE role-name `ProviderAdmin`.** Drží se [CONTEXT.md → Provider identity](../../CONTEXT.md) a zrcadlí `ProviderWriteAuthorization.Authorize(caller, target, roles)`. „ProviderAdmin" zůstává jen **`ApiAudience` doc label**, ne runtime scoping klíč.
- **`ApiAudience` ≠ autorizace.** GroupName řeší jen, ve kterém OpenAPI dokumentu je endpoint *popsán*; přístup hlídá Gateway RBAC. Multi-role endpoint potřebuje větvení jen tehdy, když ho volá víc rolí a každá má vidět jiná data.

### Sdílený helper (Core/Shared/Auth)
- `caller.IsAdmin(WellKnownRoles roles)` → `string.Equals(caller.Role, roles.DefaultAdminRoleName, OrdinalIgnoreCase)`.
- Provider scope = `caller.ProviderName` (už na `Caller`). Helper nemůže být čistá property (admin potřebuje `WellKnownRoles`), mirror `ProviderWriteAuthorization`.

---

## Matice (po F0.0 auditu — ověřeno v kódu + proti legacy)

> ⚠️ **Audit přepsal původní předpoklad.** Plán předpokládal, že všech 5 read endpointů je caller-scoped (admin nevidí nic → stačí *rozšířit*). Realita se štěpí na dva opačné směry: tři endpointy se **rozšiřují** (admin vidí málo), dva se **zužují** (dnes vrací úplně vše, včetně private — regrese proti legacy).

| Endpoint | v2 dnes (ověřeno) | legacy v1 (parita) | F0 směr |
|---|---|---|---|
| `GetProjects` | caller-scoped `[.. caller.Scope]` | `/api/Project` user-scoped; `/api/Administration/Project` admin-wide | **rozšířit**: + admin→vše. *Provider větev: žádná (bez legacy precedentu — viz níže).* |
| `GetResources` | caller-scoped `[.. caller.Scope]` | `/api/ProjectResource` **už 3-tier**: admin→vše · provider→svého · user→scope | **rozšířit**: + admin→vše + provider→resources providera. (v2 ztratil legacy bypass!) |
| `GetRequests` | caller-scoped `[.. caller.Scope]` | `/api/Requests` manager-scoped; `/api/Administration/Requests` admin-wide | **rozšířit**: + admin→vše. *Provider: žádná (RBAC ho nepustí — `perm:user:request:manage` nemá).* |
| `GetProviders` | **NESCOPED — vrací vše vč. private** ⚠️ | `/api/Provider`: bez claimu→public; s claimem→vlastní | **zúžit**: user→public · provider→vlastní · admin→vše (= dnešek). Opravuje únik private. |
| `GetLocations` | **NESCOPED — vrací vše vč. private** ⚠️ | `/api/Location`: provider→svého · jinak→project-linked | **zúžit**: user→project-linked · provider→svého · admin→vše (= dnešek). Opravuje únik. |
| `GetUsers` | Admin-gated, vrací vše | `/api/Administration/User` admin-wide | **žádná** — parita ✅ |
| `GetProjectTags` | global lookup | global (i `Administration/ProjectTag` = totéž) | **žádná** — parita ✅ |
| `GetStatistics` | global, bez permission | `/api/Other/Statistics` global, bez scoping | **žádná** — parita ✅ |
| `GetProjectUsers` | per-project (`caller.Scope.Contains`) | per-project | **žádná** — scoped projektem |

---

## F0.0 — výsledky auditu (2026-06-10, hotovo)

### 1. Join cesty — všechny tři **expressible** existujícími shadow FK (žádné nové entity)
- **provider → locations**: `Location.ProviderId` (shadow FK) → join na `Provider.Name`. **Už hotové** v [`LocationDetailsQuery.GetLocationsAsync`](../../src/Modules/Core.DataAccess/Queries/LocationDetailsQuery.cs) — `filter.ProviderName` se už dnes joinuje (`where prov.Name == name`). Provider-scope = jen předat jméno do `LocationFilter`.
- **provider → resourceIds**: `ProjectResource → AssignedLocation(.LocationId) → Location(.ProviderId) → Provider`. Staged-join styl potvrzen v `ResourceAssignmentQuery`. `ResourceFilter` dnes nemá provider-name vstup → **hlavní práce F0.2** (přidat provider-scope cestu do resource query). Legacy ekvivalent: `resource.AssignedLocations.Any(x => x.Location.Provider.Id == providerId)`.
- **provider → projectShortNames**: `… → AssignedProjectResourceToProject → Project`. Expressible, ale **NEPOTŘEBUJEME** — viz rozhodnutí níže. Tím odpadá nejhlubší join.

### 2. RBAC — **žádné chybějící entries** (klíčový de-risk vyřešen) ✅
Matice volatelnosti v2 GET rout (z `roleManagmentRBAC.json`):

| Routa | perm | Admin | ProviderAdmin | User |
|---|---|:--:|:--:|:--:|
| `GET v2/Projects` | `project:list` | ✓ | ✓ | ✓ |
| `GET v2/Providers` | `provider:list` | ✓ | ✓ | ✗ |
| `GET v2/Locations` | `location:list` | ✓ | ✓ | ✗ |
| `GET v2/resources` | `resource:list` | ✓ | ✓ | ✗ |
| `GET v2/Requests` | `user:request:manage` | ✓ | ✗ | ✓¹ |
| `GET v2/Statistics` | (žádný) | ✓ | ✓ | ✓ |

`Admin` (level 100) má všechna oprávnění → na widened data **vždy dosáhne**. `ProviderAdmin` (level 90) má `provider/location/resource/project:list` → dosáhne na provider-scope u providers/locations/resources/projects, **ale ne na requests**. → **F0 nepotřebuje měnit RBAC.**

¹ `user:request:manage` drží Admin/Owner/Manager; bare `User` ne. (Detail volatelnosti requests řeší existující per-role permission, F0 ho nemění.)

### 3. Rozhodnutí
- **`GetRequests` provider sémantika → žádné provider widening.** Důvod: legacy nemá provider-requests view a RBAC provider identitu na `v2/Requests` nepustí. Jen **admin** větev.
- **`GetStatistics` → žádná práce.** v2 i legacy vrací global data bez scopingu — parita ✅. (Pozn.: callable i bare-Userem, bez permission — shodné s legacy. Pokud by produkt chtěl admin-only, je to *separátní* rozhodnutí mimo F0.)
- **`GetProjects` provider větev → DROP.** Legacy nemá „provider listuje své projekty" (user-scoped + admin-wide). Provider→projects nemá paritní precedent → vynecháno. **Odpadá nejhlubší join (F0.2 zjednodušeno).**
- **`GetProviders` / `GetLocations` → ZÚŽIT, ne rozšířit.** v2 dnes vrací vše vč. private komukoli s rolí (Member/Owner/Manager). Legacy vracelo jen public/own. Je to **paritní regrese i únik private dat** → F0 přidá user-tier (public/project-linked) a provider-tier (vlastní); admin-tier už splněn.
- **`GetResources` → tracked gap navíc.** Legacy `/api/ProjectResource` měl admin/provider/user 3-tier; v2 ho zúžil jen na caller-scope. Není mezi 8 `Administration/*` routami (proto parity-doc to minul), ale je to skutečná regrese — F0 obnoví admin+provider tier.

---

## Kroky

### F0.0 — Audit (read-only, bez kódu) ✅ HOTOVO (2026-06-10)
Výsledky výše („F0.0 — výsledky auditu"). Shrnutí: join cesty expressible; RBAC bez gapů; Requests = jen admin; Statistics = bez práce; provider→projects DROP; Providers/Locations = zúžit (regrese), Resources = obnovit 3-tier.

### F0.1 — Scope helper ✅ HOTOVO (2026-06-10)
- [`ReadScope.cs`](../../src/Modules/Core/Shared/Auth/ReadScope.cs): uzavřený union `ReadScope` (`All` / `Provider(pn)` / `Scoped(projects)`) + extension `caller.IsAdmin(roles)` a `caller.ResolveReadScope(roles)`. Precedence admin > provider-identita > caller-scope, zrcadlí `ProviderWriteAuthorization`.
- **Filtry NEzměněny v F0.1** — přidání provider-scope pole do `ProviderFilter`/`LocationFilter`/`ResourceFilter` se přesunulo do **F0.2**, kde ho query reálně čte (jinak by to bylo mrtvé pole). Resolver je samostatně testovatelný, filtry ne.
- **Verify ✅:** [`ReadScopeTests`](../../Tests/Modules/Core/Core.UnitTests/Auth/ReadScopeTests.cs) — 7 testů (admin/provider/scoped precedence, case-insensitivita, admin > provider, prázdný scope ≠ All). Build čistý.

### F0.2 — Provider-scope queries + filtry
- **Filtry**: přidat provider-scope vstup tam, kde chybí — `ProviderFilter` (+`OnlyPublic`/provider-name), `LocationFilter` (provider-name; query už umí `filter.ProviderName`), `ResourceFilter` (provider-name). `ProjectFilter`/`RequestFilter` mají `ScopeShortNames` (null=vše pro admin) → stačí.
- **Query**: **`provider → resourceIds`** (přes `AssignedLocation → Location.ProviderId`) je hlavní práce. **`provider → locations`** už `LocationDetailsQuery` umí → jen prodrátovat. **`provider → projectShortNames` se NEDĚLÁ** (GetProjects nemá provider větev).
- Styl `IDataQuery<T>` staged join (jako `ResourceAssignmentQuery`).
- **Verify:** query unit testy přes EF in-memory (provider má/nemá entity; soft-delete hrany).

### F0.2 — HOTOVO ✅ (2026-06-10)
- ✅ **Providers** — `ProviderFilter.OnlyPublic` + `ProviderDetailsQuery` (`Where(!IsPrivate)`). Vedlejší fix: řazení přesunuto za materializaci (`OrderBy(Name.Value)`) — řazení podle VO `ProviderName` v query běží na SQL, ale EF in-memory ho neumí (ProviderName není `IComparable`). [`ProviderDetailsQueryTests`](../../Tests/Modules/Core/Core.UnitTests/Providers/ProviderDetailsQueryTests.cs) 3 testy.
- ✅ **Locations** — `LocationFilter.ScopeShortNames` + dvoufázové `LocationDetailsQuery` (pre-query location-ids z `AssignedLocation→AssignedProjectResourceToProject→Project`). [`LocationDetailsQueryTests`](../../Tests/Modules/Core/Core.UnitTests/Locations/LocationDetailsQueryTests.cs) 4 testy.
- ✅ **Resources** — `ResourceFilter` (`ScopeShortNames` nullable + `ScopeProviderName`); `ResourceDetailsQuery.LoadAsync` restrukturalizováno: tier-specifická **Stage 0** (resolve candidate resource-ids: user→přes projekty, provider→přes `AssignedLocation→Location.ProviderId`, admin→null=vše) + **Stage A** bez project-joinu (project-less resources přežijí adminu/providerovi). `projectShortName` jako průnik. [`ResourceDetailsQueryTests`](../../Tests/Modules/Core/Core.UnitTests/Providers/ResourceDetailsQueryTests.cs) 5 testů.
- **Baseline:** full Core.UnitTests = jen 8 známých live-SQL `*DatabaseAccessTests` selhává; 16 nových F0.2 query testů zelených.

### F0.3 — Per-endpoint větvení — ✅ hotovo
Pořadí (vzestupně dle náročnosti):
1. `GetProviders` — **zúžit**: user→**relevant** (public **+** private dosažitelné přes uživatelovy projekty) · provider→`Name==pn` · admin→vše. (+ přidat `Caller` do handleru — dnes ho neinjektuje.) Vyžaduje novou query „providers reachable via projects" (`Project→AssignedProjectResourceToProject→ProjectResource→AssignedLocation→Location→Provider`) — inverze location-scope chainu. Rozhodnuto 2026-06-10 (full parita, viz Review níže).
2. `GetLocations` — **zúžit**: user→project-linked · provider→`ProviderName==pn` (reuse existující filter) · admin→vše.
3. `GetResources` — **rozšířit**: user→`caller.Scope` (dnešek) · provider→resourceIds providera · admin→vše.
4. `GetProjects` — **rozšířit**: user→`caller.Scope` (dnešek) · admin→vše. *(žádná provider větev)*
5. `GetRequests` — **rozšířit**: user→`caller.Scope` (dnešek) · admin→vše. *(žádná provider větev)*
- Každý: explicitní `if (caller.IsAdmin) … else if (caller.ProviderName is { } pn) … else …`, shodný Response; reuse F0.2 queries.
- **Verify:** handler testy **per endpoint per tier** (zejm. provider/admin u Providers/Locations vidí správně zúžené/plné; private se neukáže non-adminovi).

### F0.4 — Parity rows + dokumentace — ✅ hotovo
- `v1-retirement-parity.md`: 8 `Administration/*` GET řádků → ✅; doplnit nový řádek/poznámku pro **GetResources** regresi (mimo Administration-8) a opravit chybný předpoklad „Providers/Locations caller-scoped".
- Aktualizovat next-phase-plan F0 stav.
- **Verify:** parity řádky zelené; build + Core.UnitTests baseline (0 regresí).

---

## Review F0.1+F0.2 (2026-06-10) — nálezy a akce

Dvě nezávislé adversariální review pasáže nad celým F0.1+F0.2 changesetem.

**Opraveno hned:**
- 🐞 **Stage B v `ResourceDetailsQuery` načítal soft-deleted assignmenty** (žádný `!al.IsDeleted`; `AssignedLocation` nemá global query filter). **Pre-existující** bug (před F0.2 restrukturalizací), ale ve změněné metodě a proti legacy paritě (`ProjectResourceRepository` filtruje `.AssignedLocations.Where(!IsDeleted)`). Opraveno + zkorumpoval i locationType post-filtr.
- ✅ **Soft-delete pokrytí doplněno** — žádný fixture dřív neseedoval smazaný link, takže všechny `!IsDeleted` predikáty byly netestované. Přidáno 6 testů (resource provider/user tier vyloučí smazaný assignment/link; Stage B vyloučí smazaný assignment z `Assignments`; location user tier vyloučí smazaný link i assignment).
- ✅ Posílen provider name-filter test (2. provider, aby `Single` dokázal vyloučení) + `ReadScope` test pro caller s providerName claimem *i* project-rolemi → `Provider` (scope se neunionuje).
- 📝 Upřesněn komentář u řazení v `ProviderDetailsQuery` (shodná množina, tie-break pořadí se může lišit od DB collation).

**Rozhodnutí pro F0.3:**
- ✅ **GetProviders user tier = „relevant" (full parita).** Rozhodnuto 2026-06-10. Legacy měl pro non-provider callera *dvě* cesty: `GetPublicProvidersAsync` (jen public) **a** `GetRelevantProvidersAsync` (providery dosažitelné přes uživatelovy projekty, vč. private). User tier tedy vrací **public + project-reachable private** (ne jen public) — jinak by uživatel ztratil viditelnost private providera hostujícího jeho vlastní projekt (regrese proti v1). Přidává query „providers reachable via projects" (viz F0.3 krok 1). `ProviderFilter.OnlyPublic` (z F0.2) tím **nestačí sám** pro user tier — bude doplněn o relevant-scope vstup.
- **By-id endpointy** (`GetResource`/`GetProvider`/`GetLocation`) také potřebují tiering ve F0.3 — dnes `GetResourceAsync` bere jen `scope` (provider/admin by-id by spadl na user scope). Audit Administration-8 zahrnoval `Provider/{id}`, `Location/{id}`.
- **Private locations a user tier** — `LocationDetailsQuery` private lokace nefiltruje; user je vidí přes reachability. Odpovídá legacy „relevant locations" cestě (která private taky nefiltrovala) → pravděpodobně **záměrná parita**, ne bug. Potvrdit u GetLocations rozhodnutí výše.

## Otevřené / rizika
- **provider → resourceIds join** (F0.2) — jediná netriviální query práce; cesta potvrzena (shadow FK `AssignedLocation.LocationId → Location.ProviderId`), legacy ekvivalent existuje.
- **Private-data únik (GetProviders/GetLocations) — ✅ vyřešeno F0.3.** Před F0.3 v2 vracelo private data komukoli s rolí (latentní expozice, regrese proti legacy); F0.3 to zúžilo (user → dosažitelné přes projekty, provider → vlastní, admin → vše). Historická poznámka, ne otevřený stav.
- **Předpoklad: provider identita nese roli `ProviderAdmin`** (a tím `*:list` permissions) společně s `ProviderName` claimem. RBAC matice to potvrzuje pro ProviderAdmin roli; pokud by provider service-key nesl jinou roli bez `*:list`, provider-tier by na data nedosáhl. Drží legacy mechanismus (ProviderAdmin role → provider claim).
- **Parita testy (F7)** stále nutné před cutoffem — F0 přidává per-tier unit testy, ne v1↔v2 srovnání.
