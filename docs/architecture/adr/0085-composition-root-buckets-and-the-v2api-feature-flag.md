# Composition-root buckets (host-infra / V1 / V2) and the `V2Api` feature flag

## Context

`feat/refactor-core` must be mergeable into `main` with the modular (v2) Core dormant by
default and switchable on per deployment, without a rename or a rewrite of the 386-commit
branch's composition root. A grep of `docs/architecture/adr/` for `AddV2Core` or
"composition root" hits only ADR-0016 — the three-bucket shape of `Program.cs`
(`AddHostInfrastructure` / `AddV1Core` / `AddV2Core` / `AddModules`) was never itself
documented.

Two constraints shaped the design:

- `Microsoft.FeatureManagement`'s `IFeatureManager` is async and only resolvable after
  `WebApplicationBuilder.Build()`. Composition-time decisions (what to register, what to map)
  happen before that point, so they cannot use it. The repo already has a precedent for this:
  `HostInfrastructureExtensions.cs:81` reads `FeatureManagement:ReadOnlyMode` straight off
  `IConfiguration` to decide whether `AddEmailSenderClient()` runs.
- Not every v2-looking thing is actually inside `AddV2Core()`. The Ddi module's four
  `api/v2/*` paths are mapped by `AddModules()`/`MapModules()` (`DdiModuleExtensions.cs:51-65`),
  entirely outside this flag's reach.

## Decision

### 1. The ALWAYS vs FLAGGED split of `AddV2Core()`

`AddV2Core(bool isV2ApiEnabled)` (`src/Core/API/Composition/V2CoreExtensions.cs:54-80`) is split
into an unconditional bucket and a bucket gated on `isV2ApiEnabled`:

| Call | Verdict | Reason |
|---|---|---|
| `AddCoreDataAccess(configuration)` | **ALWAYS** | `CoreDatabaseMigrationStartupService` is the sole runtime owner of `dbo` migrations since ADR-0049 retired the legacy migrator; v1 tables live in that schema. |
| `AddForgeCore()` | **ALWAYS** | Legacy v1 injects `IRoleListService` / `IUserOrgRolesService` from Forge directly (e.g. `ProjectEndpoint.cs:403`, `UserInfoEndpoint.cs:58,117`); v1 must keep working with the flag off. |
| 3x `AddOpenApi("v2-*")` | **ALWAYS** | `ApiDocuments.Published` lists the three v2 documents whatever the flags say; an unregistered one crashes the docs UI. |
| bare `AddOutputCache()` | **ALWAYS**, in the host | `Program.cs:162` calls `app.UseOutputCache()` unconditionally, but only `AddCore()` registered the underlying service before this change. A parameterless `AddOutputCache()` in `HostInfrastructureExtensions.cs:77` keeps the middleware resolvable with the flag off. No policy names, no `CoreCachePolicy`, no `IOutputCacheInvalidator` are added here — see §5. |
| `Configure<WellKnownRoles>` | **ALWAYS**, relocated | See §6. |
| `AddCore(configuration)` | **FLAGGED** | v2 use-cases, the 4 named output-cache policies + `IOutputCacheInvalidator`, cache-eviction handlers, `IResourceProvisioningService`, the mail `HttpClient`, the resource-request flow. |
| `AddOutboxPersistence()` | **FLAGGED** | Self-contained: own `OutboxDbContext`, own schema, own hosted migrator and dispatcher. |
| `AddResourceRequestsPersistence()` | **FLAGGED** | Self-contained: own `ResourceRequestsDbContext`, own schema, own hosted migrator. |
| `AddWorkflowEngine()` | **FLAGGED** | Self-contained: own `WorkflowDbContext`, own schema, own hosted migrator and workers. See §7 for the ADR-0056 tension this creates. |
| `MapV2CoreEndpoints()` | **FLAGGED** (in `Program.cs:178-181`, not inside `AddV2Core` itself) | The `api/v2` Core route group. |

### 2. Two-layer `V2Api` flag contract

`FeatureFlags.V2Api` (`src/UserOrg.Shared/FeatureManagement/FeatureFlags.cs`) is one constant,
consumed at two different points with two different semantics:

1. **Composition gate — synchronous, startup-time, opt-in per deploy.**
   `Program.cs:121` reads `builder.Configuration.GetValue<bool>("FeatureManagement:V2Api")`
   once, before `builder.Build()`, and threads the result into both `AddV2Core(isV2ApiEnabled)`
   and the `MapV2CoreEndpoints()` call. This cannot use `IFeatureManager` — it doesn't exist yet
   at this point in the pipeline — so it follows the same pattern as `ReadOnlyMode` gating
   `AddEmailSenderClient()` in `HostInfrastructureExtensions.cs:81-85`. This layer is a one-way
   decision: routes and services are wired, or they never exist, for the process lifetime.
