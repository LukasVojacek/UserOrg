# ADR 0006 - Core trusts the internal token claims for caller scope and role

- Date: 2026-06-02
- Status: accepted
- Amended by: [ADR 0113](0113-membership-changes-reach-the-gateway-through-a-response-header.md) (2026-09-30), membership changes evict the proxying gateway's snapshot at once

## Decision

The Core API treats the Gateway-issued **internal token** as the source of truth for the caller's identity, effective role, and **caller scope** (which projects the caller may see). Core reads these from the validated token claims — `sub` (`IdpId`), the `role` claim, and the `projectRoles` claim (role → project short-names) — instead of re-deriving them from the database or the Forge role service.

Core read queries filter by `IdpId` and project short-name. The previous chain `IdpId → UserId → project IDs` is removed: `GetUserIdByIdpIdAsync` and `GetUserProjectIdsAsync` are no longer used on the request path, and the duplicate `IUserOrgRolesService.GetUserPermissionsAsync` fetch (previously done both for the output-cache vary-by key and for the response body) collapses to reading the claim.

The UserInfo endpoints still call the role service to expand a role name into its full permission list — that data is not in the token.

No change is required in the Gateway: `projectRoles` and `sub` are already embedded in the internal token today.

## Context

- The Gateway already performs centralized authorization and shapes the internal token (ADR-0001), embedding `projectRoles` built from the same SQL database Core uses.
- Core endpoints were independently re-computing scope (`GetUserProjectIdsAsync`) and re-fetching permissions (`GetUserPermissionsAsync`) on every request — duplicating work the Gateway had already done and put into the token.
- Keying Core's read queries on `IdpId`/short-name (both present on every authenticated request) removes the need for the Core domain `UserId` GUID on the request path, so the domain primary key stays out of the auth boundary.

## Consequences

- `projectRoles` is currently populated only for `JwtBearerToken` auth; ApiKey/Cookie callers carry an empty claim. Any non-JWT auth method that must reach a scoped Core endpoint has to populate the claim at the Gateway, or those callers legitimately resolve to empty scope.
- Scope/role reflect the token, so a permission change is visible to Core only once a new token is issued (bounded by the Gateway's 30 s permission cache and the internal-token TTL). The Gateway's gate already enforces access, so Core trusting the token is consistent with the centralized-authorization model.
- Core's read queries must join `User` on `IdentityProviderUserId` (rather than the `UserId` GUID) where a per-caller filter is needed (e.g. requests sent by the caller).
- Removes a redundant SQL scope query and a duplicate role-service round-trip per request.
