# ADR 0028 - Transactional outbox for durable domain-event dispatch (HEAppE resource sync)

- Date: 2026-06-27
- Status: accepted (2026-06-27) — resolves the deferred "Outbox pro HEAppE resource sync" item in `next-phase-plan.md`.
- Relates to: ADR-0016 (resource-assignment writes; `ResourceAssignmentChangedEvent` → HEAppE sync via `IResourceSyncTrigger`), ADR-0019 (self-onboarding single transaction), ADR-0009 (inline mail). Prerequisite for F5/F6.

## Context

Domain events are dispatched **after** `SaveChanges` through an **in-memory `DomainEventChannel`** —
**at-most-once**. A crash or shutdown between the DB commit and the dispatch **loses a committed event**.
For `ResourceAssignmentChangedEvent` that means a resource assignment is persisted but **never synced to
HEAppE**, with no record that the sync is owed — a silent inconsistency of exactly the kind this refactor's
data audit keeps surfacing.

Not every event needs this: **mail is inline** (ADR-0009) and **cache eviction tolerates best-effort**. Only
events that drive an **external, must-happen side-effect** — today the HEAppE resource sync — need guaranteed
delivery.

## Decision

Events requiring guaranteed delivery use a **transactional outbox**:

1. The event is written to an **`Outbox` table in the SAME transaction** as the aggregate change. If the
   business transaction commits, the event is durably recorded (**at-least-once**); if it rolls back, so does
   the event. No window where the change is committed but the intent is lost.
2. A **separate dispatcher** (background service) polls undispatched outbox rows, delivers them (HEAppE sync),
   marks them done, and **retries with backoff** on failure. Failures don't block the business write.
3. **Consumers must be idempotent.** At-least-once means an event can be delivered more than once (retry after
   a delivery that succeeded but wasn't marked done). The HEAppE sync is keyed by the assignment identity so a
   repeat delivery converges to the same state.

This **supersedes the in-memory `DomainEventChannel` for durability-requiring events**. Best-effort events
(cache eviction) may stay on the in-memory path.

## Consequences

- **Additive schema:** one new `Outbox` table (id, type, payload, occurredAt, dispatchedAt?, attempts,
  error?). No change to aggregate tables — consistent with the overlay/additive constraint.
- **At-least-once + idempotent consumers** is the contract. The HEAppE sync (and any future durable consumer)
  must be safe to re-run; document the idempotency key per consumer.
- **Dispatcher operational concerns:** retry/backoff, a stuck-row / dead-letter signal (attempts over a
  threshold → alert), and observability of the outbox depth. A periodic **reconciliation sweep** stays useful
  as a backstop (find assignments whose sync is owed) but is **not** the primary mechanism.
- **Ordering is not guaranteed** unless explicitly enforced (per-aggregate sequence). HEAppE sync is
  convergent, so global ordering is not required.
- **Enforcement stays aggregate-first (ADR-0027 principle):** the aggregate raises the event; the outbox only
  guarantees the *delivery* of an already-decided fact — it is plumbing, not a place for business rules.

## Considered options

- **Reconciliation sweep as the primary mechanism (rejected).** A periodic job that finds un-synced
  assignments and re-triggers. Simpler, no same-transaction write, but duplicates the sync-decision logic, is
  only eventually consistent, and has no per-event audit trail. Kept as a **backstop**, not the primary path.
- **Keep at-most-once in-memory (rejected).** Cheapest, but accepts silent loss of a committed sync intent —
  the failure mode this refactor is actively eliminating elsewhere.
- **Full broker (e.g. a message bus) (rejected for now).** Over-engineered for one durable consumer; the
  in-process outbox + dispatcher covers HEAppE sync without new infrastructure. Revisit if durable consumers
  multiply.
