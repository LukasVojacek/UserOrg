# Resource-accept path is transacted; the action link burns only on success

Status: accepted — revises ADR-0071 §Consequences (the "two separate `SaveAsync` calls" claim)
and ADR-0064 (line 26, "tokens are invalidated on use" — the resource path only)

## Context

Issue #278: accepting a `ProjectResourceAssignment` request writes the resource assignment and
the request's state change as two separate `SaveAsync` calls. ADR-0071's Consequences describe
this verbatim: *"State change and resource assignment are two separate `SaveAsync` calls sharing
one scoped `DbContext` (the UoW exposes no explicit transaction)."* A failure of the second save
(the state change) after the first has committed left the resource attached to a project while
the request stayed `PendingForPIAccept` — a split-brain between the two saves, reachable from
both accept entry points (`RequestsEndpoint.cs` PUT /Accept and the e-mailed action link via
`ServiceEndpoint.cs` → `ActionLinkService.Handle`).

v2 (the modular `Core` flow) has a parallel but different shape: `AttachResourceHandler` writes
to two separate `DbContext`s — the Core `DatabaseContext` and the projection's own
`ResourceRequestsDbContext` — with no transaction spanning them, and neither write was wrapped
for retry classification. A **crash** between the two is recovered by lease expiry and the
existing idempotent retry; an **exception** was not, because it fell through to
`RequestWorker.cs`'s permanent-failure path on the first attempt.

## Decision

### Legacy: transact the `ProjectResourceAssignment` accept path only

`ProjectRequestService.AssignResourceAndAcceptAsync` (ProjectRequestService.cs:281) wraps the
assignment save (`AssignResourceAllowingAlreadyAssignedAsync`) and the state-change save
(`request.State = Accepted` + `_uow.SaveAsync`) in one transaction, via
`IUserOrgDbContext.BeginTransactionAsync`. On any exception it rolls back
(`RollbackPreservingFailureAsync`, which never lets a rollback failure mask the original
exception) and calls `ClearChangeTracker()` — after a rollback the tracked entities still claim
to be persisted while the database has reverted them, and a later save in the same scope would
act on that stale state. This revises ADR-0071's Consequences: the two saves no longer "share one
scoped `DbContext`" with no transaction — they now commit or roll back together.

The e-mail send (`SendRequestEmailToHpcContact` / `SendAcceptedEmailToUser`) stays **outside** and
after the commit: a mail-server outage must not roll back an accept the assignment has already
granted (this is unchanged behavior, not new).

### The asymmetry: `SelfOnboarding` is deliberately NOT transacted

`ProjectRequestService`'s `SelfOnboarding` branch (around ProjectRequestService.cs:185-221) keeps
its two un-transacted saves — `AssignedUserToProjectWithRoleAsync`'s assignment write, then the
state-change save — exactly as before this ADR. This is a scoped-down fix, not an oversight:
`AssignedUserToProjectWithRoleAsync` calls the role service over HTTP (`ProjectService.cs:367`)
between the two writes. Transacting this branch would hold an open SQL transaction across that
remote call — a shape nothing else in this repo uses (`WaldurExportOrchestrator.cs:69,231,317`
wrap DB work only, never HTTP). The split-brain window this leaves — the assignment save commits,
the HTTP call or the state save then fails, and the request stays stuck pending while the user is
already an assigned project member — is the same shape #278 fixed for the resource path, and it
remains open here. It is recorded, not forgotten: **issue #281** (see Follow-up below) tracks it,
naming the option raised during planning — hoist the role lookup (`_roleListService.ListRoles`)
out of the transaction boundary so only DB writes remain inside it. `AcceptAssignRequestAsync`
already resolves roles before entering this branch on at least one call path, so the inner HTTP
call may be a duplicate lookup rather than a hard dependency; that needs confirming as part of the
follow-up, not assumed here.

### Action-link burn: unconditional on `SelfOnboarding`, conditional on success for the resource path

`ActionLinkService.Handle` no longer saves before settling the request (the old intermediate
`_uow.SaveAsync` is gone). It marks the related links deleted, calls the accept/decline logic, and
then issues one trailing `_uow.SaveAsync` after that call returns — the safety net for the paths
that settle without saving anything themselves (`DeclineAssignRequestAsync`'s early return for an
already-settled request, and an accept that matches no branch).

For the resource path, that trailing save (and the burn it carries) rides the same scoped
`DbContext` as `AssignResourceAndAcceptAsync`'s transaction. This has a consequence ADR-0064
did not anticipate: **on a rollback, the burn rolls back too.** The action link reverts to
un-burned in the database, so a PI whose accept failed can retry with the same link — arguably
better semantics than before (a failed accept no longer permanently strands the PI), but it
contradicts ADR-0064's line 26, *"Tokens are invalidated on use"*. For this path that is now
**"invalidated on successful use."**

On `SelfOnboarding`, D1 (no transaction) means the burn still flushes unconditionally alongside
the assignment save, exactly as before this change — `ActionLinkService.Handle`'s behavior did
not change per-path, only the resource path's transaction changed what "the assignment save"
means for it.

