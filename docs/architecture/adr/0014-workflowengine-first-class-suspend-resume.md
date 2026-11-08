# ADR 0014 - WorkflowEngine: first-class suspend/resume, not retry-as-poll

- Date: 2026-06-09
- Status: accepted (implemented 2026-06-09)

## Decision

A workflow that must wait for an external actor (a human approval, another system) does so through a
**first-class wait/resume mechanism**, not by abusing the retry machinery:

- A **Gate** is a step that performs no compute and parks the Request until a typed **Signal** arrives.
  In the flow definition a Gate declares its expected signal type, e.g. `.Gate<ApprovalDecision>("AwaitServiceAccountApproval")`.
- `RequestStatus` gains a first-class value **`AwaitingSignal`**. While parked, the Request is in
  `AwaitingSignal` — distinct from `InProgress` (running) and from a step's `RetryScheduled`. The worker
  neither dispatches nor fails a parked Request, and the crash-recovery scan skips it.
- An external caller resumes via **`ResumeAsync<TSignal>(requestId, signal)`**. Resume targets whatever
  Gate the Request is *currently* parked at (the caller does not name the gate); the engine validates that
  `TSignal` matches that Gate's declared signal type, then feeds the signal to the routing function to pick
  the next step. Resume on a Request that is not `AwaitingSignal` is a conflict/no-op driven by status +
  `RowVersion`, not an exception on the happy path.
- **Resume is kept distinct from Retry.** Retry (`RetryLastStepAsync` / `RetryStepAsync`) remains the
  operator's recovery action for a *failed* step; Resume is the normal forward path through a Gate.

## Context

- The existing docs recommended a "retry-as-poll" pattern: an `AwaitApprovalHandler` throws
  `RetryableException` until the approval arrives, relying on the retry scanner to re-run it. This is
  **broken under the engine's own defaults**: `MaxStepRetries` defaults to `1`, so after a single retry
  the worker treats the next `RetryableException` as permanent and marks the flow `Failed`. An approval
  that takes longer than one back-off interval kills the flow.
- The deeper problem is semantic: "waiting for a human" is **not** a transient failure. Routing both
  through the retry mechanism conflates two unrelated lifecycle concepts and overloads the word "retry" —
  the same `RetryLastStepAsync` was being documented both as operator error-recovery *and* as the way to
  resume after approval, so a caller could not tell which one they were invoking.
- The target product flow (project-resource onboarding) has **two** genuine external waits — service-
  account approval and a manual publish step — each potentially lasting hours or days. These are central,
  not edge cases.
- A "design it twice" comparison of four candidate public APIs confirmed that an explicit Gate/Signal/
  Resume model is needed regardless of the chosen authoring surface; only a full workflow-as-code rewrite
  (event-sourced replay) would avoid an explicit wait concept, and that was rejected as a core rewrite
  (see ADR-0015 context).

## Considered options

- **First-class Gate + `AwaitingSignal` status + typed `ResumeAsync` (chosen).** Clean separation of
  "waiting" from "failing"; a machine-readable status the UI can map directly to an "action required"
  state; type-checked resume. Cost: one new `RequestStatus` value (a DB value), a worker branch that
  excludes parked Requests from dispatch/fail/recovery, and a new public method.
- **Keep retry-as-poll.** No engine change, but it does not work under the default retry budget, burns the
  retry budget on normal waits, and muddles transient-failure semantics with intentional waiting.
  Rejected.
- **Step-level `Awaiting` only (no flow-level status).** Represent the wait solely as a step execution
  status and keep `RequestStatus` four-valued. Less schema change, but the flow-level status can no longer
  answer "does this need human action?" without inspecting steps — worse for UI and operations. Rejected.
- **Resume by overloading `RetryLastStepAsync`.** The previously documented approach. Rejected: overloads
  one verb with two opposite meanings (recovery vs normal progression).

## Consequences

- **Schema/migration.** `RequestStatus` gains `AwaitingSignal`; the `Status` column must accept the new
  value. This is the highest-risk part of the change and must land before the engine has a production
  consumer.
- **Worker logic.** The dispatch loop, the permanent-failure path, and the crash-recovery scan must all
  treat `AwaitingSignal` as "do not run, do not fail" until a Resume re-enqueues the next step.
- **UI/operations.** `AwaitingSignal` (plus the awaited signal type and current gate name on the status
  projection) maps directly to an "action required" indicator. Business *milestones* (e.g. "completed",
  "published") still come from `CurrentState`, not from `Status`; `AwaitingSignal` is an
  execution-lifecycle state, not a business milestone.
- **Routing stays pure.** A Gate's routing reads the delivered Signal plus the read-only request snapshot
  and returns the next typed step; like all transitions it is re-run on replay and must remain
  side-effect-free.
- **Resume vs Retry never overload again.** The glossary and API fix the distinction: Resume = forward
  through a Gate; Retry = operator recovery of a failed step.
