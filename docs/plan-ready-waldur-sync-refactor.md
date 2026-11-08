# Plan — Waldur integration rewrite: event-producing sync module (Waldur.Sync)

**Status:** approved via grilling session 2026-07-06; **amended after independent review
2026-07-06** (2 reviewers, findings in `.ai/reviews/waldur-sync-plan-review.md`)
**Target branch:** `feat/refactor-core` · **Plan branch:** `docs/waldur-sync-refactor-plan`
**ADRs:** [0053](architecture/adr/0053-waldur-sync-rewrite-event-producing-module.md) ·
[0054](architecture/adr/0054-waldur-fat-crud-event-contract.md) ·
[0055](architecture/adr/0055-waldur-sha-breadcrumb-change-detection.md) ·
[0056](architecture/adr/0056-waldur-sequential-worker-sync-ack.md) ·
[0057](architecture/adr/0057-waldur-consumer-handlers-direct-dbcontext.md) ·
[0058](architecture/adr/0058-waldur-logging-contract-stepuplogging.md) ·
[0059](architecture/adr/0059-waldur-cutover-parallel-run-acceptance.md) ·
[0060](architecture/adr/0060-waldur-sync-optional-container-split-http.md) *(optional)* ·
[0061](architecture/adr/0061-waldur-efp-storage-reconciliation.md)

> Several ADRs carry an **Amendments** section from the independent review. Where this plan and
> an original ADR paragraph disagree, the ADR's Amendments section wins — this plan is already
> written against the amended state.

## Goal

Replace the current Waldur integration (Waldur.EFP, Waldur.Puhuri, WaldurPlatform.Base,
`Userorg.Adapter/Waldur` exporters) with a **new, simple, long-term-maintainable implementation**
that keeps the functionality but restructures it as:

- **`Waldur.Sync`** — a self-contained pull module (per-instance sequential worker, SHA change
  detection) that **produces CRUD integration events**,
- **`Waldur.Contracts`** — the event types + `IWaldurEventPublisher` port (hexagonal seam),
- **consumer handlers** in `Userorg.Adapter/Waldur` that apply events to the UserOrg DB.

Still a monolith today, but the seam is exactly where RabbitMQ slots in later: swap the
in-process publisher for a queue publisher, host the handlers behind a queue consumer, lift
`Waldur.Sync` into its own container (optional Phase 6 / ADR-0060). Nothing else changes.

**Why this shape (junior summary):** the one boundary that matters is *"pull side produces
events, UserOrg side consumes them"*. Everything inside either side stays plain, sequential,
readable code. We deliberately do **not** rebuild the current channel pipeline — it solved a
throughput problem the 30-minute cadence doesn't have, at the cost of making "which item failed
and why" nearly unanswerable.

## Locked decisions

D1–D13 from the grilling; D14–D18 added by the independent review. Amended rows say so.