### Cache eviction stays inside the transaction

Cache eviction for the resource path (`ProjectService.cs:319-320,334-335`, inside
`AssignedProjectResourceToProjectAsync`, not itself touched by this change) stays where it is —
inside the transaction. An over-eager evict is safe on rollback (nothing is lost, only re-read
from the DB). The residual, accepted race: evict happens inside the still-open transaction,
a concurrent read repopulates the cache with pre-commit state, and only then does the transaction
commit — a narrow window between evict and commit, not a correctness gap in the accept itself.

### v2: exception classification, not a transaction

`AttachResourceHandler` deliberately does **not** grow a transaction spanning the Core
`DatabaseContext` and `ResourceRequestsDbContext` — the issue comment on #278 explicitly discourages
carrying the resource path's transactional design into v2, and WorkflowEngine's bounded step retry
plus this handler's existing idempotency (`ResourceAlreadyAssigned` treated as success) already
converge a retried step to the correct end state. What was missing was **classification**: neither
write was wrapped, so a transient `DbUpdateException`/`SqlException` fell through to
`RequestWorker.cs`'s permanent-failure path on the very first attempt instead of being retried.

Both writes are now wrapped so a non-permanent exception surfaces as `RetryableException`:
- `unitOfWork.SaveChangesAsync` via `FlowHandlerHelpers.SendOrRetryAsync` (the same idiom the
  notify handlers already use).
- `store.UpdateBySlugAsync` via an inline try/catch, because the missing-row case
  (`ResourceRequestProjectionStore.UpdateBySlugAsync` throwing for a row that does not exist) is
  a **permanent** condition — retrying it would only burn `MaxStepRetries` — and
  `FlowHandlerHelpers`'s existing two-way split cannot express this third case. The missing-row
  throw was given its own type, `ResourceRequestRowMissingException`, so the carve-out is on that
  dedicated type rather than on `InvalidOperationException` generally: SqlClient connection-pool
  exhaustion and `ObjectDisposedException`-adjacent connection failures are also
  `InvalidOperationException` and must still classify as transient.

> **Note 2026-10-01.** `FlowHandlerHelpers.SendOrRetryAsync` now lets `ResourceRequestRowMissingException`
> through unwrapped, so both `AttachResourceHandler` writes use the helper and the inline try/catch is gone.
> The classification is unchanged.

The `ResourceRequests` context's own `EnableRetryOnFailure` is a separate, lower-granularity retry
layer (EF retries the statement); this handler's wrap is a second, step-level layer on top —
deliberate, not a duplicate to fix later.

Two residual risks were identified and accepted rather than fixed in this change (recorded in
`B02.state/notes.md`):
- **Cancellation classification.** Both catch filters convert a step's own cancellation
  (`OperationCanceledException`/`TaskCanceledException`, e.g. worker shutdown) into
  `RetryableException`. Accepted: the notify handlers already behave the same way, and
  `RequestWorker.cs` calls `repo.SaveAsync(aggregate, ct)` with the same cancelled token right
  after regardless, so the outcome is unchanged.
- **Unconditional `State = Assigned` overwrite during a long retry backoff window.** The write
  pair is idempotent (a re-run hits `ResourceAlreadyAssigned`, no second Core save), so this is a
  state race, not a partial write: a concurrent cancel/expire of the projection row during the
  backoff window (up to 10 minutes) is silently overwritten back to `Assigned`. Accepted as
  out of scope; would need a guarded mutation (e.g. `if (r.State is PendingPiApproval or Assigned)`)
  the next time the projection's terminal states are touched.

## Consequences

- ADR-0071's Consequences bullet "two separate `SaveAsync` calls … the UoW exposes no explicit
  transaction" is now false for the `ProjectResourceAssignment` accept path — it holds only for
  `SelfOnboarding`, and only until the follow-up issue below closes that gap.
- ADR-0064 line 26 ("Tokens are invalidated on use") no longer holds universally — the resource
  accept path invalidates only on a **successful** accept; a rolled-back accept leaves the link
  reusable. `SelfOnboarding` is unaffected: its link still burns unconditionally, matching ADR-0064
  as written.
- The resource accept path and `SelfOnboarding` now have genuinely different failure semantics for
  the same conceptual operation (accept a request). This asymmetry is deliberate, not an
  inconsistency to "clean up" reflexively — closing it requires restructuring
  `AssignedUserToProjectWithRoleAsync` to not need a remote call inside the write, which is out of
  scope for #278. Tracked by the follow-up issue below.
- v2's fix changes no observable behavior for a caller: the same steps eventually reach the same
  end state. It changes only how a transient failure is classified internally, which is the actual
  gap #278's comment identified for this path.

## Follow-up

`SelfOnboarding` accept path remains un-transacted (split-brain between assignment and state
save) — filed as a follow-up issue, cross-referencing #278 and this ADR:
https://opencode.it4i.eu/lexis-platform/backend/userorg/-/work_items/281 (#281)
