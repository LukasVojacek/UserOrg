# ADR-0054: F6 outbox hardening — observability, dead-letter surfacing, trigger retirement, row-claim deferral

- **Status:** Proposed
- **Date:** 2026-07-08
- **Deciders:** developer + implementation session `20260708T080255Z-f6-durable-outbox`
- **Related:** [ADR-0028](0028-transactional-outbox-for-domain-events.md),
  [ADR-0040](0040-outbox-dispatcher-delivers-sync-to-completion.md),
  [ADR-0041](0041-outbox-dedicated-schema-isolated-persistence-unit.md);
  issue #237; [outbox-flow.md](../outbox-flow.md)

## Context

The transactional outbox core is **already implemented and wired** at commit `e8b84efd`:
both HEAppE-sync events (`ResourceAssignmentChangedEvent`,
`ProjectResourceAssignmentChangedEvent`) implement `IDurableEvent`; `UnitOfWork`
persists them to the `outbox` schema in the aggregate transaction; a live
`OutboxDispatcherService` polls and delivers them to HEAppE with retry/backoff. The
`docs/ddd-migration/progress.md` F6 row ("HEAppE sync events not yet routed through it")
is **stale** — the routing is done.

What F6 in this session actually covers is **hardening and cleanup** of that existing
mechanism, not building the routing. A grill resolved the open scope questions.

## Decisions

### D1 — Observability: meters **and** tracing spans

Add an `OutboxTelemetry` static class (`static Meter` + `ActivitySource`, process-scoped,
matching the HEAppE/Waldur idiom) exposing:

- `outbox.messages.dispatched` (counter), `outbox.messages.failed` (counter),
  `outbox.messages.dead_lettered` (counter),
- `outbox.dispatch.duration` (histogram, ms),
- a tracing span around each `ProcessRowAsync` (tags: message id, type, attempts).

Register meter + source with OTEL via `AddMeter` / `AddSource`, wired from
`UserOrg.ServiceDefaults` consistent with the Waldur instrumentation-extension pattern.

*Rejected:* meters-only — a span per dispatch gives the dispatch→HEAppE causal link in
the Aspire trace view for the cost of a few lines; the codebase already traces the
HEAppE enqueue path, so parity is worth it.

### D2 — Dead-letter surfacing: **metric + structured log** (no query surface, no requeue)

At the existing `Attempts >= MaxAttempts` branch, increment
`outbox.messages.dead_lettered` alongside the existing `LogCritical` (message id / type /
error). No admin query endpoint and no requeue capability are built.

*Rejected:* a read-only dead-letter query endpoint and a manual-requeue write path — both
add read models / RBAC / write authz for a scenario (a genuinely dead HEAppE sync) that
is rare, single-instance, and adequately actioned from logs + alerting. Revisit if
dead-lettering proves frequent.

### D3 — Retire `IResourceSyncTrigger` (the old fire-and-forget path)

`IResourceSyncTrigger` + `ChannelResourceSyncTrigger` have **zero live callers** — the two
events now flow through the outbox to the awaited `IResourceSyncRunner`. Delete the port,
its adapter, and its DI registration; fix the two stale `<see cref="IResourceSyncTrigger"/>`
doc references. **Keep** `Channel<WorkItem>`, `ChannelWorker`, `WorkItemProcessor`, and
`HeappeSynchronizationService` — the channel has a **second, still-live producer**
(`HeappeSynchronizationService`, the legacy sync path).

This **supersedes** the note in ADR-0040 that "deleting the trigger is out of F6 scope":
with the two events cut over and no remaining callers, the trigger is dead code and its
removal is safe and in scope.

### D4 — Row-claim locking: **deferred**, seam documented

The dispatcher stays single-instance. No row-claim is built. Document the upgrade seam
(comment at the poll query naming `UPDLOCK`/`READPAST` or `UPDATE … OUTPUT`) so a future
horizontal-scale change has a clear, single place to land. Deployment runs one API
instance; a row-claim now is speculative (YAGNI).

### D5 — Test coverage

- **Unit tests** for `OutboxDispatcherService`: success → `DispatchedAt` stamped; failure
  → `Attempts++` / `LastAttemptAt` / `Error`; dead-letter at `MaxAttempts`; backoff gating
  (`IsDue`); batch order + cap; transient poll error survives. (No dispatcher test exists
  today — the biggest real gap.)
- **Real-DB integration test** (gated on `USERORG_TEST_DB`, `IntegrationDb.cs` pattern) for
  the cross-context same-transaction write: a durable event's aggregate row and outbox row
  commit atomically; a rollback leaves neither. The EF in-memory provider cannot exercise
  the shared-`DbConnection` + `UseTransaction` guarantee, so this needs a real DB.

### D6 — Documentation

Add [`docs/architecture/outbox-flow.md`](../outbox-flow.md) — a detailed flow reference
with component, write-path, dispatch-path, and message-lifecycle diagrams. Update the
stale F6 row in `docs/ddd-migration/progress.md` to reflect reality (core done; this
session = hardening).

## Consequences

- **Positive:** the existing outbox becomes observable (metrics + traces), its worst-case
  (dead-letter) is measurable, dead code is gone, and the transactional guarantee is
  pinned by a real-DB test. No behavioural change to the happy path.
- **Negative / accepted:** still single-instance (D4); dead-letter recovery stays manual
  (D2). Both are documented seams, not silent gaps.
- **Neutral:** `progress.md` and ADR-0040's scope note are corrected rather than left to
  drift.
