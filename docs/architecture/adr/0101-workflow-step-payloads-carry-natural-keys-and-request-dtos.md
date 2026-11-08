# ADR 0101 — Workflow step payloads carry natural keys and request DTOs, never aggregates

- Status: Accepted
- Date: 2026-09-08
- Context: WorkflowEngine.Core (`workflow` schema) and every `Core` flow that runs on it; written from the
  T-055 incident on the `create-project-resource` strategy.
- Relates to: [`0029-natural-keys-at-contract-and-aggregate-boundaries.md`](0029-natural-keys-at-contract-and-aggregate-boundaries.md)
  (natural keys at boundaries — this ADR extends that boundary to include the workflow step payload),
  [`0056-workflow-engine-platform-integration.md`](0056-workflow-engine-platform-integration.md)
  (the engine and its persistence),
  [`0067-workflow-step-claim-invariant.md`](0067-workflow-step-claim-invariant.md) (step claim, lease and
  replay),
  [`0100-in-memory-workflow-queue-is-best-effort-durability-is-the-requests-row.md`](0100-in-memory-workflow-queue-is-best-effort-durability-is-the-requests-row.md)
  (the durability boundary and startup recovery).
- Amends: §7 of [`0043-provider-factory-create-and-allocate-resource.md`](0043-provider-factory-create-and-allocate-resource.md)
  (see that file's §7, now marked superseded),
  [`0050-multi-location-resource-provisioning.md`](0050-multi-location-resource-provisioning.md),
  [`0057-async-resource-creation-202-accepted-and-status-endpoint.md`](0057-async-resource-creation-202-accepted-and-status-endpoint.md)
  and the Consequences of
  [`0100-in-memory-workflow-queue-is-best-effort-durability-is-the-requests-row.md`](0100-in-memory-workflow-queue-is-best-effort-durability-is-the-requests-row.md).

> **Citation convention for this ADR.** The numbers `0057` and `0061` are each used by two different files in
> this directory, so every cross-reference here names the **full filename**, never a bare `ADR-NNNN`. The
> duplicate numbering itself is a separate finding and is not addressed here.

## Context

The `create-project-resource` flow put the resolved `Core.Domain.Provider.Provider` **aggregate** into its
step records and let the engine carry it from the HTTP scope to the worker scope
(`src/Modules/Core/Features/Resource/Workflow/CreateStrategy.cs`, the four steps under
`src/Modules/Core/Features/Resource/Workflow/Steps/`). Production reported a
`PK_AssignedLocation` violation; the investigation found that a single design mistake produced at least four
distinct failures, only one of which was the reported one.

### The root cause, stated precisely

`Provider.Id`, `Location.Id` and `AssignedLocationValue.Id` are EF **shadow** properties
(`ProviderConfiguration.cs`, `LocationConfiguration.cs`). Their values live in the `ChangeTracker`, not on the
CLR object, so they **die with the disposed `DbContext`**. `AssignedLocation.Id` is a **CLR** property,
client-generated as UUIDv7 in `AssignedLocation.Create`
(`src/Modules/Core.Domain/Provider/AssignedLocation.cs:80`), so it **survives detach**.

When the worker's handler passed that detached graph to `DbSet.Add`, EF cascaded `EntityState.Added` over
every untracked reachable entity **regardless of whether its key was set**. The shadow-keyed nodes were
re-INSERTed as brand-new rows (new keys minted by `UuidV7ValueGenerator`), duplicating rows that already
existed; the one node whose CLR key survived, `AssignedLocation`, was INSERTed with a key the table already
held and collided on `PK_AssignedLocation`. That is the reported symptom, and it is a symptom of the payload,
not of the insert.

**`Attach` or `Update` cannot be substituted for `Add` here.** Both infer entity state from key presence, and
the shadow/CLR asymmetry makes that inference wrong in *both* directions at once: the shadow-keyed nodes look
new when they are not, and the CLR-keyed `AssignedLocation` looks existing when it is not. A reload of the
aggregate in the handler's own scope removes the cascade but does not fix the write on its own — EF's
`NavigationFixer` then attaches a freshly created, client-keyed `AssignedLocation` as `Modified`, and the
`UPDATE` affects 0 rows (`DbUpdateConcurrencyException`). The working mechanism is a reload **plus** an
explicit `Add` of the newly created child, which is exactly what
`IProviderRepository.MarkAssignedLocationAsAdded` does: `Add` walks the reachable graph and marks every
not-yet-tracked node `Added`, so the new `AssignedLocation` and its still-untracked `AssignedLocationValue`
children are attached together. Setting `Entry(x).State = EntityState.Added` instead is **not** equivalent —
it tracks one node and does not traverse navigations, so the children are silently never inserted.

### Three more failures from the same payload, none of them the reported one

1. **The payload could not be serialised at all.** A provider loaded through the real
   `IProviderRepository.FindAsync` (the lean `_providerIncludes` load) and already owning one allocation threw
   `NullReferenceException` from `AssignedLocation.ResourceKey` (`_resource!.Key`, which the lean load never
   populates) *inside* `JsonSerializer`. `RecordStepStarted` died before the step row was written. The
   consequence of putting an aggregate in the payload was therefore **never replay-only** — it broke step
   persistence on the spot.
2. **The payload could not be deserialised.** `NotSupportedException: Deserialization of types without a
   parameterless constructor … is not supported. Type 'Core.Domain.Provider.Provider'.` Every replay,
   scheduled retry and startup recovery of these four steps was dead.
3. **A member serialised to nothing, silently.** `CheckHeappeStep.ResolvedLocations` was
   `List<(Location, LocationAssignment)>`. System.Text.Json ignores `ValueTuple` fields, so two fully
   populated entries persisted as `[{},{}]` with no error anywhere.

## Decision

### 1. The payload rule

**A workflow step record carries natural keys and request DTOs. It never carries a domain entity, an
aggregate root, an EF-tracked object, a `ValueTuple`, or any interface other than `IReadOnlyList<>` /
`IReadOnlyCollection<>`.**

Concretely: `ProviderName`, not `Provider`; `ResolvedLocationRef(LocationName, Endpoint)`, not
`(Location, LocationAssignment)`. This is
[`0029-natural-keys-at-contract-and-aggregate-boundaries.md`](0029-natural-keys-at-contract-and-aggregate-boundaries.md)
applied to one more boundary: the step payload is a **persisted contract**, and it is as much a boundary as an
HTTP body is.

Members that must be present for the step to be meaningful carry `[property: JsonRequired]`. Do **not** add
`[SetsRequiredMembers]` to a step record's constructor — it disables exactly the enforcement these members
exist for, and an old row would then deserialise quietly into `default(ProviderName)`.

### 2. The handler loads the aggregate in its own scope

**A handler resolves every aggregate it needs from a repository inside its own DI scope and unit of work**,
from the natural key the payload carries. It never operates on an object graph that arrived from another
scope. An aggregate that is gone by the time the worker runs is a `PermanentException` with a stable error
code (`PROVIDER_NOT_FOUND`), not a retry.

This is not merely hygiene. The worker runs in a scope the submitting HTTP request has long left
(`RequestWorker` creates a fresh scope per step), and after a restart there is no submitting scope at all —
only the row. Re-resolving per step is the only rule that behaves identically on the happy path, on a replay
and on a recovery.

### 3. The payload must survive a System.Text.Json round trip

**Every step record must serialise and deserialise back to an equal value with the runtime-type overloads the
engine actually uses** (`JsonSerializer.Serialize(step, step.GetType())` /
`Deserialize(raw, type)`). A member that cannot make that trip is not allowed in a step, whatever it looks
like at the call site — `ValueTuple` is the cautionary case, because it fails **silently**.

Round-trip fidelity is the precondition for everything the engine promises after the submit: replay
(`0067-workflow-step-claim-invariant.md § Consequences`), scheduled retry, and the startup recovery of
[`0100-in-memory-workflow-queue-is-best-effort-durability-is-the-requests-row.md`](0100-in-memory-workflow-queue-is-best-effort-durability-is-the-requests-row.md).
The `workflow.Requests` row is the durability boundary; a payload that does not round-trip makes that boundary
a lie, because the row survives while its content does not.

### 4. A persisted payload is a versioned contract

`workflow.RequestSteps.InputData` + `MessageType` and `workflow.Requests.Payload` are **persisted, versioned
contracts**, on the same footing as a public API schema. The following are all part of that contract and none
of them may be changed casually:

- the step's **CLR type name**,
- its **namespace**,
- its **assembly**,
- its **`StateName`** string,
- the JSON shape of every member.

`MessageType` is resolved by name at materialisation time. Renaming, moving or re-homing a step type
invalidates every in-flight row that names it; changing a `StateName` desynchronises the persisted
`CurrentState` from the strategy's transitions. A **widening** change (adding an optional member) is safe. A
**narrowing** change (removing a member, making one required, changing its type) makes old rows
unmaterialisable and requires the operational plan below — inventory the in-flight requests and end them
loudly. Old rows are never silently reinterpreted.

The `create-project-resource` remediation deliberately kept all four type names, namespaces, the assembly and
every `StateName` untouched, so existing rows still **resolve to a type** and then fail `JsonRequired` loudly,
instead of vanishing into an unresolvable `MessageType`.

### 5. Two guards enforce this, at two different moments

**Guard A — the detached-aggregate guard, at save time.** `DatabaseContext` tracks every aggregate root
handed to a repository `AddAsync` (`RegisterAddedAggregateRoot`, a `ReferenceEqualityComparer` set) and
`GuardAgainstDetachedAggregateInserts()` runs in `SaveChangesAsync` after `CollectDomainEvents()` and before
`ApplyConventions()` and both save branches — so it fires ahead of `BeginTransactionAsync` on the durable
outbox branch and nothing is written. Any `AggregateRoot` in state `Added` that was never registered throws
an `InvalidOperationException` naming the CLR type and the remedy. This is the backstop: it converts a
cascaded insert of a detached graph from a data-corrupting `INSERT` into a loud failure, whatever route
produced it.

**Guard B — the architecture rule and the round-trip suite, at build time.** In
`Tests/Modules/Core/Core.ArchitectureTests/`:

- `WorkflowStepPayloads.cs` discovers every non-abstract `StepRequest` descendant in the `Core` assembly by
  reflection and exposes the type graph; both suites read it through `[MemberData]` and both carry an
  `Assert.NotEmpty` over it, so the rule cannot pass by discovering nothing.
- `WorkflowStepPayloadTests.cs` rejects anything assignable to `IEntity`/`AggregateRoot`, any `ValueTuple`,
  and any interface other than `IReadOnlyList<>` / `IReadOnlyCollection<>` — rule 1.
- `StepPayloadRoundTripTests.cs` builds a fully populated sample of each step (`StepPayloadSampleBuilder.cs`:
  every member set, no nulls, non-zero enums, two elements per collection, so nothing can match a default on
  both sides) and compares member by member after a real round trip — rule 3.

Both suites were verified red against the pre-fix code, naming all three bad shapes without masking.

**The honest limit of guard B.** Failure 1 above (the `NullReferenceException` from a lean repository load)
is **not** reachable from a reflection-built sample: the builder constructs `Provider` through its private
parameterless constructor, so the sample aggregate is empty and `AssignedLocation.ResourceKey` is never
invoked. That failure mode is a persistence-load artifact — an in-memory aggregate built through the real
factories always has `_resource` set. Its regression cover is
`Tests/Modules/Core/Core.UnitTests/Features/Resource/Workflow/CreateResourceStepPayloadPersistenceTests.cs`,
which loads the provider through the real repository, **not** the reflection round trip. Recorded here rather
than papered over: guard B proves the shape, not the load.

### 6. Handler idempotency is a requirement, because delivery is at-least-once

**Every step handler must be idempotent.** This is not a nicety; it follows directly from the engine's
delivery semantics, and a handler that is not idempotent is defective even if it never fails.

The same step is delivered more than once by design, on at least four routes:

- the retry of a `RetryableException`;
- `RetryScheduledScanner` re-enqueueing a due row — and, per
  `0067-workflow-step-claim-invariant.md § Consequences`, a completed retry's stale `RetryScheduled` row is
  re-enqueued on **every subsequent poll**, indefinitely, because `RecordStepStarted` appends a new row rather
  than superseding the old one;
- startup recovery re-enqueueing an `Accepted` request with no step row or an `InProgress` request with no
  `Running` step
  ([`0100-in-memory-workflow-queue-is-best-effort-durability-is-the-requests-row.md`](0100-in-memory-workflow-queue-is-best-effort-durability-is-the-requests-row.md));
- a lost claim race, where the loser drops the step and the winner runs it
  ([`0067-workflow-step-claim-invariant.md`](0067-workflow-step-claim-invariant.md)).

`RequestWorker.IsStepCompleted`'s replay guard absorbs a redelivery of an *already-settled* step, but it does
not cover the `RetryScheduled` case and it does not cover a redelivery that arrives while the first execution
is still in flight. The handler is the last line, so the handler carries the requirement.

**Idempotency is asserted on the natural key, never on the exception text.** The pattern that works:

1. before doing the work, resolve the natural key (`new ProjectResourceKey(provider.Name, resourceName)`)
   through the repository; on a hit, return success without acting and without saving;
2. wrap the write in `try/catch (DbUpdateException)` and, in the catch, **re-read the same natural key** — a
   hit means the redelivered write is now redundant, so return success; otherwise rethrow.

Matching the exception message or an index name is forbidden: SQLite reports a uniqueness violation by column
names and SQL Server by index name (`IX_ProjectResource_ProviderId_Name`), so any message match that satisfies
a test is dead code in production. Re-reading the key the index protects is provider-neutral.

The inverse mistake is worse and is explicitly out of bounds: a blanket
`catch (DbUpdateException) { return success; }` reports genuinely failed writes as completed steps. Failures
are wrapped in `RetryableException`/`PermanentException`; they are never translated into success.

### 7. A payload that cannot be materialised fails the request after a bounded number of attempts

`RetryScheduledScanner` has **three** branches where a due step cannot be turned back into a message — an
unresolvable `MessageType`, a deserialisation that threw, and a deserialisation that returned `null` — and
each of them previously released the lease and left the row due, so the scanner re-picked the same row on
every poll, forever, at Warning level and with no operator-visible end state. All three now route through one
failure path bounded by `WorkflowEngineOptions.MaxMaterializationFailures` (default **3**).

The reasoning for having a threshold rather than failing on the first attempt: an unresolvable `MessageType`
is genuinely transient during a rolling deploy, when an old instance polls a row written by a new one. Below
the budget the scanner logs at Warning and releases the lease, so such a row self-resolves once the deploy
completes. At the budget it stops guessing: `RecordStepFailed` + `MarkFailed` with a **non-empty**
`ErrorCode` — `UNRESOLVABLE_MESSAGE_TYPE`, `DESERIALIZATION_FAILED` or `NULL_MESSAGE` — persisted through
`IRequestRepository.SaveAsync`. A poison payload therefore ends as a terminal, queryable state rather than as
log noise. The threshold is deliberately distinct from `MaxStepRetries`, which is a different budget
governing handler retries in `RequestWorker`.

**Known ceiling, stated rather than hidden.** The consecutive-failure counter is in-process per scanner
instance; it is not persisted on the row. A scanner restart resets it, so the worst case is
N × (restarts before it is noticed), not a hard global N. Persisting it needs a schema migration. Upgrade
trigger: if a poison row is ever seen surviving past a restart in production, move the counter onto
`workflow.RequestSteps`.

## Consequences

- The four `create-project-resource` steps carry `ProviderName` and `ResolvedLocationRef`; `Provider` is
  re-resolved once per step inside the handler's own unit of work. §7 of
  [`0043-provider-factory-create-and-allocate-resource.md`](0043-provider-factory-create-and-allocate-resource.md)
  ("No FlowModule") is superseded — the flow does run on the WorkflowEngine.
- **Rows written before this ADR are unrecoverable and are not migrated.** `Provider` has a private
  constructor, so the old payloads cannot be deserialised by any code we could write. In-flight
  `create-project-resource` requests are inventoried and terminated through
  `IRequestApplicationService.MarkRequestFailedAsync`, so they end loudly in a terminal state instead of
  silently. The runbook lives in the MR that lands this ADR.
- A new step, or a new member on an existing step, is now gated by two automated checks that fail the build
  rather than production. The cost is that a step cannot hold a convenient object graph; the handler pays one
  extra repository read per step. That read is the point, not the overhead.
- Handler idempotency is now a stated requirement rather than an accident, which means a new handler that
  writes without a natural-key pre-check is reviewable as defective.
- No schema change. `MaxMaterializationFailures` is a new option with a default, so no configuration change is
  required either.
- The in-process materialisation-failure counter is a known, bounded gap with a written upgrade trigger.

## Verification

- `Tests/Modules/Core/Core.ArchitectureTests/WorkflowStepPayloadTests.cs` and
  `StepPayloadRoundTripTests.cs` — the shape rule and the round trip, over every discovered step type,
  verified red against the pre-fix code.
- `Tests/Modules/Core/Core.UnitTests/Features/Resource/Workflow/CreateResourceStepPayloadPersistenceTests.cs` —
  the real-repository load, which is what covers the serialisation failure the reflection suite cannot reach.
- `Tests/Modules/Core/Core.UnitTests/Features/Resource/Workflow/Handlers/InitializeProjectResourceHandlerPersistenceTests.cs` —
  in-process SQLite over the real EF model: the reload, the explicit `Add` of the new `AssignedLocation`, the
  redelivery cases, and `Should_KeepClrKeyButLoseShadowKeys_When_ProviderGraphOutlivesItsScope`, which pins
  the shadow/CLR asymmetry that rules out `Attach`/`Update`.
- The detached-aggregate guard has one test per repository `AddAsync` overload (9 call sites), so a missed
  registration turns exactly one test red and names the type.
- `RetryScheduledScanner`'s three materialisation branches are pinned by tests that were red for the right
  reason first: the step stayed `RetryScheduled` after 3s with roughly one failure log entry per poll.
