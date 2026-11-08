# The provider-scoped admin project list also returns the caller's own projects, defined by assignment and not by permission

## Context

`GET /api/Administration/Project` has three branches (`AdministrationEndpoint.cs:29-49`): a
platform admin gets every project unscoped, a caller carrying a `provider_id` claim gets their
provider's projects, and anyone else gets 403. The provider branch resolves a project to a
provider by a four-hop traversal — `Project → AssignedProjectResourceToProject → ProjectResource
→ AssignedLocation → Location.ProviderId` (`ProjectRepository.cs:409-414`).

A ProviderAdmin can also be assigned to projects that have no resources at all, or whose
resources belong to a different provider. Those projects satisfy no hop of the traversal, so the
endpoint hid projects the caller demonstrably belongs to — and can already list for themselves
through `GET /api/Project`.

Deciding what to add meant choosing between **two unsynchronised notions of belonging** that v1
carries side by side:

- `AssignedUserToProject` (`AssignedUserToProject.cs:7-16`) — the assignment table. This is what
  `GET /api/Project` already presents as "my projects", via
  `GetProjectsContainsUserAsync` (`ProjectRepository.cs:229-251`, predicate `:241`): a
  non-deleted assignment row **or** `Project.CreatedByUser`. It never reads a role.
- `UserPermission` (`UserPermission.cs:6-15`) — where the role lives (`SystemRoleId`, `:14`). Two properties make it
  hazardous to join naively: a row can exist role-less, because it is inserted before the role
  is assigned (`ProjectService.cs:243-257`), and every user has exactly one system-level row
  with `ProjectId = NULL` (`UserService.cs:89-96`) that an unguarded join matches for every
  project.

The request was phrased as "projects the user belongs to, in any role", which points at
`UserPermission`; the established predicate points at `AssignedUserToProject`.

## Decision

On the provider-scoped branch only, the endpoint returns the **union** of the provider traversal
and the caller's own projects, where "own" is **exactly the predicate
`GetProjectsContainsUserAsync` already uses** — a non-deleted `AssignedUserToProject` row for
the caller, or `Project.CreatedByUser` — keyed on `User.IdentityProviderUserId`, which the
endpoint already holds as `caller.IdentityProviderUserId` (`AdministrationEndpoint.cs:31`).

That caller id is `string?`, so the membership predicate is applied **only when it is present**.
An absent caller id contributes nothing to the union — the provider traversal alone decides the
result, exactly as it does today.

Membership for this endpoint is **not** derived from `UserPermission`.

The platform-admin branch and the 403 branch are unchanged, as is the RBAC mapping. This follows
the caller-at-the-endpoint, scope-in-the-handler shape of ADR-0008.

## Consequences

- **One definition of belonging platform-wide.** The admin list becomes a strict superset of
  what `GET /api/Project` returns to the same caller, so the two views cannot disagree. Verified
  against the code: the two paths use an identical predicate and identical soft-delete filters,
  share the `ProjectModel` mapper, and the admin path `Include`s strictly fewer navigations.
- **No new information is disclosed.** Every project the union adds is one the caller can
  already retrieve. This widens a result set, not an authority, and creates no enumeration
  oracle — so, unlike a scope change that adds a reachable 404, it needs no caller-scoped read
  port to collapse "missing" and "unreachable".
- **The present-caller-id guard is a contract guarantee, not a live defect fix.** A
  provider-scoped caller need not be a person: `TryGetUserIdentityProviderId` yields the `sub`
  claim only for JWT bearer
  requests and `null` for every other scheme (`AuthenticationExtensions.cs:28-34`), while an
  API-key principal for a provider service key is authenticated as `ApiKey`
  (`ApiKeyAuthenticationHandler.cs:78`) and still carries `provider_id`
  (`ApiKeyAuthenticationHandler.cs:102`) — so it reaches this branch with no caller id. The null
  arm cannot fail under any schema-valid arrangement: `Project.CreatedByUser` is marked
  `IsRequired()` (`ProjectTableConfiguration.cs:76-80`), the shadow `Guid` FK is non-nullable
  since `20231108173610_Init.cs:425,439`, and every creator is enforced by the schema, so
  `IS NULL` matches nothing. The guard's true value is against the **empty-string case**, where
  `""` passes EF's non-null check while the column is `NOT NULL` but does not validate non-empty;
  that arm is now covered by a test proven to fail under mutation. In practice, API-key principals
  are rejected by the gateway RBAC layer before reaching the handler anyway, as it demands
  `perm:service:<name>` while this endpoint carries only `perm:platform:admin` or
  `perm:provider:admin` (`AuthorizationHelpers.cs:60-72`, `roleManagmentRBAC.json:881-891`),
  making the guard strictly a contract document of how the membership predicate is keyed, not a
  protection against a reachable null.
- **Both `UserPermission` traps are structurally out of reach** — role-less rows and the
  system-level `ProjectId = NULL` row can never be consulted.
- **No RBAC and no schema change**, so this is not an RBAC-grant-plus-handler-scoping deployable
  unit (contrast ADR-0009, whose mappings needed a manual RBAC import step on `main`) and
  rollback is a plain revert.
- **A second caller must stay unaffected.** The widened overload is also called by HEAppE
  kill-switch name resolution (`Userorg.Adapter/Heappe/Userorg/UserorgService.cs:295,336`) with
  `providerId: null`. The caller-id parameter is optional and that path passes none, so its
  result is unchanged.
- **Two doc comments become false and are corrected with the change** —
  `ProjectRepository.cs:362-366` and `ProjectService.cs:86-89` both describe the scope as *only*
  projects whose resource chain resolves to the provider.
- **One behaviour visibly flips.** Filtering by `shortName` for a member project outside the
  caller's provider previously returned the filter-specific 404 (`ProjectService.cs:97-105`)
  and now returns the project. No consumer depends on that 404: the E2E tests on this route
  assert 403, and the 404-pinning unit test mocks the repository.
- **Accepted: the two tables may drift.** A user holding a `UserPermission` role on a project
  with no assignment row stays invisible. Whether they can actually drift was deliberately not
  investigated; if they can, it is a separate defect that equally affects `GET /api/Project`,
  and fixing it at the source fixes both rather than papering over it here.
- **v2 must translate this, not copy it.** The v2 module has no `Administration/Project` route
  (it is `ProviderAdmin/Projects`), scopes through `ProjectResource.ProviderId` in three hops,
  is paged, and has no "my projects" query at all — its `Caller.Scope` comes from the
  `projectRoles` claim, which is `UserPermission`-derived, i.e. the notion rejected here. That
  tension is the content of the follow-up task, not an oversight.
- **This ADR will be renumbered and relocated once.** It lands as `0015` in `docs/adr/` on
  `main`; `feat/refactor-core` keeps a single tree at `docs/architecture/adr/`, into which
  `main`'s ADRs are folded at the next free number on merge (the earlier batch became
  0070–0084, then 0086 and 0090 — that tree already reaches 0094, so `0015` is not the number
  it will carry there).
