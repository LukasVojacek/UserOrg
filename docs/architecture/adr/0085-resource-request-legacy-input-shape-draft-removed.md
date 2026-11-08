# 0085 — Resource-assignment request returns to the legacy input shape; Draft catalog record removed

Date: 2026-07-21
Status: Accepted
Issue: #251
Supersedes / amends: supersedes [0065](0065-draft-projectresource-lifecycle.md); amends
[0063](0063-resource-assignment-request-flow-on-workflowengine.md),
[0064](0064-pi-approval-authority-anonymous-action-link.md),
[0066](0066-resource-request-slug-scheme.md), [0067](0067-workflow-step-claim-invariant.md); narrows
[0018](0018-read-scope-tiers.md)

## Context

0065 required a catalog record to exist before a request could reference it: a ProviderAdmin
pre-created a `Draft` `ProjectResource`, requests could only name an existing row, and the PI
address was read off that record. The product decision reverses this: the requester (an
Owner/Manager of the project) supplies `ResourceName` as free text — it need not exist — and
supplies `PrimaryInvestigatorEmail` directly, the legacy shape. `POST
providers/{p}/resource-drafts`, `ProjectResource.CreateDraft`/`Materialize`, and the
`ResourceAvailability` column are removed outright; nothing replaces them.

The engine flow (0063) and the anonymous PI action link (0064) are kept. Variant B keeps both
gates: the PI approves first regardless of whether the resource exists; only after approval, if
it does not exist, the provider's **support** e-mail (not the Draft-materialization path) becomes
the channel, and the request parks in `AwaitingResourceCreation` until a matching
`ProjectResource` is created through the normal `POST Providers/{p}/Resources` path.

## Decision

- **Request contract**: `POST Projects/{p}/ResourceRequests` accepts
  `{ProviderName, ResourceName, PrimaryInvestigatorEmail}`. `ResourceName` is free text and is
  not checked against `ProjectResource` at creation time. The provider must have
  `SupportEmail` set, or the request is refused with 422 at creation, before any gate work starts
  (DEC-5) — a resource that does not exist yet has nowhere else to route the notification.
- **Flow (Variant B)**: PI approval is the first gate, unconditional on resource existence. After
  approval: if the resource already exists, the request goes straight to `Assigned` (unchanged
  from before this change); if it does not, `CheckResourceExistsStep` (renamed from
  `CheckResourceOperationalStep` — the question is no longer "Draft or Active?", it is "does a row
  exist at all?") notifies the provider's support address and parks the request at
  `AwaitingResourceCreation`. `ProjectResource.Create` (not only `Materialize`, which no longer
  exists) now raises `ProjectResourceActivatedEvent`, which is what wakes a parked request when a
  matching resource is later created.
- **Draft catalog record removed**: `ResourceAvailability`, `POST providers/{p}/resource-drafts`,
  `CreateDraft`, `Materialize`, and the materialize-instead-of-409 branch in `POST
  providers/{p}/resources` are deleted. Creating a resource whose name collides with an existing
  one at the same provider is a plain 409, as it was before 0065.

### Accepted risk 1 (DEC-8) — requester-supplied PI address enables self-approval

The requester supplies `PrimaryInvestigatorEmail` directly (`CreateResourceRequestEndpoint.cs`).
Nothing ties that address to the actual resource owner. A requester who is an Owner/Manager of
the project can supply their own address, receive the anonymous action-link e-mail (0064), and
approve their own request. **A project role is sufficient to attach a provider's resource to
that project** — there is no independent verification of PI identity. This is a deliberate,
developer-accepted trade (legacy behaved identically), not an oversight, and it is not mitigated
by this change. Recorded here so it is not later rediscovered and "fixed" without a decision to
change it.

### Accepted risk 2 — the legacy v1 create path is a silent-stranding vector

Legacy v1 `POST` (`src/Core/Application/UserOrg/Services/ProjectResourceService.cs:172-175`,
routed at `src/Core/API/API/ProjectResourceEndpoint.cs:94`) inserts `ProjectResource` rows
through the monolith's own EF entity (`obj.ConvertToEntity()` →
`ProjectResourceRepository.InsertAsync`), never through `Core.Domain.ProjectResource.Create`.
Such an insert raises no domain event and writes no outbox row. Before this change that was
harmless — the Draft `Materialize()` path was equally v2-only and coexisted with it. After Draft
removal, `Core.Domain.ProjectResource.Create` is the **only** wake mechanism for a request parked
at `AwaitingResourceCreation`, so the legacy v1 endpoint is now the sole remaining way a resource
can come into existence without waking the requests parked on its name — silently stranding them.
Not fixed here (the v1 path is legacy and slated for removal); recorded as an accepted risk
alongside DEC-8.

