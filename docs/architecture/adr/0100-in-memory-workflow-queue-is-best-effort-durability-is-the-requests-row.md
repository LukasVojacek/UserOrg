# ADR-0100 — The in-memory workflow queue is best-effort; the `workflow.Requests` row is the durability boundary

- Status: Accepted
- Date: 2026-09-07
- Context: WorkflowEngine.Core (`workflow` schema); T-005-05, plan
  `create-resource-on-location-hang-plan-ready.md` decision D5. T-005-04 (queued right after this one)
  makes `EnqueueAsync` non-blocking and able to drop messages under back-pressure.

## Context

`IQueuePort`'s production adapter (`ChannelQueueAdapter`) is an in-process `Channel<StepRequest>`. It does
not survive a process restart, and — once T-005-04 lands — it does not even guarantee delivery while the
process is up: a full channel drops the message rather than blocking the caller.

Two strategies currently drive real work through this shared queue:

- **ADR-0063's gated `assign-resource-to-project` flow** (`src/Modules/Core/Features/Resource/Workflow`) —
  two human/external gates (`PiApproval`, `ResourceAvailability`), each a resume that re-enters the queue.
- **`create-project-resource`** (`CreateStrategy.cs`) — the simpler, ungated creation flow.

Both submit through the same `SubmitHandler`/`WorkflowClient`, and both enqueue onto the same
`ChannelQueueAdapter`. Neither strategy can see or influence queue delivery — the queue is engine
infrastructure, not something a flow definition touches.

Before this ADR, `RequestWorkerStartupRecovery` (the one-shot startup scan, ADR-0067) only recovered a
`Running` step whose lease had expired. Two classes of loss were invisible to it:

1. An `Accepted` request with no step row at all — submitted, but the initial enqueue never reached a
   worker (process died between the commit and the consume, or a full channel dropped it).
2. An `InProgress` request with no `Running` step — the flow advanced past its last completed step (or a
   gate resumed) and the message carrying the next step was lost the same way.

A request stuck in either shape never runs again: nothing re-derives it, and no operator-visible error is
raised. The request simply hangs, indistinguishable from one still legitimately in flight.

## Decision

**The queue is best-effort. The durability boundary is the persisted `workflow.Requests` row (plus its
`RequestSteps`), not the channel.** Anything the queue can lose must be re-derivable from that row alone.

Startup recovery (`RequestWorkerStartupRecovery`) is the mechanism that re-derives it, and now covers three
classes in one one-shot pass, all registered as `IHostedService` before `RequestWorker` (ADR-0067):

1. `Running` step, lease expired — reset via `ResetStepForRetry`, re-enqueued from persisted `InputData`
   (unchanged from ADR-0067).
2. `Accepted`, no step row — re-enqueued by reconstructing the initial step from the aggregate's
   `Payload` (`SubmitHandler` stores the initial step there verbatim, before the first enqueue) and the
   strategy's `InitialStepType`.
3. `InProgress`, no `Running` step, no `RetryScheduled` step — re-enqueued by replaying the strategy's
   pure `Transition` over the last completed step. This reconstructs the actual next message (correct
   concrete type and data), not a placeholder — the same computation that produced the message the lost
   enqueue was carrying.

`RetryScheduled` steps are deliberately excluded from class 3: they are `InProgress` with no `Running` step
too, but they belong to `RetryScheduledScanner`, and a duplicate re-enqueue would skip the back-off — the
one duplicate a single worker cannot absorb (see Consequences).

## Consequences

