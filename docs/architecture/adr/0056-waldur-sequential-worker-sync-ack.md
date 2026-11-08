# Sequential sync worker with synchronous ack; retry is the next iteration

**Status:** accepted (grilling session 2026-07-06) · **amended after independent review 2026-07-06 — see Amendments** · relates to ADR-0053, ADR-0055

## Context

The current pipeline runs channels, drain phases, semaphores, a 1–32 worker pool and a dead-letter
queue for a job that fires every 30 minutes. Throughput is not the problem — comprehensibility
and failure attribution are: one item travels through three background services, so "which item
failed and why" is hard to answer. The channel stages effectively ran the phases sequentially
anyway.

A second, subtler problem is consistency between the SHA map (ADR-0055) and reality: if the map
is updated before the consumer actually processed the item and processing fails, the change is
lost forever (next pull sees "unchanged").

## Decision

**One sequential worker per enabled instance.** An iteration is a plain loop preserving today's
phase order — Providers → Projects (with resources) → project Users — with `try/catch` around
each item: fetch → build DTO → diff against the SHA map → publish events for changed items →
continue with the next item on failure. Parallelism is allowed only where Waldur API latency
demands it (e.g. bounded `Parallel.ForEachAsync` over per-project detail fetches), never as an
architectural layer. No channels, no worker pools, no drain phases.

**Synchronous ack.** `IWaldurEventPublisher.PublishAsync(evt)` in the in-process implementation
dispatches directly to the consumer handler and returns only after the handler (DB write, Heappe
init) completed. The SHA map entry is updated **only after `PublishAsync` succeeds**. On failure:

- the SHA entry is not written → the item is re-diffed and re-published on the next iteration;
- the failure is logged with full attribution (ADR-0058);
- the iteration continues with the next item.

**No dead-letter queue.** Retry is free from the pull loop itself; a permanently failing item
shows up as a repeating structured error with the same `externalId` — a monitoring concern, not a
poison-queue mechanism.

## Consequences

- Failure attribution is trivial: one stack, one item, one log scope.
- Consumers must be idempotent (they are — upsert semantics, ADR-0054), because a handler may
  succeed partially before throwing and the item will be replayed.
- On the future RabbitMQ move the ack semantics shift (SHA update on broker confirm; the consumer
  gets its own retry). Because consumers are idempotent from day one, the worst case there is
  duplicate delivery, which upserts absorb.
- The `SyncDeadLetterQueue`, channel-depth metrics and worker-pool configuration are deleted, not
  ported.

## Amendments (independent review 2026-07-06)

### A1 — Missing parent = ordinary retriable failure; handlers must throw, never skip-and-ack

A `ProjectCreated` whose provider failed earlier in the same iteration will fail in the handler
(lookup of the provider/location fails). That is the **designed** outcome — but it must be a
contract, not an accident: a handler that cannot resolve a referenced parent **throws** (→ no
ack, no SHA write, retry next iteration). It never logs-and-acks, because an acked event is gone
forever (the SHA map now believes the item was applied). One permanently broken parent cascades
into N child errors per iteration; that is accepted and belongs to the monitoring signal
(ADR-0058 notes the error-class).

### A2 — Timer model: delay measured from iteration completion

The per-instance worker sleeps `SyncIntervalMinutes` **after** an iteration finishes (not a
fixed-rate timer). In-process iteration overlap is thereby impossible by construction, even when
an iteration runs longer than the interval.

### A3 — Cross-instance races on shared reference data

Two instance workers can concurrently get-or-create shared reference rows (Country,
LocationType, ProjectDomainType, AllocationUnitType, User by `UserName`). A unique-constraint
violation (SQL 2601/2627) is treated as an ordinary retriable failure of that item (next
iteration wins by reading the existing row). The old code's dedicated unique-violation retry loop
is *not* ported as infrastructure; if a specific hot spot proves noisy, a single in-handler
retry-once-on-2601/2627 is the permitted fix.

### A4 — Fetch completeness is explicit (guards ADR-0054/D7 and D3)

The old `CollectorService` **silently truncates** on a mid-pagination failure (`yield break`), so
a caller cannot distinguish a complete list from a partial one — faithfully porting that would
break the "UserRemoved only from a complete snapshot" rule. The new API client must **throw (or
return an explicit failure) on any page error**; a partial list is never presented as a complete
result. Per-project detail fan-out (offering fetches) uses **bounded** concurrency (today's
unbounded `Task.WhenAll` is not ported).