| # | Decision | ADR |
|---|---|---|
| D1 | SHA state is **in-memory only**; cold rebuild from UserOrg DB on start (DB → DTO → hash). No sync-state table, no SHA columns. | 0055 |
| D2 | **Fat events** (event-carried state transfer). Consumer never calls back. Future Waldur push = second trigger on the producer input, not a pull-back channel. | 0054 |
| D3 | **No delete from pull absence.** Absence = warning **on transition only** (amended), plus summary count. Project removal = `IsRemoved` payload flag (see D6). | 0054, 0058/A1 |
| D4 | **Sequential worker**, phase order preserved: Providers → Projects → Users. No channels/worker pools; **bounded** parallelism only for API-latency hot spots. Timer = delay **after** iteration completion. | 0056 |
| D5 | **Synchronous ack:** SHA map updated only after the handler succeeded. Retry = next iteration. **No DLQ.** Handlers **throw** on missing parents — never skip-and-ack. | 0056 |
| D6 | *(amended)* Taxonomy: `ProviderCreated/Updated`, `ProjectCreated/Updated` (resources embedded; **`IsRemoved` flag in payload replaces the separate `ProjectDeleted` event**), per-user `UserAssigned/UserRoleChanged/UserRemoved`, `StorageConfigurationChanged`. | 0054/A1, 0061 |
| D7 | `UserRemoved` only from a **successfully fetched complete member snapshot**; failed member fetch → no membership events for that project that iteration. New API client **fails loudly on partial pagination** (amended — old collector truncated silently). | 0054, 0056/A4 |
| D8 | *(amended)* Consumer = one handler per event type, shared EFP+Puhuri, direct `IUserOrgDbContext`. **Heappe ensure runs in the project upsert path (Created AND Updated), per HPC resource**, plus the Heappe command-template sync trigger. One event = one DI scope = one terminal `SaveChangesAsync` (Heappe called **before** save). | 0057/A1–A3 |
| D9 | Projects: `Waldur.Contracts` ← `Waldur.Sync` / consumer in `Userorg.Adapter/Waldur`. EFP/Puhuri = **config instances** (`Waldur:Instances`), 0–N enabled concurrently, own worker + own SHA map each. | 0053 |
| D10 | STOMP listener **not ported**. | 0053 |
| D11 | Logging via StepUpLogging levels: Debug = troubleshooting detail, Info = per-entity start/end, always-log Info = iteration summary, Warning/Error = structured per-item failure. Unchanged items log nothing. | 0058 |
| D12 | **Cutover in one branch**, old projects deleted; acceptance = parallel run old-vs-new with a **specified comparison methodology** (natural keys, exclusion + expected-difference lists, Heappe isolated, old side one instance at a time). | 0059 |
| D13 | SHA-256 over the **canonically serialized event payload**; hash covers **only pure functions of API data + config** (no clock-derived fields); normalization checklist is binding. | 0055/A1–A2 |
| D14 | **EFP storage reconciliation**: write-time association in the project upsert handler + config-hash-triggered `StorageConfigurationChanged` full reconcile; reconciliation-owned rows (`ServiceName = EfpCoreServiceName`) are **excluded from payload mapping/hashes**. | 0061 |
| D15 | **Membership identity = `UserName`** (Waldur user UUID is not stored in the DB and cannot seed a cold rebuild). `UserRemoved` carries `(Instance, ProjectExternalId, UserName)`. | 0054/A2 |
| D16 | Shared entities (users): hash **instance-owned fields only** (project, userName, role, access); user profile fields written on create only. | 0055/A3 |
| D17 | **Puhuri semantics defined by intent, not by today's broken code**: service name = instance name ("Puhuri-Waldur"), own service account, no storage reconciliation. Non-empty-DB cutover data check required. | 0059/A2–A3 |
| D18 | Per-instance **service account** is ensured by the consumer (idempotent, from instance identity options); it is the CreatedBy/AssignedBy identity for all sync writes. | 0057/A5 |

## Current state (verified by code exploration + independent review, 2026-07-06)

- `WaldurPlatform.Base` (~2 100 LOC): channel pipeline (`PipelineOrchestrator` 332 LOC), 3
  background services, worker pool 1–32, `SyncDeadLetterQueue`, STOMP listener, collector
  (`CollectorService`, `WaldurModels`), 241-LOC LogMessages.
- `Waldur.EFP` / `Waldur.Puhuri`: 75/74-LOC config wrappers over the same base.
- `Userorg.Adapter/Waldur` (~2 900 LOC): `WaldurEfpExporter` 1 280 LOC, `PuhuriExporter` 712 LOC,
  ~900 LOC DbContext extension methods, `WaldurExportOrchestrator` 398 LOC (transactions +
  domain logic mixed), 247-LOC LogMessages.
- **No SHA-style change detection** — every 30-min iteration upserts everything (`SyncFrom()`
  merge). But the old system is **not** purely pull-shaped; it also does, every iteration:
  - **Heappe ensure per resource** (get-or-create **and update**, incl. date propagation), with
    DB rollback when Heappe fails (`WaldurExportOrchestrator.cs:140–150`), plus a
    **command-template sync trigger** into the Heappe module's channel per committed resource;
  - **EFP storage reconciliation** (~600 LOC, `InitializeStorageLocations`): config-driven
    creation/deactivation of EFP-Core provider, shared/per-provider iRODS and StagingArea
    locations, and association sweep over **all** EFP resources in the DB;
  - **membership sweep against the DB** (removes members absent from the snapshot — including
    manually created ones), all users of a project in one all-or-nothing transaction;
  - provider **location derivation from per-customer offerings** (SSH-URL grouping,
    longest-common-prefix naming).
