# ADR-0067 — A step's claim is its first persisted row, settled by the aggregate rowversion

- Status: Accepted
- Date: 2026-07-14
- Context: WorkflowEngine.Core (`workflow` schema); surfaced by #251 / MR !297
- Supersedes / amends: operational detail of ADR-0014 (Gate/ResumeAsync), ADR-0056 (workflow persistence)

## Context

`RequestWorker` guards a step against multi-instance double-execution with a step lease:
`TryAcquireStepLeaseAsync` runs an `UPDATE` over `workflow.RequestSteps` for `(RequestId, StepName)`
and treats "0 rows affected" as "another worker holds it".

That row, however, is written by `RecordStepStarted` — which the worker calls *after* the lease is
granted. On a step's **first** execution there is therefore nothing to update, the `UPDATE` affects
0 rows, and the worker concluded the step was held elsewhere and dropped it.

The engine had no production consumer until #251, so this never fired: every request died at its
first step (and at every step after a gate resume). Live symptoms on SQL Server: `workflow.RequestSteps`
empty, no PI e-mail, the Core projection left with `WorkflowRequestId = Guid.Empty`, and the ADR-0066
single-active index then blocking any retry of the same request.

The unit suites missed it because EF InMemory cannot execute `ExecuteUpdateAsync`, so the repository
was replaced by a fake that granted every lease unconditionally.

## Decision

**A step with no persisted row is unclaimed, and the first worker to persist that row owns it.**

1. `TryAcquireStepLeaseAsync` returns `true` when no row exists for `(RequestId, StepName)`. An existing
   row keeps the previous semantics: lease granted only when unlocked or expired.
2. Concurrent claims are settled by the **rowversion on `workflow.Requests`**, not by the lease. Both
   workers call `RecordStepStarted` + `SaveAsync`; EF's concurrency token lets exactly one commit, and
   `SaveChanges` is transactional, so the loser inserts no step row either.
3. The loser catches `DbUpdateConcurrencyException` and drops the step, exactly as a lost lease would.
   It must not escape the consume loop — an unhandled exception there kills the worker outright.
4. `SaveAsync` runs **before** the handler is invoked. This ordering is the real safety property: a
   worker that loses the claim can never produce a duplicate effect (a second PI e-mail, a second attach).

## Consequences

- The lease remains the guard for *re-execution* (expired lease, retry); the rowversion is the guard for
  the *first* execution. Two mechanisms, one per case.
- **This relies on flows being sequential.** `Transition` returns a single next step, so two steps of the
  same request never run concurrently. If the engine ever gains parallel fan-out, the rowversion becomes a
  liability rather than a guard — a losing parallel branch would vanish silently — and the claim must move
  to a per-step atomic upsert instead.
- **Amendment 2026-09-07 (T-005-06):** the consequence above — "`RetryScheduledScanner` is unaffected" —
  was stale and wrong. `RetryScheduledScanner` acquires the step lease (as `WorkerId`) and enqueues the
  step *without releasing it*, because that lease doubles as the scanner's own de-dup: `FindDueRetriesAsync`
  keeps returning the same due step every poll until the worker flips its status, and releasing the lease
  before enqueueing would turn a stalled worker into a queue-filler, reversing the "lease remains the guard
  for re-execution" property above. Since the scanner and the worker are hosted services in the same process
  sharing one `IOptions<WorkflowEngineOptions>` (one `WorkerId`), the worker's own
  `TryAcquireStepLeaseAsync` therefore saw a live lease — its own — and dropped the step: a due scheduled
  retry never ran. The fix keeps the scanner's lease exactly as it is and adds owner semantics to the
  acquire predicate instead: a lease already held by *this* `WorkerId` is acquirable
  (`WorkflowDbContextRepository.TryAcquireStepLeaseAsync`), alongside the existing "unlocked or expired"
  conditions. A lease held by a *different* worker is still refused — that invariant is unchanged and is
  what `StepLeaseOwnershipTests.Should_RefuseTheLease_When_AnotherWorkerHoldsALiveLease` pins.
  > Amendment 2026-09-12 (T-121): the amendment above is kept for its fix, but its *reasoning* about the
  > de-dup was wrong at the time it was written. `TryAcquireStepLeaseAsync` and `ReleaseStepLeaseAsync`
  > (`WorkflowDbContextRepository`) match on `RequestId + StepName` and so touch **every** row of that step
  > name, while `RecordStepStarted` then appended a fresh row — so the row the worker flipped was not the row
  > the scanner had leased, and "the same due step keeps coming back until the worker flips its status" did
  > not hold. After T-121 the de-dup rests on a row invariant rather than on the lease alone: a step name has
  > **at most one live (non-`Completed`) row** per request, because `RecordStepStarted` reuses that row
  > (`RequestStep.RestartAttempt`, carrying `StepRetries` forward) and appends only when there is none. The
  > row the scanner leases and reads through `FindDueRetriesAsync` is therefore the row the worker flips, and
  > the retained lease does its de-dup job for the reason this amendment claimed.