2. **Runtime kill-switch — `.EnabledWhen(FeatureFlags.V2Api)` on the `api/v2` group**
   (`EndpointsExtensions.MapCoreEndpoints`). This is the standard `Microsoft.FeatureManagement` per-request
   check via `FeatureFlagMiddleware`, applied at the `MapGroup` level so it materializes onto
   every endpoint the group maps. It only has an effect if the composition gate mapped the group
   in the first place — flipping the flag ON at runtime does nothing when it was off at startup,
   because the routes were never mapped.

### 3. Accepted limitations of the kill-switch layer

- `FeatureFlagMiddleware` runs after `app.UseAuthorization()` (`Program.cs:160,164`), so an
  unauthenticated probe of a killed v2 route gets 401, not 404. Immaterial here because the v2
  group is `RequireAuthorization()`.
- `FeatureFlagMiddleware` also runs after `app.UseOutputCache()` (`Program.cs:162,164`), so an
  already-cached v2 GET response keeps returning 200 until its cache entry expires or is evicted.
- The Docker profile loads `/opt/API/confs/appsettings.json` with `reloadOnChange: false`
  (`Program.cs:77`, the third `AddJsonFile` argument), so in that profile a runtime flip needs a
  process restart, or an env-var-backed configuration provider that does support reload. The
  kill-switch hot-flips only where a reloading provider actually supplies the flag.

These are named limitations, not defects: fixing them would mean moving `FeatureFlagMiddleware`
earlier in the pipeline, which would change behavior for the 67 existing `.DisabledWhen()`
endpoints that already rely on its current position.

### 4. What `V2Api` explicitly does NOT gate

- **The Ddi module's four `api/v2/*` paths** (`/api/v2/DataStaging/WorkerData`,
  `/api/v2/DataStaging/DataStagingLocations`, `/api/v2/Service/ProjectsInfo`,
  `/api/v2/Service/SyncDetails`), mapped by `AddModules()`/`MapModules()`
  (`DdiModuleExtensions.cs:51-65`), outside `AddV2Core()` entirely. They stay live with `V2Api`
  off.
- **The Gateway RBAC seed.** `RbacSeedHostedService` reconciles `roleManagmentRBAC.json`
  additively on every startup regardless of this flag (ADR-0060); the v2 endpoint-to-permission
  mappings seed either way. `PublicEndpointsOptions.cs:29-33` also allow-lists the v2 OpenAPI
  document paths and `/api/v2/Public` unconditionally.
- **Database migrations.** `src/Tools/DatabaseMigrations` (`MigrationRunner.cs:708-719`,
  `Program.cs:80-113`) registers `OutboxDbContext`, `ResourceRequestsDbContext`, and
  `WorkflowDbContext` unconditionally, and `src/UserOrg.AppHost/MigrationToolExtssensions.cs:43`
  wires `ConnectionStrings__WorkflowDBContext` into the `db-migrations-apply` executable
  unconditionally (Outbox and ResourceRequests reuse the same `UserOrgDBContext` connection
  string, so they need no separate wiring here). A `V2Api=false` deploy still creates all three
  v2-only schemas and still needs `ConnectionStrings__WorkflowDBContext` set.
- **PK generation.** ADR-0061's client-generated GUID keys for legacy write paths are unrelated
  to this flag and apply regardless of its state.

### 5. Output-cache registration split (host vs `Core`)

`HostInfrastructureExtensions.cs:77` registers a bare `AddOutputCache()` — a host concern, since
`Program.cs:162` calls `app.UseOutputCache()` unconditionally regardless of the flag. The four
named policies (`UserInfoCache`, `ProjectCache`, `CountryCache`, `StatisticsCache`) and
`IOutputCacheInvalidator` stay inside the flagged `AddCore()`
(`Core/Composition/ServiceCollectionExtensions.cs:39-46`). They cannot be relocated to the host:
`CoreCachePolicy` and `IOutputCacheInvalidator` are `internal` to `Core`, and ADR-0079 freezes
`Core`'s public surface to exactly `AddCore`/`MapCoreEndpoints`/`ApiAudience` plus two named
exceptions — moving cache-policy types across that boundary would violate the frozen surface
`Core.ArchitectureTests` enforces.

### 6. `WellKnownRoles` relocation