- Deletes today: project `is_removed` → `IsDeleted=true` via ordinary upsert — but removed
  projects usually have no eligible resources, get skipped, and the flag never lands (broken);
  membership removal and storage-location deactivation work as described above.
- **Puhuri is broken today** (D17): registered with the EFP registration + `Waldur` config
  section; writes EFP service names; user export can never succeed; old DI (unkeyed singletons)
  cannot run two instances concurrently.
- Config lives in **AppHost env wiring**, not appsettings (`ProjectConfigurationExtensions.cs`;
  `"N"`-format GUID dictionary keys); `StompBusAddress/Authentication` are `[Required]`;
  production values outside the repo. Puhuri has no wiring at all.
- **`UserOrg.ServiceDefaults` references `Waldur.EFP` at compile time** (options, enabled check,
  metrics/instrumentation, ActivitySource) — deletion breaks the build unless rewired.
- Endpoints pulled: `/api/customers/`, `/api/projects/`, `/api/marketplace-resources/` (per
  project), `/api/marketplace-public-offerings/?customer_uuid=…` (per customer — feeds provider
  locations), `/api/marketplace-public-offerings/{uuid}/` (per distinct offering — **primary**
  source for resource offerings, today unbounded `Task.WhenAll`),
  `/api/projects/{uuid}/list_users/`.
- Tests: `Waldur.Tests` (16 files) + `Waldur.Adapter.Tests` (9 files), ~6 000 LOC. Behaviour
  pins to port: `FullPipelineFlowTests`, `WaldurExporterExportDataTests`,
  `AggregateDataValidator` + `EnsureValidator` + inline producer/consumer skip rules,
  `RateLimitHandlerTests`, `PollyPoliciesTests`, `RegistrationTests` (config-gated DI),
  `ProjectSyncSkipRegistryTests`, `WaldurExportProjectResourceTrackingTests`.

## Target architecture

```
                 Waldur API (per instance: EFP, Puhuri, …)
                          │  HTTP pull (interval) — future: push trigger joins here
                          ▼
┌─ Waldur.Sync ────────────────────────────────────────────────────────┐
│  per enabled instance (config Waldur:Instances):                     │
│    WaldurApiClient → payload mapping (normalization!) → SHA diff     │
│    → events  (sequential worker; SHA map in memory, cold rebuilt)    │
│    + storage-config hash → StorageConfigurationChanged (ADR-0061)    │
└──────────────────────────────┬───────────────────────────────────────┘
                               │ IWaldurEventPublisher (Waldur.Contracts)
                               ▼         ← the future RabbitMQ seam
┌─ Userorg.Adapter/Waldur (consumer) ──────────────────────────────────┐
│  InProcessWaldurEventPublisher → handler per event type              │
│  (DI scope per event, direct IUserOrgDbContext, single SaveChanges;  │
│   ProjectUpsertHandler: resources + storage assoc + Heappe ensure    │
│   + Heappe template-sync trigger; StorageReconciliationHandler;      │
│   service-account bootstrap)                                         │
└──────────────────────────────────────────────────────────────────────┘
```

### Iteration algorithm (one instance)

0. **Startup only:** cold rebuild of the SHA map via `IShaMapRebuilder` (DB → payloads → hash);
   then always publish one `StorageConfigurationChanged` (EFP-configured instances only).
1. **Storage config check:** hash the storage config; if ≠ map entry → publish
   `StorageConfigurationChanged`; SHA update only after ack (as any event).
2. **Providers:** fetch all customers; per customer fetch its public offerings; map to
   `ProviderPayload` (locations **derived** from offerings — ADR-0054/A3) → hash → compare →
   publish `ProviderCreated/Updated` or skip.
3. **Projects:** fetch all projects; per project fetch marketplace resources + per distinct
   offering the offering detail (bounded concurrency) → apply eligibility rules (ADR-0054/A4) →
   map to `ProjectPayload` (resources embedded, `IsRemoved` flag, reconciliation-owned rows
   never mapped) → hash → compare → publish `ProjectCreated/Updated`.
4. **Users:** per eligible project fetch `list_users` (complete snapshot or explicit failure —
   D7). On success: diff per `(projectExternalId, userName)` → `UserAssigned` /
   `UserRoleChanged` / `UserRemoved`. On failure: warning, skip membership for that project.
5. Every successful `PublishAsync` → write SHA entry. Every failure → structured error, no SHA
   write, continue with next item. Absence transitions → warning (ADR-0058/A1).
