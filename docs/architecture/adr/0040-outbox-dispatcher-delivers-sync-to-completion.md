# ADR 0040 - The outbox dispatcher delivers the HEAppE sync to completion (mark-done on success only)

- Date: 2026-06-29
- Status: accepted
- Refines: [ADR-0028](0028-transactional-outbox-for-domain-events.md) (transactional outbox — establishes the table + at-least-once contract; left the *delivery boundary* unspecified).
- Relates to: [ADR-0016](0016-resource-assignment-writes-through-provider-aggregate.md) (`ResourceAssignmentChangedEvent`), F5 (`ProjectResourceAssignmentChangedEvent`), [ADR-0017](0017-ddi-service-api-as-separate-module.md) (F6's other half).

## Context

ADR-0028 hardens **one** hop: it writes the domain event into an `Outbox` table in the same
transaction as the aggregate change. But the HEAppE resource sync today flows through **two**
in-memory, at-most-once hops, not one:

```
DB COMMIT → DomainEventChannel            ← HOP 1 (ADR-0028 hardens this)
          → ResourceSyncHandler / ProjectResourceSyncHandler
          → IResourceSyncTrigger.Enqueue → HEAppE WorkItem channel   ← HOP 2 (still lossy)
          → ChannelWorker → IProjectCommandTemplateSyncService.SyncCommandTemplatesForResource(...)
```

If the outbox dispatcher merely re-enqueues onto the in-memory `WorkItem` channel and then marks
the outbox row dispatched, the loss window has only **moved from hop 1 to hop 2** — a crash between
"row marked done" and "ChannelWorker drains the item" still silently drops a committed sync intent.
The whole point of ADR-0028 (eliminate silent loss of an owed HEAppE sync) would be defeated.

A grilling session (2026-06-29) confirmed the enabling fact: a **synchronous, awaitable,
DI-resolvable** sync entrypoint already exists and is already idempotent:
`IProjectCommandTemplateSyncService.SyncCommandTemplatesForResource(projectShortName,
resourceName, ct) → ValidationResult<bool>` (Heappe module, registered scoped). It returns a
Result rather than throwing, so success/failure is directly inspectable.

## Decision

The outbox dispatcher **performs the HEAppE sync to completion** and marks the outbox row
`Dispatched` **only when the sync returns success**:

1. The dispatcher polls undispatched `Outbox` rows, deserializes each to its `IDomainEvent`, and
   re-runs the matching `IDomainEventHandler<T>` **inside a fresh DI scope**. The handler's
   fan-out logic (e.g. `ResourceSyncHandler` querying `GetProjectsUsingResourceAsync` → N project
   syncs) stays in **one place** — the dispatcher does not duplicate it (this is why the outbox
   stores the **raw event**, not pre-expanded work items).
2. For F6, the durable handlers call `IProjectCommandTemplateSyncService.SyncCommandTemplatesForResource`
   **directly and await it**, instead of the fire-and-forget `IResourceSyncTrigger.Enqueue`. The
   in-memory `WorkItem` channel is **bypassed** for these two events — not rebuilt.
3. `ValidationResult<bool>.IsSuccess` → mark the row `Dispatched` (stamp `DispatchedAt`).
   Failure or exception → leave the row undispatched, increment `Attempts`, record `Error`, and
   **retry with backoff** on the next poll. Failures never block the business write (already
   committed).
4. `Attempts` over a threshold → stop retrying and emit a **dead-letter / stuck-row** signal for
   the operator (alert + observable outbox depth). The periodic reconciliation sweep (ADR-0028)
   remains a backstop, not the primary path.
5. **Delivery sits behind a swappable port** (`IOutboxDispatchTarget`): the dispatcher owns polling,
   marking, retry and dead-lettering; the *target* owns what "deliver" means. The default
   `InProcessOutboxDispatchTarget` deserializes the row and runs the `IDomainEventHandler<T>` handlers
   in-process (point 1). Should durable consumers multiply — the trigger ADR-0028 names for revisiting a
   broker — a `BrokerOutboxDispatchTarget` that publishes to e.g. RabbitMQ is a one-line registration
   swap; the outbox capture, table, and loop are unchanged. The outbox stays the broker-agnostic message
   store; only the target changes.

**Durable-vs-best-effort selection:** an event opts into the outbox by a **marker interface**
(`IDurableEvent`, in `Core.Domain.Primitives`). `ResourceAssignmentChangedEvent` and
`ProjectResourceAssignmentChangedEvent` carry it; cache-eviction and other best-effort events do
not and stay on the in-memory `DomainEventChannel`. `UnitOfWork.SaveChangesAsync` routes
durable-marked events to the `Outbox` table (same transaction) and best-effort events to the
channel as today.

## Consequences

- **At-least-once all the way to HEAppE**, not just to the outbox table. The only "loss" that
  remains is a sync that HEAppE itself rejects, which is recorded as a failed/dead-lettered row —
  visible, not silent. This is the failure mode ADR-0028 set out to kill.
- **Idempotency is already satisfied.** `SyncCommandTemplatesForResource` is keyed by
  `(project, resource)` + upsert, so a redelivery (succeeded-but-not-marked) converges. No new
  idempotency work; document the key per ADR-0028.
- **The in-memory `WorkItem` channel is no longer on the durable path.** It may still serve any
  non-durable / manual-trigger callers; F6 does not delete it, only stops routing the two durable
  events through it. Removing it entirely is a separate cleanup, out of F6 scope.
- **Dispatcher runs the real side-effect synchronously**, so a slow/blocked HEAppE call slows the
  dispatcher, not a business request. Bounded concurrency + per-row timeout keep one stuck row
  from starving the rest. (The existing `ChannelWorker` ran 4 parallel consumers; the dispatcher
  should likewise process rows with bounded parallelism, not strictly serially.)
- **Additive schema only** (ADR-0028): one `Outbox` table; aggregate tables untouched; modular
  snapshot stays frozen (overlay freeze) — the table lands as one additive migration.
- **Single-instance dispatcher (operational constraint).** The poll loop selects due rows and stamps
  `DispatchedAt` without a per-row claim / concurrency token, so two dispatcher instances polling the
  same `Outbox` would double-dispatch (tolerable — the HEAppE sync is idempotent, ADR-0028) but waste
  work and race on `Attempts`. The current deployment runs **one** API instance, which is correct as-is.
  **Before any horizontal scale**, add a row-claim (e.g. `UPDATE … OUTPUT` lease or `rowversion`
  optimistic token) — call it out in the deployment runbook. Dead-lettered rows (`Attempts >=
  MaxAttempts`) are excluded by the query and surfaced via `LogCritical`; promoting them to a terminal
  status column for operability is a possible follow-up, not in F6 scope.

## Considered options

- **Re-enqueue onto the in-memory channel + mark done (rejected).** Smaller diff, but leaves hop-2
  loss intact — durability ends at the outbox, not at HEAppE. Only acceptable if reconciliation is
  promoted back to the *primary* mechanism, which ADR-0028 explicitly rejected.
- **Make the `WorkItem` channel itself durable (rejected).** That is "add a broker," which
  ADR-0028 ruled out for a single durable consumer.
- **Store pre-expanded work items in the outbox (rejected).** Moves the fan-out query into the
  write transaction and couples the outbox to the HEAppE sync shape instead of the domain fact;
  duplicates handler logic. Storing the raw event keeps one fan-out site and a truthful audit row.