### The deliberate provider-existence narrowing (ADR-0018)

`CreateResourceRequestEndpoint.cs` resolves the named provider through the caller-scoped
`IProviderQuery.GetProviderDetailsAsync(provider, [.. caller.Scope], ct)`, not through the
unscoped write repository. A private provider the caller cannot reach therefore resolves to the
same `provider.not_found` 404 as one that genuinely does not exist, matching what `GET
providers/{name}` already reports to the same caller (ADR-0018 anti-leak). This narrows behaviour
versus the pre-existing endpoint, which accepted the request as long as *any* `ProjectResource`
row existed, regardless of the caller's reach into the provider. The first implementation of this
endpoint shipped the unscoped lookup instead and was found, on review, to create an enumeration
oracle — a caller could distinguish "provider does not exist" from "provider exists but I cannot
reach it" by trying to create a request against it. Recorded so the scoped lookup is not later
"simplified" back to the unscoped one.

### Renaming a workflow step type is a data-compatibility change (standing constraint)

This flow surfaced a general property of WorkflowEngine, not specific to this change: **renaming
a step type is a data-compatibility change, and the failure mode is silent.** Two concrete
instances from `CheckResourceOperationalStep` → `CheckResourceExistsStep`:

- `RequestWorker.cs:122` persists `message.GetType().AssemblyQualifiedName` on every step start;
  `RetryHandler.cs:132` and `RetryScheduledScanner.cs:87` both rehydrate it with
  `Type.GetType(step.MessageType)`. Renaming the step type changes the AQN, so any historical row
  left `Failed`/`RetryScheduled` under the old type name becomes permanently unresolvable —
  `RetryHandler` throws, and `RetryScheduledScanner` **degrades silently**: it logs an error and
  releases the lease rather than surfacing the failure anywhere a human would see it.
- The step's result type was itself renamed for clarity in the same change:
  `CheckResourceExistsResult.Exists` → `IsAssignable` (the old name conflated "the row exists" with
  "the row is `Active`" — a `Draft` row existed but reported `false`). That result is
  JSON-serialized into the engine's persisted `StepResult`. A `StepResult` written before the
  rename and replayed after it during crash recovery deserializes with the old property absent,
  defaulting the new one to `false` — parking a request that should have attached.

- The step's persisted `StateName` label changed with it (`"CheckResourceOperational"` →
  `"CheckResourceExists"`, `AssignResourceFlowSteps.cs:15`), but that label is **inert as a routing
  key**: the flow dispatches on the step's *type* (`.From<CheckResourceExistsStep>()`,
  `AssignResourceFlow.cs:65`; `RequestWorker.cs:155`), and a parked request resumes on its **gate
  name** held in `CurrentState` (`RequestAggregate.MarkAwaitingSignal:236`), neither of which the
  rename touches. Its only reach is the replay lookups keyed by name
  (`IsStepCompleted`/`GetStepResult`, `RequestAggregate.cs:312,319`, read at `RequestWorker.cs:94`):
  a step row written under the old label would not be recognised as completed and would re-execute,
  rather than resolve to something wrong. Recorded so the label is not mistaken for the dangerous
  value here — the AQN and the result contract above are.

Both were safe to make here **only because this flow has never been deployed** — it is absent
from `origin/main` entirely, so no historical rows exist to break. A future rename of a step type
or its result contract, once this flow is live, needs either a data migration for in-flight rows
or a tolerant read path; it must not be assumed free by analogy to this change.

### Expiry (DEC-7) applies to both gates

Parked requests now expire: a new terminal `Expired` state, default 30 days, configurable,
measured from when the row entered its current non-terminal state (`UpdatedAt`), applied
identically to `PendingPiApproval` and `AwaitingResourceCreation`. A poll-loop sweeper finds rows
past expiry and, for the normal case, resumes them through the existing cancel signal path with a
flag that makes `InvalidateLinksHandler` record `Expired` instead of `Cancelled`; rows the engine
cannot resume — orphaned (no workflow ever started) or unknown to the engine
(`ResumeResult.RequestNotFound`) — are instead settled by a direct projection write, mirroring
what `CancelResourceRequestEndpoint` already does for the same case
(`ResourceRequestExpirySweeper.ExpireOneAsync`, `HandleResumeOutcomeAsync` and `SettleOrphanAsync`). This was not designed to interact with
resending the PI approval e-mail, but it does:
`ResendApprovalEmailEndpoint` calls `UpdateBySlugAsync`, which unconditionally bumps `UpdatedAt`
— the same column the sweeper's cutoff is measured from — so every resend restarts the 30-day
clock. Observed, not designed; documented here because it is not obvious from either code path in
isolation.