6. End of iteration: always-log summary; **sleep interval measured from completion** (0056/A2).

## Contracts (abstractions the junior codes against — signatures only)

The exact shapes below are the *intent*; adjust naming to repo conventions, do not add layers.

```csharp
// ── Waldur.Contracts ─────────────────────────────────────────────────
public abstract record WaldurIntegrationEvent
{
    public required string Instance { get; init; }        // "EFP", "Puhuri", …
    public required string ServiceName { get; init; }     // upsert-key discriminator, e.g. "EFP-Waldur"
}

public sealed record ProviderCreated(ProviderPayload Payload) : WaldurIntegrationEvent;
public sealed record ProviderUpdated(ProviderPayload Payload) : WaldurIntegrationEvent;
public sealed record ProjectCreated(ProjectPayload Payload)  : WaldurIntegrationEvent;
public sealed record ProjectUpdated(ProjectPayload Payload)  : WaldurIntegrationEvent;
public sealed record UserAssigned(ProjectUserPayload Payload)     : WaldurIntegrationEvent;
public sealed record UserRoleChanged(ProjectUserPayload Payload)  : WaldurIntegrationEvent;
public sealed record UserRemoved(string ProjectExternalId, string UserName) : WaldurIntegrationEvent;
public sealed record StorageConfigurationChanged(StorageConfigPayload Config) : WaldurIntegrationEvent;

// Fat payloads — fields enumerated in Phase 0 by reading exporter usage. Sketch:
public sealed record ProviderPayload(string ExternalId, string Name, /* …customer fields… */
    IReadOnlyList<ProviderLocationPayload> Locations);          // derived from offerings!
public sealed record ProjectPayload(string ExternalId, string Name, bool IsRemoved,
    /* …project fields… */ IReadOnlyList<ResourcePayload> Resources);
public sealed record ResourcePayload(/* identity: EffectiveId/BackendId + offering + dates
    + allocations + heappe/storage enrichment inputs */);
public sealed record ProjectUserPayload(string ProjectExternalId, string UserName,
    string Role, /* access flags; profile fields (email…) = create-only, NOT hashed */);

// The port (the RabbitMQ seam). In-process impl completes when the handler completed.
public interface IWaldurEventPublisher
{
    Task PublishAsync(WaldurIntegrationEvent evt, CancellationToken ct);
}

// One canonical hasher for both sides (API→DTO and DB→DTO). Normalization lives HERE.
public static class WaldurPayloadHasher
{
    public static string Hash(object payload); // canonical JSON → SHA-256 hex
}

// ── Waldur.Sync (internal seams) ─────────────────────────────────────
public interface IWaldurApiClient      // typed HttpClient; throws on partial pagination (D7)
{
    Task<IReadOnlyList<ApiCustomer>> GetCustomersAsync(CancellationToken ct);
    Task<IReadOnlyList<ApiPublicOffering>> GetCustomerOfferingsAsync(Guid customerUuid, CancellationToken ct);
    Task<IReadOnlyList<ApiProject>> GetProjectsAsync(CancellationToken ct);
    Task<IReadOnlyList<ApiMarketplaceResource>> GetProjectResourcesAsync(Guid projectUuid, CancellationToken ct);
    Task<ApiPublicOfferingDetail> GetOfferingDetailAsync(Guid offeringUuid, CancellationToken ct);
    Task<IReadOnlyList<ApiProjectUser>> GetProjectUsersAsync(Guid projectUuid, CancellationToken ct);
}

// Pure: (current payloads, map) → events + map mutations to apply on ack. No I/O.
public sealed class DiffEngine { /* per-entity Created/Updated/Unchanged + membership diff */ }

// Port used at startup; implemented adapter-side (Waldur.Sync must not see the DB).
public interface IShaMapRebuilder
{
    Task<IReadOnlyDictionary<ShaKey, string>> RebuildAsync(string instance, CancellationToken ct);
}
public readonly record struct ShaKey(string EntityType, string ExternalId, string? UserName = null);

// ── Userorg.Adapter/Waldur (consumer) ────────────────────────────────
public interface IWaldurEventHandler<TEvent> where TEvent : WaldurIntegrationEvent
{
    Task HandleAsync(TEvent evt, CancellationToken ct);   // throw = not acked; never skip-and-ack
}
// InProcessWaldurEventPublisher: resolve handler, CREATE DI SCOPE per event, await, done.
```

