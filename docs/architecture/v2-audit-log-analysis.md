# Analýza auditního logu pro v2 (modulární Core)

Stav ke dni 2026-08-12, větev `claude/audit-log-analysis-v2-34a157`. Pouze analýza — žádná implementace.

## 1. Výchozí stav: audit log neexistuje

| Co dokumentace tvrdí | Realita v kódu |
|---|---|
| ADR-0004 "SQL Server + immudb pro tamper-evident audit" (accepted, 2026-02-02) | Žádný immudb klient, žádný NuGet balíček (`grep -i immu` v `Directory.Packages.props` = 0 hitů) |
| `docs/architecture/diagrams.md:17,27` — hrana `API -->|audit| ImmuDB` | Hrana v kódu neexistuje |
| `CLAUDE.md:24` — "immudb (immutable audit log)" | `src/UserOrg.AppHost/ImmuDbExtensions.cs:5-27` startuje kontejnery immudb + immugw, ale obojí `.ExcludeFromManifest()` → **jen lokální dev, nikdy nasazené**; `Program.cs:56` zahodí návratovou hodnotu, žádné `.WithReference()` |
| `docs/components/core/features/endpoint-access.md:10,125` — konfigurační klíč `"AuditLog": true` | Klíč ani kód neexistuje |
| Health check `audit` (`src/UserOrg.ServiceDefaults/Extensions.cs:163`) | `Extensions.cs:179` načte `immuGwUrl` a **nikdy ho nepoužije** — mrtvá proměnná, chybí `AddUrlGroup`; test `HealthCheckRegistrationTests.cs:167` to netestuje |

Co v repu pod jménem "audit" je:

- **Auditní sloupce** (`CreatedAt` / `ModifiedAt` / `IsDeleted`) — v2 přes `Core.DataAccess/Conventions/AuditableEntityConventions.cs:24-49` + stampování v `Contexts/DatabaseContext.UnitOfWork.cs:67-86`. **Neobsahují aktéra** — `IAuditableEntity` nemá `CreatedBy`/`ModifiedBy`. To je zásadní mezera: identita volajícího se dnes nikam nepersistuje.
- **HEAppE Resource Validity Audit** (`src/Modules/Integrations/Heappe/Features/ResourceValidityAudit/`) — kolize jmen, jde o doménovou kontrolu platnosti HPC zdrojů, ne o who-did-what.
- **StepUp request logging** (`Gateway/Program.cs:121`, `Core/API/Program.cs:153`) — Serilog Console/File/Loki. Level-driven, sampled, rotované, nemanipulace-odolné. **Není auditní stopa.**

Žádný `ISaveChangesInterceptor` v repu neexistuje — ten seam je volný.

## 1b. StepUpLogging 3.5.0 už auditní framework má

Používáme `Lukdrasil.StepUpLogging` 3.5.0 (`Directory.Packages.props:33`). **Verze 3.5.0 (vydaná 2026-08-05) je právě ta, která audit logging zavedla** — jsme na ní, ale nepoužíváme z něj nic. V repu není jediný výskyt `AddAuditLogging`, `IAuditLogger` ani `IAuditEventSink`.

Co knihovna dává:

| Prvek | Popis |
|---|---|
| `AddAuditLogging<TSink>()` | Registrace; **vyžaduje předchozí `AddStepUpLogging()`**, jinak host nenastartuje |
| `IAuditEventSink.WriteAsync(AuditEvent)` → `ValueTask` | Jediný bod perzistence. Sink smí být Scoped i Singleton |
| `IAuditLogger<T>`, `AuditAsync(evt, log => …)` | Scoped služba k injektáži do handlerů; druhý parametr je volitelná doprovodná log akce |
| `AuditEvent` | Volající plní `Action`, `ActorId`, `Outcome` (Success/Failure/Denied), volitelně `TargetType`, `TargetId`, `Reason`, `Data`, `ActorType`, `OnBehalfOfId`, `TenantId` |
| Automaticky doplněno | `TimestampUtc`, `TraceId`, `SpanId` (z OpenTelemetry), `SourceIp` (podle client-IP pravidel StepUpu), `UserAgent` (vždy redigovaný) |
| Metriky | Čítače pod meterem `StepUpLogging.Audit` (počet událostí a selhání) |

