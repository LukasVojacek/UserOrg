# Single request-accept code path shared by UI and e-mail

## Context

A `ProjectResourceAssignment` request can be accepted two ways: by the Principal Investigator in the authenticated UI (`PUT /Requests/{id}/Accept`) or via an e-mailed action link (`POST /api/Public/Handle/{actionLink}`). Historically each path called a *different* `ProjectRequestService.AcceptAssignRequestAsync` overload, and the two overloads had diverged. The UI overload was missing the branch for `{ ProjectResourceAssignment, ProjectResource: not null, PendingForPIAccept }` — the most common case — so accepting such a request changed nothing, `SaveAsync` returned 0, and `DatabaseCommitException.ThrowIfLessThan(1, …)` surfaced as a 500 `{"errors":"changes was less then 1"}` (issue #211).

## Decision

There is **one** accept logic. The two public overloads remain only because the e-mail path is unauthenticated and has no `assignedByUserId` (needed solely for `SelfOnboarding`); both delegate to a shared private core for `ProjectResourceAssignment`. The actual resource→project attachment is owned by `ProjectService.AssignedProjectResourceToProjectAsync` (single source of truth, incl. read-only and non-shared-resource validation) rather than re-implemented inline. The endpoint no longer performs its own assignment and no longer reads the request's pre-accept state; the service returns the resulting `ERequestState`, which the endpoint maps to its response message.

## Consequences

- `PendingForHPCProject` is confirmed to mean "PI approved, but the requested resource does not yet exist" — not a separate human HPC approval step. A request with an existing resource therefore goes straight to `Accepted` on PI accept.
- State change and resource assignment are two separate `SaveAsync` calls sharing one scoped `DbContext` (the UoW exposes no explicit transaction). Assignment runs first; if it fails the request stays pending and the PI can retry.
- Reusing `AssignedProjectResourceToProjectAsync` means its validations (read-only project, non-shared-resource conflict) now apply to **both** accept paths — UI and e-mail action link — where the e-mail path previously skipped them. A failed assignment propagates and leaves the request un-accepted (assignment runs before the state change), so the PI can retry. Pinned by `ProjectRequestServiceTests`.