### Per-entity identity & upsert keys (verified against today's code — NOT uniform!)

| Entity | Upsert key today (KEEP) | SHA map key | Notes |
|---|---|---|---|
| Provider (customer) | `(ExternalId, ServiceName)` | `(Provider, externalId)` | Country + LocationType resolved from DB; missing → item fails (throw) |
| Project | `(ExternalId, ServiceName)` | `(Project, externalId)` | `RequireProjectByExternalIdAsync` today ignores ServiceName — latent bug, new lookups always include ServiceName |
| ProjectResource | **`(Name, ServiceName)`** — ExternalId is null; `Name = EffectiveId ?? BackendId` after grouping by `(EffectiveId, BackendId, Offering.CustomerId)` | inside project hash | grouping + Min/Max date aggregation must be reproducible from DB (ADR-0055/A2.9); incl. deucalion name-trim hack |
| AssignedLocation | `(ExternalId = Waldur resource uuid, ServiceName)` | inside project hash | reconciliation-owned rows (`EfpCoreServiceName`) excluded everywhere (D14) |
| Location | endpoint+service, fallback endpoint-only; ambiguity → failure | inside provider hash | derivation rules ADR-0054/A3 |
| User | **`UserName` only** (no instance scope; shared row) | `(Member, projectExternalId, userName)` | profile fields create-only, not hashed (D16) |

### Options schema (full surface — today's `BaseIntegrationOptions` + profile constants)

`Waldur:Instances` is an array; each element:

```
Name                      // "EFP" | "Puhuri" | future — also the log/OTel tag
Enabled
BaseUrl + 4 relative endpoint URIs
Auth: AuthorizationHeader, AuthorizationHeaderValuePrefix, EndpointApiKey
Timeout, RateLimitPerSecond, RateLimitBurst, RetryCount, RetryInitialBackoffMs, RetryMaxDelayMs
SyncIntervalMinutes
Identity: ServiceName ("EFP-Waldur"/"Puhuri-Waldur" — THE upsert discriminator),
          ServiceAccountUserName, ServiceAccountEmail, WaldurPIEmail, WaldurPreferredAuthType
HeappeInstancesMap        // base-level, required for HPC resources (uri+identifier per provider GUID)
AllocationUnitMap         // node-hours×1, core-hours→k-Hours×1000, GB→TB-hours×1000 (from WaldurEfpConstants)
Storage (EFP only): IrodsInstancesMap, StagingAreaInstancesMap, UseSharedIrodsForAllEntities,
          EfpCoreServiceName, allocation amounts
ServiceProjectDomainName
```

Startup validation fails fast on nonsense (enabled instance missing auth/identity/etc.).
Today's constants classes dissolve into this config; nothing per-platform remains in code.

## Phases

Each phase ends green (build + tests) and is independently reviewable. TDD throughout: port the
*behaviours* of the old tests, not the test code.

### Phase 0 — Contracts (`Waldur.Contracts`)

1. New classlib `src/Modules/Integrations/Waldur.Contracts` (**does not exist today** — no
   collision to resolve).
2. Event records + payloads per §Contracts. Enumerate payload fields by reading
   `WaldurEfpExporter`/`PuhuriExporter`/context-extension field usage (incl. EFP
   iRODS/staging inputs, Heappe enrichment inputs: offering `plugin_options` →
   `HEAppELocalBasePath`, provider→`HeappeUri/Identifier`).
3. `IWaldurEventPublisher` port.
4. `WaldurPayloadHasher` + canonical serializer implementing the **ADR-0055/A2 normalization
   checklist verbatim** (no clock reads; truncation-to-DB-limits in mapping; GUID `"D"`
   lowercase; UTC + second precision; null==empty collections, sorted; invariant decimals).
   Unit tests: property-order independence, collection-order normalization, culture invariance,
   known-vector stability, **over-length strings, null dates** (adversarial vectors).

**Why:** the contract assembly is the seam everything depends on; hashing lives beside the
payloads because "what the consumer sees" and "what we hash" must never diverge.
**Verify:** zero project references; hasher tests green.

### Phase 1 — Waldur API client + payload mapping (`Waldur.Sync`)