`services.Configure<WellKnownRoles>(configuration)` moved from `Core/Composition/
ServiceCollectionExtensions.cs` (the flagged `AddCore()`) into the always-registered
`AddCoreDataAccess` (`Core.DataAccess/ServiceCollectionExtensions.cs:41`). `DatabaseContext`
resolves `IOptions<WellKnownRoles>` for its role lookups
(`src/Modules/Core.DataAccess/Contexts/Repositories/DatabaseContext.UserRepository.cs:74-114`)
and is registered in the ALWAYS bucket; `IOptions<T>` degrades silently to type defaults instead
of throwing when nothing binds it. Leaving the binding in the flagged bucket would have
corrupted v1 role lookups with the flag off, with no startup failure to reveal it. The general
hazard this illustrates: any `IOptions<T>` binding must live in a bucket that is guaranteed to
run wherever its consumer runs — a conditionally-registered bucket is the wrong home for a
binding an always-registered consumer depends on.

### 7. The ADR-0056 exception

ADR-0056 places `AddWorkflowEngine()` in `Program.cs` specifically because engine persistence is
a **host** concern, not a `Core`-module concern. Gating that call behind `FeatureFlags.V2Api` — a
flag named for the v2 **API surface** — keeps ADR-0056's placement (still in the host, still
outside `AddCore`) but bends its framing: the call is no longer unconditional infrastructure
wiring, its lifecycle is now tied to whether the v2 API is enabled. Recorded here as a deliberate
exception rather than a silent deviation. The alternative — decoupling `AddWorkflowEngine()` from
`V2Api` and giving it its own flag — was rejected: nothing outside `AddV2Core()`'s FLAGGED bucket
currently needs the engine, and it is self-contained (own schema, own hosted migrator, own
workers), so tying its lifecycle to the same flag as its only consumers is consistent with how
`AddOutboxPersistence()` and `AddResourceRequestsPersistence()` are treated.

### 8. Where the flag is deliberately forced ON

The flag is off by default everywhere except the four sites below, and those four are the
**only** sanctioned places to force it on. Each is a closed environment that cannot represent a
production deploy — local developer runs, the E2E topology, and the build-time OpenAPI document
generator plus its in-process characterization. Anywhere else — any deployed profile, any
`appsettings.*.json`, any new test host — the flag stays configuration-driven and off by default;
a new forcing site is a change to this ADR, not a local convenience.

- `src/UserOrg.AppHost/Program.cs:75-78` — local `aspire run` developer experience only. No
  running test/CI topology depends on this value.
- `Tests/E2E/IntegrationTests/TestcontainersTopologyFixture.cs:161` — the Testcontainers-based
  E2E topology (boots API and Gateway as child processes), so ParityTests exercise v1 and v2
  side by side. This repo's E2E mechanism is Testcontainers, not Aspire; Aspire-hosted E2E tests
  were retired.
- `src/Core/API/Program.cs:58-73` — the `isOpenApiDocumentGeneration` in-memory configuration
  block (flag set at `:71`), so a plain `dotnet build` regenerates the three committed
  `API_v2-*.json` snapshots with real content instead of emptying them.
- `Tests/Core/Application.Tests/OpenApiSchemaReferenceUniquenessTests.cs:250-263` — the
  `OfflineApiFactory` test host, which sets the same flag through an environment variable for the
  identical reason: it characterizes that build-time generation path in-process, so it must
  reproduce that path's flag state.

## Consequences

- Flipping `FeatureManagement:V2Api` on and off is the entire mechanism for de-risking the merge
  of `feat/refactor-core` into `main`, but this flag is composition-and-routing scoped only. It
  does **not** by itself make the merge safe: the `20260630191024_ModularCleanup` migration's
  destructive DDL against the shared database (dropping `Configuration`, `ConfigurationValue`,
  `SystemModule`, tightening `UserPreference`/`UserPermission` columns, dropping `NewId()`
  defaults) runs regardless of this flag and is a separate, still-open risk. A dedicated
  deploy-runbook / schema-rollout verification task must close it before the merge to `main`
  actually happens — this ADR and the flag it documents cover the composition half only.
- A future v2-only persistence unit follows the same test as Outbox/ResourceRequests/Workflow:
  self-contained schema and workers ⇒ FLAGGED; consumed by v1 or by always-on infrastructure ⇒
  ALWAYS, with its `IOptions<T>` bindings (if any) placed per §6.
- The kill-switch's three accepted limitations (§3) are not to be "fixed" by moving
  `FeatureFlagMiddleware` — doing so changes behavior for every existing `.DisabledWhen()`
  endpoint, not just the v2 group.