Zápisy auditu **obcházejí Serilog i step-up gating** a jdou přímo do sinku s awaitovaným zápisem — tedy nepodléhají vzorkování ani úrovni logu. To je přesně sémantika, kterou audit potřebuje a kterou `UseStepUpRequestLogging` (§1) nemá.

Předpoklad je splněný: `src/UserOrg.ServiceDefaults/Extensions.cs:37` volá `builder.AddStepUpLogging(...)`, takže Core API i Gateway framework už mají. Chybí jen `AddAuditLogging<TSink>()` a vlastní sink.

**Co knihovna nedává:**

1. **Žádný hotový sink.** Perzistence je celá na nás — rozhodnutí immudb vs. append-only SQL (§5.1) tím zůstává otevřené beze změny.
2. **Žádná tamper-evidence.** Knihovna zapisuje, neřetězí ani nepodepisuje. Hash-chain / immudb je pořád naše práce.
3. **`ActorId` si plní volající.** Knihovna doplní síťový kontext (IP, UA, trace), ale ne identitu — tu musíme vzít z `Caller` (`src/Modules/Core/Shared/Auth/Caller.cs:26`). Mezera §2 „aktér se nikam nepersistuje" tím není vyřešená, jen zůstává na jednom, jasně definovaném místě.
4. **Výjimky ze sinku propagují beze změny.** Selhání auditu tedy shodí uživatelský požadavek. Pro audit je to obhajitelné (fail-closed), ale je to vědomé rozhodnutí — zvlášť u anonymních cest, kde by výpadek úložiště zablokoval schvalování resource requestů.
5. **Retry a ordering nejsou dokumentované.** Nespoléhat na ně.
6. **Redakce se nevztahuje na argumenty message template** — jen na query, hlavičky a těla.
7. Vlastní datový obsah v `Data` je volný slovník — schéma auditní události si musíme definovat sami a držet stabilní.

### Dopad na architekturu

`IAuditLogger<T>` je scoped, HTTP-tvarovaná služba. To posouvá doporučení z §2: **primární seam je use-case / endpoint vrstva, ne outbox.** Tam je `Caller` po ruce, což řeší nejdražší část problému, a hlavně to jako jediné umí zachytit věci, které přes doménové události nikdy neprojdou:

- autorizační selhání (`Outcome = Denied`) v Gateway — `AuthorizationHelpers.cs:36-38` i obě fail-open větve (`:21-24`, `:90-93`),
- neplatné / expirované / spotřebované tokeny na anonymních cestách (`ResourceRequestActionTokenResolver.cs:23-29`),
- operace bez doménové události — členství a role v projektu (§3).

Outbox tím neztrácí smysl: README uvádí zápis sinku „ve stejné transakci jako business operace" (EF příklad), takže sink může psát do stejné SQL transakce jako `IUnitOfWork`. Rozumné rozdělení:

- **`IAuditLogger` v endpointu** — kdo, co, s jakým výsledkem, včetně odmítnutí a neúspěchů. Pokrývá celé P1.
- **Outbox / doménové události** — jen tam, kde je potřeba spolehlivý přenos změny stavu do jiného systému. Pro audit samotný ne.

Tím odpadá potřeba převádět best-effort události na `IDurableEvent` kvůli auditu (§3) — zůstává to jako samostatná otázka pro cache a integrace.

### Otevřené body specificky ke knihovně