### Expiry also settles engine-finished requests, not only timed-out ones (amendment)

The sweeper snapshots due rows `AsNoTracking`, so between that read and `ResumeAsync` the flow can
legitimately finish the request on its own. `ResumeOutcome.NotAwaiting` therefore does not mean
"benign lost race, leave it" — it conflates a live request with a finished or diverged one. The
sweeper resolves the ambiguity with one extra round trip, `IWorkflowClient.GetStatusAsync`, and
branches on all five `RequestStatus` members plus `null`
(`ResourceRequestExpirySweeper.HandleNotAwaitingAsync`):

| `RequestStatus?` | verdict |
|---|---|
| `null` | settled like an orphan — `Expired` (same remedy as `ResumeResult.RequestNotFound`) |
| `Failed` | settled — projection `Failed` (see next section) |
| `Completed` | not settled from the snapshot; re-read via `GetBySlugAsync` first — the snapshot may already be stale because a concurrent PI click completed the flow legitimately in the gap; only a still-non-terminal fresh read is reported as a divergence |
| `AwaitingSignal` | left — parked, but at a gate the sweeper cannot resolve; deliberately wedged (below) |
| `Accepted` / `InProgress` | left — alive by the engine's own account; deliberately wedged (below) |

### `ResourceRequestState.Failed` gains its first writer (amendment)

`Failed` was declared on `ResourceRequestState` (`ResourceRequestState.cs:29-30`) but nothing wrote
it. The sweeper's `RequestStatus.Failed` arm above is now the sole writer: the workflow engine
reports the flow's step failed permanently (a permanent exception or exhausted retry budget), and
the sweeper settles the projection to `Failed` through the same guarded mutation used for `Expired`
(the `RequestStatus.Failed` arm of `ResourceRequestExpirySweeper.HandleNotAwaitingAsync`, and `SettleAsync`). This keeps the three terminal-by-the-system
states distinguishable by cause: `Failed` — the engine could not complete the flow; `Expired` — the
row timed out with no engine failure; `Cancelled` — an owner/manager acted. `Failed` was already
listed in 0066's terminal-state enumeration (where `Expired`, not `Failed`, is the entry attributed
to this ADR); it did not need a corresponding
amendment there — only the writer was missing, not the state.

### Settling `Failed` releases the slot — a settle-and-forget position, conditional on retry staying unwired (amendment)

Settling a row to `Failed` makes it terminal, which drops it out of the single-active index's
allow-list (see below) and releases the slot for its `(project, provider, resource)` triple exactly
as `Expired` does — a new request for the same triple can be created immediately. This is safe today
only because a `Failed` engine request is never revived: `IWorkflowClient.RetryAsync`
(`IWorkflowClient.cs:37`), `IRequestApplicationService.RetryLastStepAsync`
(`IRequestApplicationService.cs:83`), and `MarkRequestFailedAsync`
(`IRequestApplicationService.cs:93`) have no caller anywhere in the codebase outside their own
internal delegation inside `WorkflowEngine.Core` (`WorkflowClient.cs:157` calling
`RequestApplicationService.cs:105`) — not in `Core`, not in `src/WorkflowEngine.Sample`, not behind
any endpoint. If an operator-retry tool is ever wired to one of these, that safety breaks: a
resurrected request could resume through the flow's own unguarded write handlers
(the `Assigned` write of `AttachResourceHandler.HandleAsync`, the `PendingPiApproval` write of `SendPiApprovalEmailHandler.HandleAsync`) and write `Assigned` for its old
slug after a new request for the same triple has already taken the slot. Before wiring such a tool,
re-examine whether slot-release-on-`Failed` still holds, or whether a resumed request must reclaim or
verify the slot for itself.

### Two classes stay knowingly wedged (amendment)

Settling either class below would be wrong, not merely inconvenient, so the sweeper leaves both and
only logs:

- **A request the engine still reports as running** (`Accepted` / `InProgress`, unchanged since long
  before the cutoff). Settling it would terminate work the engine believes is live. Telling a
  genuinely stalled workflow apart from one that is merely slow needs a progress signal the engine
  does not expose today; there is nothing to test against.