1. New classlib `src/Modules/Integrations/Waldur.Sync` (ref: `Waldur.Contracts` only).
2. `IWaldurApiClient` (typed `HttpClient`): 6 endpoints per §Current state; port **rate
   limiting** (`RateLimitHandler`) and Polly retry behaviours (both are test-pinned today);
   pagination **throws on any page failure** (D7 — the old collector's silent `yield break`
   truncation is a bug, not a behaviour to keep); **bounded** concurrency for per-project/
   per-offering fan-out (old code's unbounded `Task.WhenAll` is not ported).
3. Options per §Options schema, bound + validated at startup; instance registration
   `AddWaldurSync(IConfiguration)` — zero enabled instances → nothing registers.
4. Pure mapping `API models → payloads`, including: provider location derivation from offerings
   (ADR-0054/A3), resource grouping/naming (`EffectiveId ?? BackendId`, Min/Max dates, deucalion
   trim), eligibility rules (ADR-0054/A4), `IsRemoved` flag, all three validation layers of
   today (`AggregateDataValidator` + `EnsureValidator` + inline skips) re-expressed as mapping
   validation: invalid item → structured warning + skip, no event, no SHA.

**Why:** client + mapping are pure and testable without host or DB; validation moves to the
producer edge so consumers only ever see well-formed events.
**Verify:** mapping matrix tests (per-platform maps, derivation, eligibility, validator rules),
rate-limit + retry + pagination-failure tests.

### Phase 2 — SHA map + diff engine (`Waldur.Sync`)

1. `ShaBreadcrumbMap`: `Dictionary<ShaKey, string>` per instance worker (single-threaded access —
   the worker is sequential; no concurrency primitives needed).
2. `DiffEngine` (pure): payloads + map → events. Rules: unknown id → Created; hash differs →
   Updated; equal → nothing; membership per-user diff with `UserRemoved` only from complete
   snapshots (D7/D15); ineligible project → no events incl. membership (ADR-0054/A4);
   absence transitions reported (for ADR-0058/A1 logging).
3. `IShaMapRebuilder` port + storage-config hash slot (`(instance, StorageConfig, "config")`,
   never cold-rebuilt — ADR-0055/A4).

**Why:** the diff is the heart of the refactor; as a pure function it is exhaustively unit-testable.
**Verify:** full diff matrix incl.: removed project (flag flip → Updated, stable thereafter);
member missing from successful snapshot → UserRemoved; failed snapshot → no membership events +
untouched member SHAs; project absent → transition warning only, no event; storage-config change
→ StorageConfigurationChanged.

### Phase 3 — Sequential worker (`Waldur.Sync`)

1. `WaldurSyncWorker : BackgroundService`, one per enabled instance; iteration per §Iteration
   algorithm; try/catch per item; SHA write only after ack (D5); delay measured from iteration
   completion (0056/A2).
2. Startup: cold rebuild, then one unconditional `StorageConfigurationChanged` (EFP-configured
   instances).
3. Logging per ADR-0058 incl. amendments (~15 `LoggerMessage` definitions): per-entity Info
   start/end, per-item structured Warning/Error with `phase`
   (`fetch|map|diff|publish|handler|heappe`), transition-only absence warnings, always-log
   summary with `absent` and `blocked-by-parent` counts. OTel: iteration counters + duration
   histogram + map-size gauge; ActivitySource + Meter named for `Waldur.Sync` (registered in
   ServiceDefaults — Phase 5).

**Why:** the worker is deliberately boring — all cleverness lives in already-tested pure parts.
**Verify:** worker tests with fake client + fake publisher: ordering, per-item failure isolation,
SHA-only-after-ack, startup sequence, summary content, cancellation.

### Phase 4 — Consumer handlers (`Userorg.Adapter/Waldur`, new content)

1. `InProcessWaldurEventPublisher`: resolve `IWaldurEventHandler<TEvent>`, **new DI scope per
   event** (fresh DbContext), await completion. Both `*Created`/`*Updated` route to the same
   upsert handler (the distinction is not load-bearing — ADR-0057/A1).
2. `ProviderUpsertHandler`: customer + derived locations upsert (keys per §table); Country/
   LocationType resolution — missing → throw (retriable).
3. `ProjectUpsertHandler` (the big one — behaviour ported from `WaldurExportOrchestrator` +
   context extensions):
   a. project upsert (`SyncFrom` merge semantics; `IsRemoved` → soft delete, memberships left
      in place);
   b. resource upserts (keys per §table) + location/allocation-unit enrichment
      (`AllocationUnitMap`);
   c. storage association for this project's resources per current config (D14 write-time half);
   d. Heappe enrichment (missing `HeappeInstancesMap` entry → throw — today's rollback
      behaviour) + **Heappe ensure per HPC resource** via `IProjectCreationService`
      (Created AND Updated — ADR-0057/A1);
   e. Heappe **command-template sync trigger** per upserted resource via
      `IHeappeSynchronizationService` (ADR-0057/A2);
   f. order of operations: mutate tracker → external ensures → **single terminal
      `SaveChangesAsync`** (ADR-0057/A3 — preserves "committed ⇒ Heappe succeeded"; no manual
      transactions).
4. Membership handlers per ADR-0057/A4: `UserAssignedHandler` writes all **five** row kinds
   (User create-only profile, AssignedUserToProject, project UserPermission via role map with
   unknown→ProjectMember+warning, system `PlatformUser` permission, UserProjectAccess);
   `UserRoleChangedHandler`; `UserRemovedHandler` dual soft-delete (assignment **and** access),
   no-op when absent.
5. `StorageReconciliationHandler` for `StorageConfigurationChanged` (D14 full-reconcile half):
   EFP-Core provider + shared iRODS ensure/undelete, per-provider iRODS/staging ensure &
   **deactivate-on-config-removal**, association sweep over the instance's resources. Behaviour
   matrix tests per ADR-0061.
6. Service-account bootstrap `EnsureServiceAccountAsync` (D18) — startup or first-event-lazy.
7. `IShaMapRebuilder` implementation: DB → payloads (same shapes as Phase 1, **excluding**
   reconciliation-owned rows, **including** soft-deleted projects, membership entries per
   non-deleted assignment) → `WaldurPayloadHasher`.
8. **Round-trip hash test (the guardian of D1/D13):** API-shaped payload → handlers write DB →
   rebuilder reads DB → hashes equal. Vectors include over-length strings (truncation),
   null-date fallbacks, storage-reconciled projects (exclusion rule), removed projects.

**Verify:** handler tests on InMemory DbContext re-pinning `WaldurExporterExportDataTests` +
tracking-state behaviours; round-trip test; Heappe failure → nothing committed, no ack.

### Phase 5 — Wire-up + old-code removal

1. Register the module in `AddModules()`; delete the old `AddWaldurModuleIfEnabled` path.
2. **`UserOrg.ServiceDefaults`** (compile-time dependency! — `Extensions.cs:20-23, 67-116`):
   replace `Waldur.EFP` references with `Waldur.Sync` OTel registration (new ActivitySource/
   Meter names); update dashboards/alerts that consumed old metric names.
3. **AppHost:** rewrite `EnableWaldurSyncIntegration`/`WaldurSyncIntegrationPeriod` parameters +
   env wiring (`ProjectConfigurationExtensions.cs`) to the `Waldur:Instances` shape (mind
   `"N"`-format GUID dictionary keys); wire Puhuri explicitly for the first time; drop STOMP
   env vars (`[Required]` today — removing the options type removes the constraint).
4. **Deployment runbook item:** rename production env vars (`Waldur__*` →
   `Waldur__Instances__0__*` equivalents), drop STOMP vars, add Puhuri instance secrets.
5. **Non-empty-DB data check (D17/0059-A3):** count rows whose ServiceName mismatches their true
   instance (Puhuri-written rows carrying "EFP-Waldur"); if hits → one-time data fix agreed with
   the team before enabling.
6. **Acceptance run (D12, before deletion):** scripted parallel run per ADR-0059/A1 — old vs new,
   each on its own empty DB, same test Waldur, **Heappe stubbed/sandboxed**, old side one
   instance at a time; diff natural-key projections with exclusion + expected-difference lists.
   Keep the script in `Tools/` (reusable for the RabbitMQ move).
7. Delete `WaldurPlatform.Base`, `Waldur.EFP`, `Waldur.Puhuri`, old exporter/orchestrator/
   extension code, `Waldur.Tests`, `Waldur.Adapter.Tests`, `WebsocketStompClient` reference.
8. Full solution build + test sweep; grep for orphans (old options keys, DI registrations,
   metric names, `ISyncIterationDbChangeCounter` — Waldur-internal, safe to delete).

**Verify:** solution green; acceptance diff empty or fully explained; zero references to deleted
types; AppHost boots with 0, 1 and 2 enabled instances.

## Optional Phase 6 — container split over HTTP (decision deferred, ADR-0060)

We are not committed to keeping the pull side in the monolith. If/when we decide to detach it —
**without RabbitMQ** — the split is a composition change only. Core code (`Waldur.Contracts`,
`Waldur.Sync` internals, all handlers) stays byte-identical; the work is two adapter classes,
one host project and Aspire wiring (~1 day):

1. New host `Waldur.Sync.Host` (minimal worker host + ServiceDefaults/OTel), registers
   `AddWaldurSync(...)`; `Waldur:Instances` config moves here. Add as Aspire resource in
   `UserOrg.AppHost` with a service reference to userorg-api.
2. `HttpWaldurEventPublisher : IWaldurEventPublisher` — `POST /internal/waldur-sync/events`;
   the API dispatches to the same handlers and returns 200 **after** the handler completes.
   HTTP response = ack → D5 semantics hold unchanged. **No automatic retries on the POST**
   (ADR-0060/A1 — the pull loop is the retry, incl. timeouts); timeout sized above worst-case
   Heappe runtime.
3. `HttpShaMapRebuilder : IShaMapRebuilder` — startup
   `GET /internal/waldur-sync/state?instance=…` returns `(entityType, externalId, userName?,
   sha)`; hashes computed **API-side** with the shared `WaldurPayloadHasher` (one hash
   implementation; DB→DTO mapping stays with the DB owner; D13 round-trip guarantee intact).
4. Internal endpoints in `/internal/waldur-sync/*`: service-to-service only (API key or mTLS per
   platform convention), never public.
5. In the monolith deployment nothing changes — in-process publisher + direct rebuilder remain
   the default composition. The split is togglable per deployment, and reversible.

**Discipline adopted now (cheap in monolith, mandatory after split):** event payloads are
JSON-serializable and evolve additively only.

**Path to RabbitMQ later:** swap the publisher for a queue publisher, host handlers behind a
queue consumer (ack shifts per ADR-0056: SHA on broker confirm, consumer-side retry, idempotent
handlers absorb duplicates). The state endpoint stays HTTP.

## Second use case (future, explicitly out of scope now)

When Waldur can push events: add a second trigger in `Waldur.Sync` that enqueues a targeted
re-fetch of one entity through the **same** fetch→map→diff→publish path. Nothing downstream
changes. Do not build any of it now (D2, D10).

## Out of scope

- RabbitMQ itself (this refactor only creates the seam).
- Tombstone/absence-based deletion (contract already supports adding it later).
- STOMP/push trigger (above).
- Any change to what data UserOrg stores (beyond removed-project flags now actually landing —
  ADR-0054/A1, an accepted improvement).

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| DB→DTO vs API→DTO drift → spurious events after restart | Round-trip hash test (Phase 4.8) with adversarial vectors, in CI; ADR-0055/A2 checklist is binding. |
| Clock-derived field sneaks into a hash → perpetual Updated storm | ADR-0055/A1 rule + hasher test asserting payload hash stability across time. |
| Payload misses a field an exporter silently used | Phase 0 enumerates by reading exporter usage; acceptance diff (Phase 5.6) catches the rest. |
| Heappe regression (resource added to existing project) | D8 amended: ensure runs on upsert per resource; pinned by test. |
| Storage config change has no effect | D14: write-time association + config-hash-triggered reconcile; behaviour matrix tests. |
| Stale membership after restart | D15: `UserName`-keyed map is DB-rebuildable; rebuild spec ADR-0055/A4. |
| Puhuri on non-empty DB duplicates rows | Phase 5.5 data check before enable (0059/A3). |
| Permanently failing item loops every iteration | By design (no DLQ); repeating structured error + `blocked-by-parent` aggregation is the monitoring hook. |
| Two instances racing on shared reference data | Unique violation = retriable failure; optional retry-once (0056/A3); DI scope per event. |
| Acceptance diff drowns in nondeterminism | 0059/A1 methodology: natural keys, exclusion + expected-difference lists, Heappe isolation. |