- Ověřit skutečný tvar `AuditEvent` proti sestavené 3.5.0 — v `~/.nuget/packages` je na tomto stroji restorovaná jen 1.10.0, která audit API neobsahuje; README je jediný zdroj, ze kterého tato sekce vychází.
- Rozhodnout chování při výpadku sinku (propagace vs. bufferování) — u anonymních schvalovacích cest to má přímý dopad na dostupnost.
- Zavést sdílený katalog hodnot `Action` (např. `project.user.role.changed`), aby audit šel dotazovat; jinak se z volného stringu stane bahno.
- Napojit čítače `StepUpLogging.Audit` na alerting — selhání auditu musí být viditelné, ne tiché.

## 2. Druhý seam: outbox, ne interceptor

`Core.DataAccess/Contexts/DatabaseContext.UnitOfWork.cs:20-65` už dělá přesně to, co audit potřebuje:

1. `CollectDomainEvents()` (`:88`) posbírá události z agregátů před uložením,
2. `OutboxRouting.Partition` (`:30`) rozdělí durable / best-effort,
3. durable události se zapíšou do `OutboxDbContext` **ve stejné transakci** jako změna agregátu (`:43-57`, ADR-0028).

Transakční atomicita zápisu auditu je otevřená konzekvence ADR-0004 — outbox ji už řeší. Doporučení: audit jako odběratel outboxu, ne nový interceptor.

Dvě věci, které outbox dnes nenese a audit je potřebuje:

- **aktér** (IdpId, role, provider/project scope, correlation id, IP) — `Caller` (`src/Modules/Core/Shared/Auth/Caller.cs:26`) existuje jen v HTTP scope, do domény se nedostane;
- **před/po hodnoty** — události nesou jen svůj payload.

Rizika k pojmenování: durable payload používá `Type.FullName` jako diskriminátor (`Outbox/OutboxRouting.cs:59-68`) — přejmenování event recordu osiří rozpracované řádky.

## 3. Mezery v pokrytí doménovými událostmi

Ne všechny bezpečnostně citlivé mutace dnes vůbec vydávají událost. Toto je **hlavní důvod, proč audit nelze postavit čistě na existujících eventech**:

| Mutace | Metoda | Událost? |
|---|---|---|
| Přiřazení uživatele do projektu | `src/Modules/Core.Domain/Project/Project.cs:275` `AssignUserWithRole` | **žádná** |
| Odebrání uživatele z projektu | `Project.cs:292` `UnassignUserWithRole` | **žádná** |
| Změna role v projektu | `Project.cs:322` `ChangeUserRole` | **žádná** |
| `AssignUser` / `UnassignUser` | `Project.cs:225`, `:251` | **žádná** |

Cache invalidace se u nich dělá imperativně v endpointu (`ChangeProjectUserRoleEndpoint.cs:77-78`). Tři nejcitlivější operace v systému tedy nemají doménovou stopu.

Naopak pokryté a durable (→ outbox, spolehlivé): `ProjectResourceAssignmentChangedEvent` (`Project.cs:453`, `:478`), `ResourceAssignmentChangedEvent` (`Provider.cs:257`, `:286`, `:306`), `ProjectResourceActivatedEvent` (`ProjectResource.cs:89`).

Pokryté, ale **best-effort** (in-memory kanál, at-most-once — pro audit nedostatečné): všech 10 událostí `User` (`Core.Domain/User/UserEvents.cs`), včetně bezpečnostně kritických `UserPromotedToProviderAdminEvent` (`User.cs:115`) a `UserDemotedToUserEvent` (`User.cs:138`); `ProjectCreated/Deleted`; `ProviderCreated/Deleted`, `LocationAdded/Removed`; všechny `OnboardingRequest*` a `Request*`.

→ Pokud audit poteče přes události, tyto musí přejít na `IDurableEvent`.

## 4. Katalog míst, kde audit má být

82 v2 endpointů, z toho 41 mutujících. Priorita P1 = bez auditu je systém neobhajitelný, P2 = potřeba pro provozní dohledatelnost, P3 = volitelné.

### P1 — Bezpečnost a přístup

