# EFP storage reconciliation: config-triggered reconcile event + write-time association

**Status:** accepted (independent review follow-up, 2026-07-06) · relates to ADR-0053, 0054, 0055, 0057

## Context

Independent review of the plan (BL2/B1) found ~600 LOC of live EFP behaviour with no home in the
pull→diff→event model: `WaldurEfpExporter.InitializeStorageLocations` runs at the end of **every**
iteration and (a) creates/soft-deletes the synthetic "EFP-Core" provider and the shared-iRODS
location, (b) ensures/deactivates per-provider iRODS and StagingArea `Location`s from
`IrodsInstancesMap`/`StagingAreaInstancesMap`, and (c) walks **all** EFP `ProjectResources` in the
DB attaching/deactivating storage `AssignedLocation`s (`UseSharedIrodsForAllEntities` switch,
allocation amounts from config). Puhuri's implementation is a no-op.

This is driven by **configuration + current DB state, not by pulled Waldur data** — a config
change produces no Waldur change, hence no CRUD event, hence (under the original plan) no effect,
ever. It cannot be expressed as `ProviderUpserted`/`ProjectUpserted` payload data alone.

A second hazard: the rows this reconciliation owns (`ServiceName = EfpCoreServiceName`) hang off
the same resources the SHA cold rebuild reads. If DB→DTO mapping included them, every restart
would produce hash mismatches and a permanent spurious-`Updated` storm (ADR-0055).

## Decision

Split today's monolithic reconcile into two mechanisms that fit the event model:

1. **Write-time association (per project).** Attaching/deactivating storage `AssignedLocation`s
   for a project's resources becomes part of the **project upsert handler** (ADR-0057): whenever
   a project event is processed, the handler also reconciles that project's storage locations
   against the instance's current storage config (carried in the event payload's instance
   context or resolved from consumer-side options — see plan §Contracts). New resources get their
   staging/iRODS associations at the moment they are written, not in a later sweep.

2. **Config-triggered full reconcile (per instance).** The worker computes a canonical hash of
   the instance's storage configuration (`IrodsInstancesMap`, `StagingAreaInstancesMap`,
   `UseSharedIrodsForAllEntities`, allocation amounts) and keeps it in the SHA map under key
   `(instance, EntityType.StorageConfig, "config")`. It publishes a
   **`StorageConfigurationChanged { Instance, StorageConfig }`** event:
   - on the **first iteration after startup** (config hash is never cold-rebuilt from the DB —
     re-running the reconcile is cheap and idempotent, so always fire once per process start), and
   - whenever the config hash differs from the map entry.
   The consumer-side **`StorageReconciliationHandler`** then performs the full sweep: ensure
   EFP-Core provider + shared-iRODS location, ensure/deactivate per-provider locations, walk all
   of the instance's resources in the DB and fix associations — including **deactivation** when a
   mapping disappeared from config. Ack semantics as any other event (ADR-0056): SHA (config
   hash) is written only after the handler succeeded.

3. **Hash-exclusion rule (guards ADR-0055).** `DB → ProjectPayload` mapping **excludes**
   reconciliation-owned rows: any `AssignedLocation`/`Location` with
   `ServiceName = EfpCoreServiceName` (and the EFP-Core provider itself) never enters a payload
   or a hash. The reconciliation subsystem is the sole owner of those rows.

Instances without storage config (Puhuri) get neither mechanism — no event, no handler work.

## Consequences

- Config changes now take effect within one iteration, with change detection (today the sweep
  runs unconditionally every iteration; the new design runs the full sweep only on startup and
  on config change — write-time association covers the steady state).
- The behaviour matrix to pin as tests (from today's code): shared-vs-per-provider iRODS
  (`UseSharedIrodsForAllEntities` true/false), staging-area ensure/deactivate on config
  add/remove, EFP-Core provider undelete, allocation amounts from config.
- `StorageConfigurationChanged` is an instance-scoped event like any other — it survives the
  RabbitMQ move and the ADR-0060 container split unchanged.
- The old end-of-iteration failure mode ("storage reconcile failure fails the whole iteration")
  is replaced by ordinary per-event failure: logged, retried next iteration (ADR-0056).
