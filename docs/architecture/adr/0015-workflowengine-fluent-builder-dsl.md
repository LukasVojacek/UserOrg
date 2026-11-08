# ADR 0015 - WorkflowEngine: fluent builder DSL as the public flow-definition API

- Date: 2026-06-09
- Status: accepted (implemented 2026-06-09)

## Decision

The public API for **defining a flow** is a **fluent builder DSL** that declares states, binds each
state to its `IStepHandler<T>`, declares transitions (including conditional branches off a step's typed
result), declares Gates with their signal type, and emits all DI registrations from one place. The
builder validates flow completeness **at startup** (single entry, every state reachable, every state has a
handler, every transition target exists, every Gate has a signal type).

The engine's **execution model is unchanged**: typed `StepRequest` payloads, pure/replayable routing, the
background worker, `Channel` queue, step leases, retry scanner, and crash recovery all stay. The DSL is a
new *authoring surface* over the existing `IRequestStrategy` / `IStrategyRegistry`; it does **not** replace
the engine internals.

Supporting public-API decisions made together with the DSL:

- **Runtime facade** is small: `IWorkflowClient` with `StartAsync`/`SubmitAsync`, `ResumeAsync`,
  `RetryAsync`, `GetStatusAsync`.
- **Start** is DSL-aware: `StartAsync<TFlow>(input, SubmitOptions?)` lets the builder own initial-step
  construction; low-level `SubmitAsync(StepRequest)` remains as an escape hatch. `SubmitOptions` (a record:
  `ClientId`, `CallbackUrl`, `IdempotencyKey`) replaces the previous three trailing nullable-string
  parameters on submit.
- **Convention layer is opt-in.** An assembly-scanning registration may be offered as sugar for the linear
  case, but the explicit builder is the canonical, type-safe path.
- **Inter-step data flow** is closed by exposing completed-step results on the read-only snapshot
  (`IRequestSnapshot.ResultOf<TStep>()`) so routing/gate lambdas can read prior outputs; the original input
  is already available via the snapshot's `Payload`. No mutable "context bag" is introduced.

## Context

- The current authoring experience scatters one flow across four unlinked places — the strategy class, N
  `StepRequest` records, N `IStepHandler<T>` classes, and N `AddStepHandler<,>` DI calls — with nothing
  guaranteeing every routed state has a registered handler (a missing registration fails at dispatch, at
  runtime). Submission used three trailing nullable `string` parameters, and the docs referenced a
  `SubmitCommand`/`CreateInitialMessage` surface that no longer exists.
- A "design it twice" exercise generated four radically different public APIs (minimal data-object surface;
  fluent builder; convention+attributes; imperative workflow-as-code). The first three are ergonomic
  variants of the *same* declarative state-machine model and share one limitation: pure routing cannot see
  prior-step data unless the engine exposes it. The fourth (imperative orchestration à la
  Temporal/Durable Functions) removes that limitation for free but only by **replacing** the engine core
  with a deterministic event-sourced replayer, imposing determinism constraints on user code (needing a
  Roslyn analyzer) and flow versioning.
- The engine is already mature and tested; the stated goal of this work is a *better public API on the
  existing engine*, explicitly **not** a core rewrite. That rules out the imperative model despite its
  appeal for branching and data flow.

## Considered options

- **Fluent builder DSL over the existing engine (chosen).** One self-documenting place per flow,
  generic-typed transitions, startup completeness validation, emits DI. Keeps the mature execution core.
  Cost: a non-trivial generic builder surface to maintain; gate names remain strings matched at startup,
  not compile time; an impure routing lambda is possible and only convention-guarded.
- **Minimal data-object surface.** Smallest possible API (a `Flow` value object + tiny runtime facade).
  Cheapest to learn, but pushes step identity into strings and surfaces most errors at runtime; weaker
  discoverability. Folded in only as the shape of the small runtime facade.
- **Convention + attributes + assembly scanning.** Near-zero ceremony for linear flows, but encodes flow
  order as declaration order (reordering two lines silently rewires the flow) and branching breaks the
  convention. Kept only as an **opt-in** sugar layer, not the canonical API.
- **Imperative workflow-as-code (event-sourced replay).** The nicest API for branching and inter-step data
  (flow is one async method; prior results are local variables). Rejected as the core direction: it
  deletes the pure transition function and builds a new deterministic replayer — a paradigm shift, not a
  refactor — contradicting the "don't rewrite the mature engine" goal, and introduces silent
  determinism-violation bugs that only manifest on crash/replay.

## Consequences

- **Engine deltas are small and additive:** the builder layer (`IFlowBuilder`, a flow-definition
  abstraction, `AddFlow`) plus startup validation; `IRequestSnapshot.ResultOf<TStep>()`; the renames from
  the vocabulary clean-up (`WorkflowEngineOptions`, `MarkRequestFailedAsync`, `SetCurrentState`,
  `StartAsync` vs `SubmitAsync`). Worker, queue, leases, retry, recovery, handlers, and persistence are
  untouched. The Gate/Signal/`AwaitingSignal`/Resume additions come from ADR-0014.
- **Type safety is partial, by construction.** Handler/payload binding and transition lambda inputs are
  compile-time typed; the *graph* (gate-name matching, reachability, single-entry, branch exhaustiveness)
  is validated at **startup**, throwing on a malformed definition rather than failing mid-flight.
- **Impure routing is the main footgun.** Routing/gate lambdas are re-run on crash recovery and must be
  pure; the signatures deliberately offer no `IServiceProvider`/`CancellationToken` to remove the obvious
  temptation, but purity cannot be enforced by the compiler. This must be documented prominently.
- **The DSL is the API.** Once consumers adopt it, builder changes are breaking; new transition shapes
  (fan-out, timeouts, sub-flows, compensation) require deliberate surface growth.
- Doing this now is cheap because the engine has **no production consumer** yet; the same change after the
  first real flow is wired would be a breaking change with migrations.