| Operace | Endpoint | Proč |
|---|---|---|
| Změna role v projektu | `src/Modules/Core/Features/Project/ChangeProjectUserRole/ChangeProjectUserRoleEndpoint.cs:89` | Eskalace oprávnění; hierarchie se vyhodnocuje z `projectRoles` claimu (`:64-68`) |
| Přiřazení uživatele do projektu | `Project/AssignProjectUser/AssignProjectUserEndpoint.cs:78` | Získání přístupu k datům projektu |
| Odebrání uživatele z projektu | `Project/UnassignProjectUser/UnassignProjectUserEndpoint.cs:77` | Odebrání přístupu |
| Povýšení na ProviderAdmin | `User/PromoteToProviderAdmin/PromoteToProviderAdminEndpoint.cs:61` | Systémová role, admin-only |
| Degradace na platform usera | `User/ResetToPlatformUser/ResetProviderAdminEndpoint.cs:57` | Systémová role, admin-only |
| Smazání projektu | `Project/DeleteProject/DeleteProjectEndpoint.cs:54` | Kaskádově soft-deletuje všechny `UserPermission` řádky |
| Přijetí onboarding requestu | `OnboardingRequest/AcceptOnboardingRequest/…:118` | Atomicky přidá žadatele jako člena |
| Zamítnutí onboarding requestu | `OnboardingRequest/RejectOnboardingRequest/…:80` | Rozhodnutí o přístupu |
| Přijetí / zamítnutí requestu | `Request/AcceptRequest/…:101`, `Request/RejectRequest/…:71` | Accept mění členství projektu |

### P1 — Anonymní token-authorized cesty

Nejvyšší riziko: rozhodnutí dělá nepřihlášený volající, autoritou je držení tokenu z e-mailu. **Dnes o nich neexistuje žádný záznam kdo/odkud.**

| Operace | Endpoint |
|---|---|
| Vyřízení resource requestu (approve/reject PI odkazem) | `ResourceRequest/HandleResourceRequestAction/…:90` (`.AllowAnonymous()` na `:91`) |
| Náhled resource request akce | `ResourceRequest/GetResourceRequestAction/…:51` |
| Použití self-onboarding action linku | `src/Modules/Core/Features/Request/HandleActionLink/HandleActionLinkEndpoint.cs:140` (invaliduje sourozenecké odkazy, `Request.cs:107`) |
| Veřejný náhled requestu | `Request/GetPublicRequestInfo/…:68` |

Pro tyto je nutné logovat i **neúspěšné pokusy** (neplatný / expirovaný / už spotřebovaný token) — `PublicAction/ResourceRequestActionTokenResolver.cs:23-29` je vrací uniformně jako 404, takže bez auditu je enumerace tokenů neviditelná. Jediným kontrolním bodem prefixu je `Gateway/Configuration/PublicEndpointsOptions.cs:34`.

### P1 — Gateway / RBAC administrace

| Operace | Místo | Poznámka |
|---|---|---|
| Přihlášení do role-management konzole | `Gateway/Startup/RoleManagementAuthMiddleware.cs:31` | Sdílený statický secret v `?t=` query (`:78`), non-constant-time porovnání (`:89`), po úspěchu razí cookie s natvrdo `Admin` rolí (`:107`) |
| CRUD rolí / oprávnění / endpoint-mapping | `Gateway/Features/RoleManagement/Services/RoleManagementService.cs` (kontrakt `IRoleManagementService.cs:17-170`) | Mění efektivní autorizaci celé platformy |
| Import / export RBAC | tamtéž | |
| RBAC seed rekonciliace při startu | `Gateway/Features/RoleManagement/Services/RbacSeedHostedService.cs:12` | Běží při každém startu, aditivně (`:39`); výjimky se polykají s warningem (`:58-61`) |
| Selhání autorizace (403) | `Gateway/Startup/AuthorizationHelpers.cs:36-38` | Nenamapovaná route = fail-closed 403; bez auditu se nepozná útok od chybějícího mappingu |
| Fail-open větve | `AuthorizationHelpers.cs:21-24`, `:90-93` | Chybějící permission service / prázdný seznam šablon = průchod. Musí být auditováno, ne jen zalogováno |

