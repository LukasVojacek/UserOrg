# Consumer side: one handler per event type, direct DbContext, Heappe inside project upsert

**Status:** accepted (grilling session 2026-07-06) · **amended after independent review 2026-07-06 — the Amendments below override the Heappe placement in the original text** · relates to ADR-0053, ADR-0054, ADR-0056, ADR-0061

## Context

Today's write side is `WaldurEfpExporter` (1280 LOC) + `PuhuriExporter` (712 LOC) + ~900 LOC of
DbContext extension methods, with Heappe cluster initialization called from *inside* the export.
The two exporters duplicate orchestration; their real differences are data, not behaviour.

## Decision

The consumer in `Userorg.Adapter/Waldur` is a set of **small handlers, one per event type**
(`ProjectCreatedHandler`, `UserAssignedHandler`, …), **shared by all instances** — EFP/Puhuri
differences are resolved by payload content, never by parallel class hierarchies.

- Handlers write through **`IUserOrgDbContext` directly**. No new repository/port layer between
  handlers and the DbContext: the DbContext already is the unit-of-work abstraction, the tests
  run on the InMemory provider, and the hexagonal boundary that matters is the event port
  (ADR-0053), not an internal persistence indirection.
- Each dispatched event runs in its **own DI scope** (fresh DbContext), so concurrently running
  instance workers cannot share EF state.
- **Heappe project initialization stays inside the `ProjectCreated` handler**, called via the
  existing `IProjectCreationService`. If Heappe fails, the handler throws, the event is not
  acked, and the item replays next iteration (ADR-0056) — therefore Heappe initialization must be
  idempotent (verify during implementation; guard with an "already initialized" check if it is
  not).
- Handlers are idempotent upserts keyed by `(instance/service name, externalId)`, preserving
  today's `SyncFrom()` merge semantics for updates.

Splitting Heappe into a second independent consumer of `ProjectCreated` was considered and
deferred: with a synchronous in-process publisher it only creates ordering and partial-failure
questions; it becomes attractive once RabbitMQ gives each consumer its own ack.

## Consequences

- The ~2 900 LOC exporter/extension layer is replaced by a handful of handlers a junior can read
  top-to-bottom; shared upsert helpers live next to the handlers, not as DbContext extensions.
- Ported behaviours (validation rules, upsert keys, role assignment, location/allocation-unit
  enrichment) are carried over as **tests first**, then implementation.
- Partial handler success before an exception is tolerated by design (replay + idempotence).

## Amendments (independent review 2026-07-06)

### A1 — Heappe runs in the project **upsert** path (Created AND Updated), per HPC resource

**This overrides "Heappe inside ProjectCreated" above.** Today Heappe ensure runs for every
(resource, assignedLocation) on **every** iteration, and `ProjectCreationService` is
get-or-create **plus update** (it propagates e.g. extended end dates to the HEAppE project).
Restricting it to `ProjectCreated` would mean: a resource added to an existing project is never
initialized in HEAppE, and date extensions are never propagated. Therefore the
`ProjectUpsertHandler` (handling both `ProjectCreated` and `ProjectUpdated`) runs the Heappe
ensure per embedded HPC resource. Consequences:

- The Created/Updated distinction is **not load-bearing anywhere** — both route to the same
  upsert handler; the distinction remains only in the event type name (observability + future
  consumers). This is essential with a lossy in-memory map: a wrong Created-vs-Updated guess
  (e.g. first run against a populated DB) must not change behaviour.
- Idempotence is already answered by today's code: `EnsureProjectAsync` is
  get-or-create-or-update; replay is safe.
- Today an EFP resource whose provider lacks a `HeappeInstancesMap` entry **fails and is never
  persisted** (enrichment failure → rollback). The handler preserves this: enrichment failure →
  throw (no ack).

### A2 — The Heappe command-template sync trigger is part of the handler

Today, after each successful resource commit, the orchestrator enqueues
`WorkItem(projectShortName, resourceName)` into the Heappe module's background-sync channel
(command-template synchronization; adapter seam `IHeappeSynchronizationService` exists). The
`ProjectUpsertHandler` must trigger this per upserted HPC resource — dropping it would silently
kill template sync. Pin with a test.

### A3 — Atomicity: one event = one DI scope = one terminal `SaveChangesAsync`

The old code wrapped every ensure in an explicit transaction and **rolled the DB back when
Heappe failed** — the invariant is "resource row committed ⇒ Heappe ensure succeeded". The new
handler preserves that invariant without manual transactions:

1. mutate the EF change tracker (project + resources + locations + memberships as applicable),
2. call external ensures (Heappe) — the DB write has not happened yet,
3. single terminal `SaveChangesAsync()`.

Heappe failure → exception → nothing committed → retry next iteration. No
`BeginTransactionAsync` (the retrying execution strategy forbids bare manual transactions;
single-SaveChanges gives atomicity for free). This also changes granularity vs. today
(per-resource commits → all-or-nothing per project event) — accepted, and expected to show up in
the ADR-0059 comparison as explainable differences.

### A4 — Membership handlers: full row enumeration + dual soft-delete

Today's per-project user sync writes **five** row kinds per member; the per-user handlers must
cover all of them:

- `UserAssignedHandler`: User (upsert by `UserName`; profile fields written on create only —
  ADR-0055/A3), `AssignedUserToProject`, project-scoped `UserPermission` (role map
  PROJECT.MANAGER→ProjectManager, PROJECT.MEMBER→ProjectMember, PROJECT.ADMIN→ProjectOwner,
  unknown→ProjectMember + warning), system-level `PlatformUser` UserPermission, and
  `UserProjectAccess`.
- `UserRoleChangedHandler`: permission + access updates for the existing assignment.
- `UserRemovedHandler`: soft-deletes **both** the `AssignedUserToProject` row **and** the
  `UserProjectAccess` rows; no-op when already absent (ADR-0054/A2).

Known behaviour change (signed off): today the removal sweep compares the snapshot against the
**DB** and also removes manually-created assignments; the new diff removes only map-known
members, so a manually-added member is swept only after a restart re-seeds the map from the DB.

### A5 — Per-instance service account is bootstrapped by the consumer

Today `InitializeSystemExportUser` runs at consumer startup and the export **hard-fails** without
it (it is the `CreatedBy`/`AssignedBy` identity for projects, assignments, accesses). The new
consumer owns an idempotent `EnsureServiceAccountAsync(instanceIdentity)` — run at startup or
lazily on first event per instance — driven by per-instance identity options (service account
username/email; see plan §Options).

