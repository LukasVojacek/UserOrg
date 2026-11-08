# 0063 — Resource-assignment request flow runs on WorkflowEngine with a Core-owned projection

Date: 2026-07-14
Status: Accepted
Issue: #251 (updates #232, defers #230)

## Context

The v2 write path for "attach a ProjectResource to a Project on request" does not exist (ADR-0009 parked the
type on legacy). Issue #232 decided a dedicated table + thin state machine + `IApprovalPolicy` seam (#230),
explicitly excluding the WorkflowEngine ("engine when a policy is genuinely multi-step"). The flow has since
become genuinely multi-step: two human/external waits (PI approval; the named resource being created at
the provider) plus attach + sync + notifications. The engine's first-class Gate/`ResumeAsync` (ADR-0014) models exactly this.

## Decision

- The request **process** is a WorkflowEngine flow (`assign-resource-to-project`, fluent DSL) — the first
  platform consumer of `Gate<TSignal>`/`ResumeAsync`. Two gates: `PiApproval` (PI decision) and
  `ResourceAvailability` (**amended by [0085](0085-resource-request-legacy-input-shape-draft-removed.md)**:
  the ADR-0065 pre-created catalog record this gate originally waited on is gone — the gate now waits on a
  matching `ProjectResource` being created at all, by the renamed `CheckResourceExistsStep`).
- Domain state names are **new** (`PendingPiApproval`, `AwaitingResourceCreation`, `Assigned`, `Rejected`,
  `Cancelled`, `Failed`) carried as strategy `CurrentState`; legacy `RequestState` 1–5 and its tables are not
  reused and will be discarded with the legacy path.
  **Amended by [0085](0085-resource-request-legacy-input-shape-draft-removed.md)**: a terminal `Expired` state
  was added (DEC-7) — the authoritative state list is `PendingPiApproval`, `AwaitingResourceCreation`,
  `Assigned`, `Rejected`, `Cancelled`, `Failed`, `Expired`.
- **Lists and detail are served by a Core-owned projection table** `ProjectResourceRequests`
  (engine step payloads are JSON blobs, not queryable). Flow step handlers update the projection row on each
  transition; the create endpoint inserts it. #232's dedicated table survives as this projection.
- Approve reuses the F5 write path (`Project.AssignResource` → `ProjectResourceAssignmentChangedEvent` →
  outbox → HEAppE sync). No new sync machinery.
- #230's `IApprovalPolicy` seam is **deferred**: future flow variability = a different flow definition/strategy
  selected per provider, introduced when a second real policy exists.

## Consequences

- Durable suspend/resume, retries, crash recovery, and 202+poll status come from the engine for free.
- Two sources of truth are avoided by ownership split: engine owns process/steps; projection owns
  list/detail/slug/tokens. The projection is rebuildable from engine state if ever needed.
- Issue #232 must be updated to this contract; #230 stays open, unblocked, later.