### P2 — Doménové zápisy (compliance / dohledatelnost)

Provider: `CreateProvider/…:98`, `UpdateProvider/…:107`, `DeactivateProvider/…:50`, `ReactivateProvider/…:52`, `AddLocation/…:99`, `UpdateLocation/…:71`, `RemoveLocation/…:53`, `src/Modules/Core/Features/Provider/ResourceAssignment/AssignResourceEndpoint.cs:166`, `UpdateAssignmentEndpoint.cs:112`, `RemoveAssignmentEndpoint.cs:74`.

Resource: `CreateResourceOnLocation/…:260` (202, startuje workflow), `UpdateResource/…:95`, `DeleteResource/…:57`, `PublishResource/…:57`.

Projekt a vazby: `CreateProject/…:199` (+ udělení ownera), `UpdateProject/…:131`, `CreateProjectTag/…:74`, `AssignProjectTag/…:60`, `UnassignProjectTag/…:52`, `AssignProjectResource/…:135`, `UnassignProjectResource/…:124`, `ProjectTag/DeleteProjectTag/…:56` (admin-only).

ResourceRequest: `CreateResourceRequest/…:238` (odesílá e-mail), `CancelResourceRequest/…:111`, `ResendApprovalEmail/…:91` (**regeneruje PI action link a invaliduje předchozí** — bezpečnostně relevantní, spíš P1).

Request: `CreateRequest/…:137` (odesílá e-mail).

Uživatel: `UpdateUserPreferences/…:50`; implicitní provisioning volajícího přes `UserInfo/…/CallerProvisioning.cs` (vzniká `User` řádek na GET cestě — vznik identity si zaslouží záznam).

### P2 — Provozní / infrastruktura

| Operace | Místo |
|---|---|
| Flush celé v2 output cache | `Cache/ResetAllCaches/ResetAllCachesEndpoint.cs:37` (admin-only) |
| Přepnutí read-only režimu a blokované požadavky | `src/Core/API/Middleware/FeatureFlagMiddleware.cs:21,45,67` — dnes jen `ILogger`, komentář to nazývá "audit trail" |
| Přepnutí feature flagu `V2Api` | `Core/Composition/EndpointsExtensions.MapCoreEndpoints` — kill-switch celé v2 |
| Vydání interního S2S tokenu / rotace klíčů | `Gateway/Features/InternalToken/TokenService.cs`, `KeyService.cs` |
| Použití API klíče (včetně backup klíče) | `Gateway/ApiKeyAuthenticationHandler.cs:81-107`, `:399-413` |
| Dev impersonace | `Gateway/Authentication/DevOverrideAuthenticationHandler.cs:25` — bearer string **je** `sub` (`:56`); jen Development, ale kdyby se to zapnulo jinde, bez auditu se to nepozná |

### P3 — Čtení citlivých dat

Obvykle se neauditují, ale tyto stojí za zvážení: `GetStatistics/…:77` (cross-aggregate report, dnes bez admin/scope kontroly v handleru), `GetUsers/…:70` (admin, seznam všech uživatelů), `GetProjectUsers/…:54`.

## 5. Otevřené otázky před implementací