- **Amends ADR-0067's "Startup recovery does not loop" consequence.** That statement only argued for the
  `Running`-lease case, where `NULL != LockedUntil < cutoff` makes non-looping automatic. Classes 2 and 3
  above are **not lease-gated** — there is no expiry to wait out, nothing marks them "in recovery". They
  are safe to re-enqueue only because recovery is one-shot and runs to completion before `RequestWorker`
  starts consuming (enforced by hosted-service registration order, and pinned by
  `Should_RegisterRecoveryAsOneShotHostedServiceBeforeWorker_When_WorkflowEngineRegistered`). A periodic
  scan would duplicate live in-flight steps: "no `Running` step yet" is also the ordinary gap between a
  step completing and the next one starting (`RequestWorker.cs` between completing one step and the next
  `RecordStepStarted`; `WorkflowClient.cs` between a resume's `SaveAsync` and its `EnqueueAsync`).
- At worker count 1, a duplicate re-enqueue of an already-completed step is absorbed by
  `RequestWorker.IsStepCompleted`'s replay guard (`RequestWorker.cs:87`/`:108`): the worker replays the
  transition instead of re-executing the handler. The one step this guard does **not** protect is
  `RetryScheduled` — re-enqueuing it re-executes immediately and skips the back-off — which is why it is
  excluded from recovery's predicate rather than relied upon to be harmless.
- **Known, unclosed gap: a genuine gate resume is not recoverable by class 3.** `WorkflowClient.cs`'s
  `ResumeAsync` computes the post-gate step via `gate.Resume(signal, aggregate)`, not via
  `strategy.Transition`, and neither the delivered signal nor the resumed step is ever persisted —
  `MarkAwaitingSignal` explicitly does not store the signal type. If the resume's enqueue is lost after its
  `SaveAsync`, replaying the *last completed* step (the one that led into the gate) routes back into the
  `GateStep` the request already left, which `RequestWorker.cs:95`'s replay guard is written to refuse
  (parking a request twice, not re-running its handler). Recovery detects this — the replayed transition
  lands on a `GateStep` or on a state that does not match the persisted `CurrentState` — and logs the gap
  rather than enqueueing something stale; it does not silently re-park the request. Closing this for real
  needs the resumed step made durable (e.g. persisted alongside the status flip in `ResumeAsync`, before
  the enqueue) so recovery has something concrete to replay from. Out of scope for this ADR.
- The two strategies named above (`assign-resource-to-project`, `create-project-resource`) get this
  coverage for free — recovery is engine infrastructure, not something either flow definition implements.
- **Startup recovery is one-shot, not continuous — a mid-run drop is not covered until the next restart.**
  `ChannelQueueAdapter`'s doc comment previously read as if any drop were automatically safe because
  recovery re-enqueues it; that overstates it. Recovery only runs once at boot. A message dropped while the
  process is already up and serving traffic — including the mid-flow enqueue at `RequestWorker.cs:187`, not
  just the initial submit — strands that request until the process restarts and recovery runs again. In the
  meantime the drop is visible only as a Warning log line. At the default channel capacity (1000) this is
  rare, and a dropped enqueue still beats a caller hung waiting on a full channel, but it is a real
  window, not a closed one. `workflowengine.queue.enqueue_dropped` (the `Counter<long>` `ChannelQueueAdapter`
  already emits on every drop) is the signal to alert on for this case — a non-zero rate outside of startup
  recovery's own window means a request is stranded right now, not merely "will be caught on next boot".
- **Amendment 2026-09-08 ([`0101-workflow-step-payloads-carry-natural-keys-and-request-dtos.md`](0101-workflow-step-payloads-carry-natural-keys-and-request-dtos.md)):
  the `workflow.Requests` row is the durability boundary only if its payload round-trips.** Everything above
  argues about whether a message is *re-enqueued*; it silently assumes that a persisted row can be turned
  back into a message. That assumption did not hold for `create-project-resource`, whose steps carried the
  `Provider` aggregate: `JsonSerializer` threw on the way in for any provider already owning an allocation,
  and `Deserialize` threw `NotSupportedException` on the way out for all of them. Startup recovery,
  `RetryScheduledScanner` and the replay guard were all therefore inoperative on that flow, while the row
  itself looked perfectly durable. **Re-derivability from the row — a clean System.Text.Json round trip over
  the step payload — is a precondition of this ADR's durability claim, not a detail of the flow.** It is
  enforced by `Tests/Modules/Core/Core.ArchitectureTests/StepPayloadRoundTripTests.cs` and
  `WorkflowStepPayloadTests.cs`.
- **Amendment 2026-09-08 (same ADR): a row that cannot be materialised is now bounded, not eternal.**
  `RetryScheduledScanner` had three branches — unresolvable `MessageType`, deserialisation threw,
  deserialisation returned `null` — that released the lease and left the row due, so the same row was
  re-picked on every poll forever with no terminal state. All three now route through one failure path
  bounded by `WorkflowEngineOptions.MaxMaterializationFailures` (default 3), which ends the request with a
  non-empty `ErrorCode`. The counter is in-process per scanner instance, so a restart resets it; see the
  new ADR for the upgrade trigger.

## Verification

`RequestWorkerStartupRecoveryTests` (SQLite, integration) pins all three recovered classes and four guard
rails: a live lease is left alone, an `AwaitingSignal` gate is left alone, a completed request is left
alone, and — the trap a naively widened "no `Running` step" predicate walks into —
`RetryScheduled` is left alone.
