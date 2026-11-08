# ADR 0008 - Core owns project-role writes; the projectRoles claim stays a read-through copy

- Date: 2026-06-03
- Status: accepted
- Amended by: [ADR 0113](0113-membership-changes-reach-the-gateway-through-a-response-header.md) (2026-09-30), membership changes evict the proxying gateway's snapshot at once

## Decision

The Core domain owns the **project role** — the role a member holds within one project (ProjectOwner / ProjectManager / ProjectMember …), persisted as a `UserPermission` row scoped to that project (`ProjectId` set). The write behaviour lives on the **Project aggregate**, alongside the existing membership link (`AssignedUserToProject`). Core writes these rows through its own `DatabaseContext`.

The Gateway continues to build the **projectRoles claim** by reading the same `UserPermission` table (ADR-0006, [UserPermissionsCache.cs](../../../src/Modules/Services/Gateway/UserOrg.Gateway/Startup/UserPermissionsCache.cs)). Core therefore *writes* the project role but still *trusts the claim* for reads — the two are different reads of one shared table, reconciled at the next token mint.

## Context

- Migrating the legacy `Project` write endpoints (Slice D: assign/unassign user, edit role, create, delete) requires modelling project-role writes, which the new modular domain did not have. Legacy attached the role to `project.UserPermissions`.
- ADR-0006 made Core trust the Gateway-issued `projectRoles` claim for reads. A naive reading suggests Core should never touch project-role data at all — but the claim is *derived* state; someone must still write the source table.
- The write path and the claim are **decoupled through the shared `UserPermission` table**: Core writes it, the Gateway reads it 30 s later (permission-cache TTL) to mint the next token. No new propagation channel, event, or claim-publishing is needed.

## Considered options

- **Project aggregate (chosen).** Role is an attribute of project membership; the aggregate already holds the membership and can enforce the relative invariants (single ProjectOwner, requester level > target level) with the data in hand.
- **User aggregate.** Rejected: `User._systemPermissions` is filtered to `ProjectId IS NULL` and validated to be exactly one system role; project roles would break that invariant and conceptually belong to the project, not the user.
- **Standalone aggregate / handler port.** Rejected for now: more moving parts without a clear ownership benefit over the Project aggregate.

## Consequences

- Schema is unchanged (migrations-overlay rule). `AssignedUserToProject` (membership) and `UserPermission` (role) remain two soft-deletable tables; the Project aggregate coordinates them as **one logical unit** — an active membership has exactly one active `UserPermission` — removing the legacy drift where the two could diverge.
- Role changes are **eventually consistent** for the caller: a member whose role Core just changed sees it only after a new token is minted (bounded by the Gateway's 30 s permission cache + internal-token TTL), exactly as ADR-0006 already established for reads.
- The relative authorization rules (hierarchy, single owner, owner-not-removable) are enforced **in the Project aggregate**, because the Gateway's permission gate cannot express a rule that compares the requester's level to the target's. Role **levels** are injected into the aggregate at hydration (the mechanism `User` already uses for its role-name map); the requester's project role is read from the `projectRoles` claim.
- Populating the `AssignedByUserId` audit field re-introduces an `IdpId → UserId` lookup, but only on the **write** path — ADR-0006 removed that resolution from the read/scope path only.

## Addendum (2026-06-08) — mechanism sharpened: loaded collection, not async handler

The **ownership decision above is unchanged** — the Project aggregate owns project-role writes. What changes is *how* it touches the row. The original implementation reached into the `UserPermission` table through an async port (`IProjectRoleHandler`) called from inside the aggregate's methods (`await handler.GetActiveRoleAsync(...)`, `UpsertRoleAsync(...)`). That put async + DB I/O inside the synchronous, EF-free write model (the Axis-A P1 / D-1 cleanup).

Refinement (decided in the Axis-A continuation plan, [core-modules-target-architecture-and-plan.md](../../ddd-migration/core-modules-target-architecture-and-plan.md) §8):

- The Project aggregate hydrates the project-role rows as a **loaded collection** `_memberRoles : ICollection<UserPermission>` (FK `ProjectId`), alongside `_assignedUsers` (membership). The aggregate mutates that collection **synchronously**; `IUnitOfWork.SaveChangesAsync` persists it. `IProjectRoleHandler` is deleted.
- `UserPermission` is **reused across two aggregates**: User navigates it as `_systemPermissions` (global query filter `ProjectId == null`, exactly one system role); Project navigates it as `_memberRoles` via the `ProjectId` FK. A distinct `ProjectMemberRole` CLR type was rejected — two CLR types to one table need a TPH discriminator column = a schema change, which the migrations-overlay rule forbids.
- The global filter `ProjectId == null` **stays** (it scopes the User aggregate); the **Project repository bypasses it at hydration** with `IgnoreQueryFilters()`. This is why a future reader sees `Project` navigating `UserPermission` with `IgnoreQueryFilters()` against a filter that says `ProjectId == null` — deliberate, not a leak.
- The relative invariants (single owner, hierarchy) move from async handler lookups to **in-memory queries over `_memberRoles`**; role levels / owner-role-id are still passed in at call time (via `ProjectRoleAssignment` and parameters), so no new hydration channel is needed.

Net effect: the aggregate becomes fully synchronous and `I*Handler`-free, role invariants are unit-testable directly on the entity with a pre-populated `_memberRoles` (no mocks), and the persistence shape (two soft-deletable tables, eventual consistency with the Gateway) is unchanged.