- **A gate the engine cannot route** (`AwaitingSignal` on a `NotAwaiting` resume). Either the
  projection and the flow disagree about which gate the request is parked at, or the request was
  still `InProgress` at `ResumeAsync` and parked in the gap before the status read — a benign race.
  Nothing in the observation tells the two apart, so settling would guess, and the sweeper logs a
  warning rather than an error: this is not yet "something is wrong"
  (the `RequestStatus.AwaitingSignal` arm of `ResourceRequestExpirySweeper.HandleNotAwaitingAsync`).

Both keep the single-active slot occupied for their (project, provider, resource) triple until the
engine moves or an operator intervenes.

### The slot can still be held for up to 30 days after the engine fails (amendment)

The sweeper only inspects rows already past `FindDueForExpiryAsync`'s cutoff — `UpdatedAt` older
than `DurationInDays` (default 30, DEC-7). A request the engine failed the moment it started still
occupies its single-active slot for up to that long before the next poll finds it due and settles it
to `Failed`. This bounds the wedge to a known ceiling *per resend*: because a resend restarts the
clock on the same column (see above), the ceiling is `DurationInDays` after the last touch, not after
the failure. It does not make settlement prompt.

### The settle is guarded against overwriting an already-terminal row (amendment)

`ProjectResourceRequest` carries no rowversion / concurrency token
(`Core.DataAccess/ResourceRequests/ProjectResourceRequest.cs:10-50`), and
`ResourceRequestProjectionStore.UpdateBySlugAsync` is an unguarded load → mutate →
`SaveChangesAsync` (`:53-64`). The sweeper's status read (`GetStatusAsync`) and its write are
separate `await`s with a DB round trip between them, so a concurrent legitimate terminal write made
by the flow itself in that gap (e.g. `AttachResourceHandler` writing `Assigned`) can land there. Both
settle paths — `Expired` and the new `Failed` — now go through a mutation delegate that no-ops when
the freshly loaded row is already terminal (`ResourceRequestExpirySweeper.SettleAsync`), so the
sweeper never clobbers a row the flow already settled.

This closes the application-level window only. What remains is the read-committed gap between the
`SELECT` and the `UPDATE` inside a single `SaveChangesAsync` — only a rowversion on the projection
would close that, and adding one is a schema/migration change deliberately out of this amendment's
scope.

### Polling, not a push callback, drives this design ([#271](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/work_items/271)) (amendment)

`ICallbackPort` fires only on the completion path, despite its documented contract having promised
"Completed or Failed" — a request the engine fails permanently notifies no callback consumer.
Because that push channel cannot be relied on for `Failed`, the sweeper (and any other consumer
that needs to observe a failure) must poll `IWorkflowClient.GetStatusAsync` instead. Fixing
`ICallbackPort` itself is tracked separately in #271, not part of this change.

### The single-active index needed no migration for `Expired`

The filtered unique index enforcing "at most one non-terminal request per (project, provider,
resource)" (`ResourceRequestsDbContext` migration `20260714103713_Init.cs:48`) is written as an
**allow-list of the two non-terminal states**
(`filter: "[State] IN (N'PendingPiApproval', N'AwaitingResourceCreation')"`), not a block-list of
terminal ones. A new terminal state — `Expired` — therefore falls outside the filter
automatically and releases the single-active slot without a migration. Recorded because the
inverse (a block-list) would have silently kept expired rows occupying the slot.

## Consequences

- The request-time existence check on the catalog and the Draft intake workflow are gone; the
  ADR-0065 catalog-intake model does not exist in the shipped system.
- Two accepted risks now stand in the design rather than being mitigated: requester-supplied PI
  self-approval (DEC-8), and the legacy v1 create path's silent-stranding of parked requests.
  Both require a follow-up decision, not a code fix, to close.
- `ResourceName` being free text means `cluster-x` and `Cluster-X` can be two distinct active
  requests for one intent (0066); accepted, unchanged by this ADR.
- A future rename of a workflow step type or its serialized result, once this flow carries live
  traffic, is a data-compatibility change requiring explicit handling — not a mechanical refactor.
- DEC-6 shipped as a self-checking migration rather than a pre-migration verify-and-stop query:
  `20260721115528_DropProjectResourceAvailability.cs:27-29` raises `THROW 51000` inside EF's
  per-migration transaction when any row still has `Availability <> 0`, rolling back instead of
  dropping. This is necessary, not incidental — modular migrations apply unattended at startup via
  `Database.MigrateAsync()` under `Database:AutoMigrateOnStartup`
  (`CoreDatabaseMigrationStartupService.cs:26-30`), so a comment or a runbook step would not have
  stopped anything; the check had to live in the migration itself. The "stop and re-decide" moment
  therefore moved from planning time to deploy time.