1. **Kam se píše.** immudb je dnes dev-only kontejner mimo manifest — přijetí ADR-0004 je změna nasazení, ne jen kódu. Alternativa: append-only SQL tabulka + periodický hash-chain. Rozhodnout dřív než cokoliv jiného.
2. **Jak se dostane aktér do domény.** Buď obohatit outbox zápis o `Caller` z HTTP scope (ambient `IAuditContext`), nebo audit emitovat v use-case vrstvě, kde `Caller` je po ruce. Druhé je jednodušší, ale rozptýlené přes 41 endpointů.
3. **Události vs. explicitní záznam.** Události nepokrývají členství a role (§3) a většina je best-effort. Buď doplnit události + převést na durable, nebo audit řešit nezávisle na doménových událostech.
4. **Neúspěchy.** Autorizační selhání (403), neplatné tokeny a rate-limit odmítnutí jsou u anonymních cest to nejcennější. Ty přes doménové události nikdy neprojdou — potřebují záchyt v Gateway/middleware.
5. **Retence a ověřování.** ADR-0004 `:43-46` obojí nechává otevřené.
6. **Osobní údaje.** Záznamy budou obsahovat e-maily a IdP identifikátory; retenční politika se musí potkat s GDPR (audit je typicky výjimka, ale je to potřeba napsat).

## 6. Vedlejší nálezy (mimo zadání, hlásím)

Při mapování vyplavaly nálezy, které nesouvisí s auditem, ale stojí za samostatné issue:

1. `Gateway/Features/RoleManagement/RoleManagementFeatureExtensions.cs:87` — `/api/role-management/rbac/pdf` nemá `.RequireAuthorization(RoleManagementAccessPolicyName)`, na rozdíl od sourozenců na `:77` a `:82`.
2. `AuthorizationHelpers.cs:21-24` a `:90-93` — dvě fail-open větve.
3. `RoleManagementAuthMiddleware.cs:78-89` — sdílený secret v query stringu, non-constant-time porovnání.
4. `Gateway/Features/Authorization/PermissionMatcher.cs:170` — `Console.WriteLine` zkompilovaných patternů při každém cache missu v produkci.
5. `Core/Shared/Auth/Caller.cs:62` — `sub.Split('@')[0]` slučuje dvě různé IdP identity na jeden `IdpId`.
6. `ServiceDefaults/Extensions.cs:179` — mrtvá proměnná `immuGwUrl`, chybí health check dokumentovaný na `:163`.
7. `UserPermissionsCache.cs:36-40` — odebraná role zůstává účinná až 30 s.
8. Zápisy členství jsou gated jen na "jakákoliv role v projektu" (`AssignProjectUserEndpoint.cs:41`), zatímco změna role vynucuje hierarchii — ověřit, zda je ta asymetrie záměr.

## 7. Coverage (T-208, 2026-09-24)

Sections 1 to 6 are the record of 2026-08-12 and stay as written. Their starting point is out of date: since ADR-0088
and ADR-0096 the audit log exists, and ADR-0109 extends it to every P1 and P2 row of §4. This section maps each row to
the audit record that now covers it.

- **Outcomes.** Core endpoints write Success, and Denied only when an authorization check refuses the caller (a masked
  404 of a scope check, an explicit 403, an unresolvable or spent anonymous token). 400, not-found 404, 409 and 422 are
  not audited. Failure is written only by callerless background work.
- **Mode.** Fail-closed: the write is awaited as the handler's last statement and an exception propagates. Fail-open:
  the write goes through `AuditBestEffortAsync`, which swallows a sink exception and logs one Warning.
- **Pin.** `Tests/Modules/Core/Core.UnitTests/Audit/AuditCoverageTests.cs` fails when a Core handler below loses its
  `IAuditLogger<>`. The gateway and host sites are pinned by their own tests.

### P1: security and access

