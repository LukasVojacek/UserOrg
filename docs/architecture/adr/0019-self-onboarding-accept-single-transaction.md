# ADR 0019 - Self-onboarding accept settles two aggregates in one transaction, no optimistic concurrency yet

- Date: 2026-06-11
- Status: accepted

## Decision

Accepting a self-onboarding request mutates **two aggregates in a single transaction**: it transitions the `Request` to `Accepted` (`Request.Accept()`) and grants the requester project membership (`Project.AssignUserWithRole(...)`), then the use-case calls `SaveChangesAsync` **once**. Both the authenticated accept endpoint and the anonymous action-link handle endpoint run this through the shared `SelfOnboardingAccept.ExecuteAsync` helper, so the request settlement and the membership grant commit atomically or not at all.

This deliberately trades the DDD "**one aggregate per transaction**" guideline for atomicity: settling the request without granting membership (or vice versa) would leave an observable inconsistency a user could act on, and there is no compensating action defined.

No **optimistic concurrency token** (`rowversion`) is configured on any aggregate. Concurrent retries are guarded by the domain state machine (a second accept of an already-`Accepted` request returns 409 via `InvalidStateTransition`) and, for membership, by the aggregate's in-memory "already assigned" guard plus the database. Two *simultaneous* accepts that both read `Pending` are therefore the residual risk; the relational uniqueness of the membership row is the backstop.

## Context

- The new modular core is an **overlay** on a legacy schema: the database and its migrations are owned by `src/Core/Infrastructure.DataAccess`, and `Core.DataAccess` reproduces that schema without emitting migrations (a probe `migrations add` must produce no table operations). Adding a `rowversion` column to `Project`/`Request`/`User` is a schema change that must go through a coordinated legacy migration, so it is out of reach of the overlay alone.
- Self-onboarding is the one request flow migrated to v2 (ADR-0009). Its accept path inherently spans the `Request` consistency boundary (state transition) and the `Project` consistency boundary (membership + role). The aggregates reference each other only by id; the *use-case* coordinates them.
- The codebase already knows the rowversion pattern (`WorkflowEngine.Core` uses `IsRowVersion().IsConcurrencyToken()`), so the absence here is a constraint, not an oversight.
- An earlier version of `CreateProject` committed twice (project, then owner grant); it was collapsed to a single commit for the same atomicity reason, reinforcing this as the module's convention for tightly-coupled multi-aggregate writes.

## Considered options

- **One transaction across both aggregates (chosen).** Atomic, simple, no orphaned half-states; the cost is a deliberate deviation from one-aggregate-per-transaction, acceptable for a modular monolith on a single database where both aggregates live in the same `DbContext`.
- **Settle the Request, then grant membership from a post-commit `RequestStateTransitionedEvent` handler.** Honors one-aggregate-per-transaction and is the path to take if these ever split across databases/services. Rejected now: the in-process domain-event channel is at-most-once (no outbox), so a lost event would leave an accepted request with no membership — strictly worse than the atomic write until an outbox exists.
- **Add `rowversion` concurrency tokens now.** Rejected: schema-owned by legacy migrations (overlay constraint). Tracked as a follow-up; an interim `IsConcurrencyToken()` on an existing column could guard the most damaging races without a schema change if the simultaneous-accept risk proves real.

## Consequences

- Accept is atomic: a failure in either `Accept()` or `AssignUserWithRole(...)` rolls back both, and the use-case returns the mapped problem before any side effect (notification, cache eviction) runs.
- The "one aggregate per transaction" rule is knowingly relaxed for this flow (and for `CreateProject`); reviewers should not "fix" it by splitting the commit without first introducing an outbox.
- Last-write-wins remains the concurrency model for all aggregates until a `rowversion` migration lands on the legacy side. Two simultaneous accepts depend on the membership unique index to avoid a duplicate row.
- If self-onboarding (or any cross-aggregate write) later moves off the shared `DbContext`, this decision must be revisited in favor of the event-driven option above, which then requires the outbox.