- Startup recovery does not loop: `ResetStepForRetry` nulls `LockedUntil`, and `FindStalledAsync` matches
  `LockedUntil < cutoff`, which `NULL` never satisfies.
  > Amendment 2026-09-07 ([0100](0100-in-memory-workflow-queue-is-best-effort-durability-is-the-requests-row.md)):
  > this argument only covers the lease-gated `Running` case. Startup recovery also re-enqueues two classes
  > that carry no lease at all — an `Accepted` request with no step row, and an `InProgress` request with no
  > `Running` step — because the in-memory queue can lose a message with nothing left behind to expire.
  > Those two are safe to re-enqueue only because recovery stays one-shot and is registered before
  > `RequestWorker`; unlike the lease case, there is no invariant that would make a periodic re-scan of them
  > safe.
- **Resolved by T-121 (2026-09-12) — see the T-121 amendment below.** Known, pre-existing and not addressed
  here: `RecordStepStarted` appends a *new* row per retry (identical `StepName`), so the lease `UPDATE` can
  touch several rows; `RequireStep` reads the last one.
- **Resolved by T-121 (2026-09-12) — see the T-121 amendment below.** Known limitation, out of scope for
  T-005-06: because `RecordStepStarted` inserts a new row rather than reusing the retried one (see above), the
  old `RetryScheduled` row from a completed retry survives after the retry executes, and `FindDueRetriesAsync`
  keeps returning it on every subsequent poll — permanent queue churn. The worker does not re-execute the step
  a second time (it short-circuits on `IsStepCompleted` and simply replays the already-settled transition),
  but the scanner re-enqueues the stale row indefinitely. Fixing this requires `RecordStepStarted` to reuse or
  supersede the prior row for the same `(RequestId, StepName)`, which is out of scope here.
  > Amendment 2026-09-12 (T-121): both paragraphs above are history. `RecordStepStarted` now takes exactly
  > the remedy the second one names — it reuses the step name's live row — so the one-live-row-per
  > `(RequestId, StepName)` invariant recorded in the amendment under the T-005-06 bullet holds: the lease
  > `UPDATE` touches the leased row plus only already-`Completed` rows, the site that increments the
  > attempt counter (`RecordStepRetryScheduled`) reads that row explicitly through `FindLiveStep`, while
  > `RequireStep` stays a status-blind `FindLast`, and no superseded `RetryScheduled` row survives a retry
  > for the scanner to re-enqueue. Neither paragraph describes an open defect any more. The attempt count that
  > row carries survives an automatic retry and a startup recovery, and only an operator retry through
  > `RetryHandler` restarts it, at `ResetStepForRetry`.

## Verification

Live against SQL Server (Aspire, local DB): create → PI e-mail with a working action link → anonymous
approve → `SendPiApprovalEmail`/`CheckResourceOperational`/`AttachResource`/`NotifyRequesterAssigned` all
`Completed` → resource attached → `ProjectResourceAssignmentChangedEvent` dispatched through the outbox.

> Amendment 2026-07-21: the step names above are left exactly as the run reported them. `CheckResourceOperational`
> has since been renamed to `CheckResourceExists`; see
> [0085](0085-resource-request-legacy-input-shape-draft-removed.md) for the rename and the
> data-compatibility constraint this invariant interacts with.

Regression tests: `StepLeaseTests` (repository, SQLite — the no-row branch) and `StepClaimRaceTests`
(worker — the loser skips the handler and the worker keeps consuming; fails if the catch is removed).