| Operation | Site | Action | Outcomes | Mode |
|---|---|---|---|---|
| Change project role | `ChangeProjectUserRoleEndpoint` | `project.user_role.changed` | Success, Denied | fail-closed |
| Assign user to project | `AssignProjectUserEndpoint` | `project.user.assigned` | Success, Denied | fail-closed |
| Unassign user from project | `UnassignProjectUserEndpoint` | `project.user.unassigned` | Success, Denied | fail-closed |
| Promote to ProviderAdmin | `PromoteToProviderAdminEndpoint` | `user.provider_admin.granted` | Success, Denied | fail-closed |
| Reset to platform user | `ResetProviderAdminEndpoint` | `user.provider_admin.revoked` | Success, Denied | fail-closed |
| Delete project | `DeleteProjectEndpoint` | `project.self.deleted` | Success, Denied | fail-closed |
| Accept onboarding request | `AcceptOnboardingRequestEndpoint` | `onboarding_request.self.accepted` | Success, Denied | fail-closed |
| Reject onboarding request | `RejectOnboardingRequestEndpoint` | `onboarding_request.self.rejected` | Success, Denied | fail-closed |
| Accept request | `AcceptRequestEndpoint` | `request.self.accepted` | Success, Denied | fail-closed |
| Reject request | `RejectRequestEndpoint` | `request.self.rejected` | Success, Denied | fail-closed |
| Resend approval e-mail (listed under P2, "spíš P1") | `ResendApprovalEmailEndpoint` | `resource_request.approval_link.reissued` | Success, Denied | fail-closed |

### P1: anonymous token-authorized paths

The actor is `anonymous` with actor type `action-link`, and the token appears only as its SHA-256 hash. Denied covers
an invalid, expired or spent token, so token enumeration is visible.

| Operation | Site | Action | Outcomes | Mode |
|---|---|---|---|---|
| Settle a resource request by PI link | `HandleResourceRequestActionEndpoint` | `resource_request.action.settled` | Success, Denied | fail-closed |
| View a resource request action | `GetResourceRequestActionEndpoint` | `resource_request.action.viewed` | Success, Denied | fail-closed |
| Use a self-onboarding action link | `HandleActionLinkEndpoint` | `request.action_link.settled` | Success, Denied | fail-closed |
| Public request view | `GetPublicRequestInfoEndpoint` | `request.action_link.viewed` | Success, Denied | fail-closed |

### P1: gateway and RBAC administration

| Operation | Site | Action | Outcomes | Mode |
|---|---|---|---|---|
| Console sign-in | `RoleManagementAuthMiddleware` (shared access token, ADR-0110) | `gateway.role_management.signed_in` | Success, Denied (wrong token) | fail-closed |
| Console sign-out | `RoleManagementSignOutApi` | `gateway.role_management.signed_out` | Success | fail-open |
| CRUD of roles, permissions and endpoint mappings | `AuditingRoleManagementService` | `gateway.rbac_role.created`, `gateway.rbac_role.updated`, `gateway.rbac_permission.created`, `gateway.rbac_permission.deleted`, `gateway.endpoint_mapping.created`, `gateway.endpoint_mapping.updated`, `gateway.endpoint_mapping.deleted` | Success | fail-closed |
| RBAC import and export | `AuditingRoleManagementService` | `gateway.rbac.imported`, `gateway.rbac.exported` | Success | fail-closed |
| RBAC seed reconciliation | `RbacSeedHostedService` | `gateway.rbac.seeded` | Success, Failure | fail-open (ADR-0109) |
| Authorization failure (403) | `AuthorizationHelpers` | `gateway.authorization.denied` | Denied, one reason per branch | fail-closed |
| Fail-open: missing permission service | `AuthorizationHelpers` | `gateway.authorization.bypassed` | Success, reason `gateway.permission_service_missing` | fail-closed |
| Fail-open: empty permission-template list | `AuthorizationHelpers` | not audited, deliberately (ADR-0109) | | |

### P2: domain writes

Every row is fail-open.

