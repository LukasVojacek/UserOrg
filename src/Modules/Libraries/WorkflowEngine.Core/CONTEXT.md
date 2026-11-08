# WorkflowEngine

A generic, domain-agnostic library for asynchronous, typed state-machine orchestration.
A consumer defines a **Strategy** (the state machine) and one **Step handler** per state; the engine
persists, dispatches, retries, and recovers each **Request** instance. It ships no concrete strategies
and has no HTTP, no host-framework, and no UserOrg-domain coupling.

## Language

### Instance & definition

**Request**:
One *instance* of a running workflow — the orchestration aggregate that is submitted, persisted, tracked,
and queried. Identified by a `RequestId`. This is the engine's central entity.
_Avoid_: job, run, workflow instance, and **flow** (flow is the definition/progression, not the instance)

**Flow**:
The state-machine *definition and progression* a Request moves through — the graph of states a Strategy
declares and the path an instance takes across them. A Flow is never persisted as an entity; a Request is.
_Avoid_: using "flow" to mean the Request instance; saga

**Strategy**:
The typed state machine for one kind of Request: it declares the states, the initial step type, and the
pure `Transition` table between states. One Strategy per Flow kind, resolved by name or by initial step type.
_Avoid_: workflow definition (in code prose), saga, orchestrator

### Steps & execution

**Step**:
One domain state in a Strategy — a single unit of work with a stable name (e.g. `"CallHEAppE"`).
_Avoid_: stage, task, activity

**Step request**:
The typed message (`StepRequest` subtype) carrying exactly the input one Step's handler needs. Serialised
to `InputData` before execution for deterministic replay.
_Avoid_: command, event, message (unqualified)

**Step handler**:
The unit that performs the work for exactly one Step (`IStepHandler<TRequest>`). Pure with respect to
routing — it has zero knowledge of the queue, the Worker, other handlers, or what runs next.
_Avoid_: processor, worker (the Worker is the engine's dispatch loop, not a handler)

**Transition**:
The pure, side-effect-free function on a Strategy that maps a completed Step + its result to the next
Step request, or to `null` (terminal). Re-run on crash-recovery replay, so it must stay pure.
_Avoid_: router, next-step selector

**Worker**:
The background loop that dequeues step requests and drives the execute → persist → enqueue-next cycle.
_Avoid_: handler, processor, consumer

### State layers

**Request status**:
The flow-level lifecycle of the whole instance: `Accepted → InProgress → Completed | Failed`. Answers
"where is the business flow as a whole?".
_Avoid_: step status, flow status (the word "flow" here would collide with the definition sense above)

**Step execution status**:
The technical outcome of a single Step attempt: `Running | Completed | Failed | RetryScheduled`. Per-attempt,
not per-instance.
Row model: a step name has **at most one live (non-`Completed`) row** per Request, and that row carries the
attempt count (`StepRetries`) forward across retries — `RecordStepStarted` reuses it rather than appending.
Each completed execution of that step name leaves its own `Completed` row, so a step that completes and runs
again has one `Completed` row per completed execution plus at most one live row.
_Avoid_: request status; treating `RetryScheduled` as a Request status (it is step-level only); reading "one
row per attempt" into the status — the attempts of one live step share a row

**Terminal**:
The condition where `Transition` returns `null` — the Flow has no next Step and the Request closes
(`Completed`, or `Failed` on a permanent error).
_Avoid_: final, end state (reserve for state-machine diagrams)

### Waiting & control

**Gate**:
A Step that performs no compute and parks the Request until an external **Signal** arrives — the
state-machine's representation of "waiting for a human or another system". Declared in the builder with
its expected Signal type. While parked, the Request status is `AwaitingSignal`.
_Avoid_: pause, approval step (too specific — a Gate is any external wait, not only approval), manual step

**Signal**:
The typed payload delivered to a Request's current Gate to unpark it. The engine validates the Signal's
type against the Gate's declared type, then feeds it to `Transition` as that Gate's step result.
_Avoid_: event, message, callback (a callback is the *outbound* terminal notification)

**Resume**:
Delivering a Signal to the Gate a Request is currently parked at, moving it out of `AwaitingSignal`.
Distinct from **Retry**: Resume is the normal forward path through a Gate; Retry is operator recovery
of a *failed* Step. Conflating them is the mistake the API deliberately avoids.
_Avoid_: retry, continue, advance (when an external Signal is involved, it is always Resume)

**Start**:
Submitting a new Request through the builder-owned initial-step factory: the caller supplies an input
and options, and the engine constructs the first Step. The low-level `Submit` (caller hands in a fully
built initial Step) is the escape hatch beneath it.
_Avoid_: create, new request, submit (Submit is specifically the low-level form)