| Operation | Site | Action | Outcomes |
|---|---|---|---|
| Create provider | `CreateProviderEndpoint` | `provider.self.created` | Success, Denied |
| Update provider | `UpdateProviderEndpoint` | `provider.self.updated` | Success, Denied |
| Deactivate provider | `DeactivateProviderEndpoint` | `provider.self.deactivated` | Success, Denied |
| Reactivate provider | `ReactivateProviderEndpoint` | `provider.self.reactivated` | Success, Denied |
| Add location | `AddLocationEndpoint` | `provider.location.added` | Success, Denied |
| Update location | `UpdateLocationEndpoint` | `provider.location.updated` | Success, Denied |
| Remove location | `RemoveLocationEndpoint` | `provider.location.removed` | Success, Denied |
| Assign resource | `AssignResourceEndpoint` | `provider.resource_assignment.created` | Success, Denied |
| Update assignment | `UpdateAssignmentEndpoint` | `provider.resource_assignment.updated` | Success, Denied |
| Remove assignment | `RemoveAssignmentEndpoint` | `provider.resource_assignment.removed` | Success, Denied |
| Create resource on location | `CreateResourceOnLocationEndpoint` | `resource.creation.requested` | Success, Denied |
| Update resource | `UpdateResourceEndpoint` | `resource.self.updated` | Success, Denied |
| Delete resource | `DeleteResourceEndpoint` | `resource.self.deleted` | Success, Denied |
| Publish resource | `PublishResourceEndpoint` | `resource.self.published` | Success, Denied |
| Create project | `CreateProjectEndpoint` | `project.self.created` | Success |
| Update project | `UpdateProjectEndpoint` | `project.self.updated` | Success, Denied |
| Create project tag | `CreateProjectTagEndpoint` | `project.tag.created` | Success, Denied |
| Assign project tag | `AssignProjectTagEndpoint` | `project.tag.assigned` | Success, Denied |
| Unassign project tag | `UnassignProjectTagEndpoint` | `project.tag.unassigned` | Success, Denied |
| Assign project resource | `AssignProjectResourceEndpoint` | `project.resource.assigned` | Success, Denied |
| Unassign project resource | `UnassignProjectResourceEndpoint` | `project.resource.unassigned` | Success, Denied |
| Delete project tag | `DeleteProjectTagEndpoint` | `project_tag.self.deleted` | Success, Denied |
| Create resource request | `CreateResourceRequestEndpoint` | `resource_request.self.created` | Success, Denied |
| Cancel resource request | `CancelResourceRequestEndpoint` | `resource_request.self.cancelled` | Success, Denied |
| Create request | `CreateRequestEndpoint` | `request.self.created` | Success |
| Update user preferences | `UpdateUserPreferencesEndpoint` | `user.preferences.updated` | Success |
| Implicit caller provisioning | `CallerProvisioning` | `user.self.provisioned` | Success (only a real insert), Denied (deactivated user) |

### P2: operational and infrastructure

| Operation | Site | Action | Outcomes | Mode |
|---|---|---|---|---|
| Flush the v2 output cache | `ResetAllCachesEndpoint` | `cache.output.flushed` | Success, Denied | fail-open |
| Requests blocked by a feature flag | `FeatureFlagMiddleware` | `feature_flag.request.blocked`, with the route pattern and flag name, never the path | Denied | fail-open |
| Read-only mode toggle | none: a configuration value (`HostInfrastructureExtensions.cs:82`), no code flips it at runtime | no toggle to audit; the requests it blocks are recorded as `feature_flag.request.blocked` | | |
| `V2Api` kill-switch toggle | none: read from configuration (`src/Core/API/Program.cs:152`) | no toggle to audit; the requests it blocks are recorded as `feature_flag.request.blocked` | | |
| Signing-key generation and rotation | `KeyService` | `gateway.signing_key.generated`, `gateway.signing_key.rotated` | Success; rotation also Failure | fail-open |
| Internal S2S token issuance | `InternalTokenProvider` | not audited per token, deliberately (ADR-0109) | | |
| API-key authentication | `ApiKeyAuthenticationHandler` | `gateway.api_key.authenticated` | Success (backup key only), Denied (missing, lookup timed out, invalid) | fail-open |
| API-key authentication with the primary key | `ApiKeyAuthenticationHandler` | not audited, deliberately (ADR-0109) | | |
| Dev impersonation | `DevOverrideAuthenticationHandler` | `gateway.dev_override.authenticated` | Success | fail-open |
