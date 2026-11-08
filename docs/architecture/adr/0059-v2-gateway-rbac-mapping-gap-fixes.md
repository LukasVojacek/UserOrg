# v2 Gateway RBAC mapping gap fixes (location GET, reactivate perm, resource-request polling, allocation-unit-types, v2 public allow-list)

## Context

The Gateway authorizes every route through the `EndpointPermissionMap` (Forge.Core), seeded from
`roleManagmentRBAC.json`. Unmapped routes **fail closed**: `AuthorizationHelpers.AuthorizeRequestAsync`
returns 403 "no permission mapping for this endpoint" (`UserOrg.Gateway/Startup/AuthorizationHelpers.cs:29-39`).
The 2026-07-12 refactor-state analysis (issue #242) found five mapping/config defects that break shipped
v2 functionality and block the v1 retirement parity suite (F7, ADR-0055) from going green:

1. `GET /api/v2/Providers/{providerName}/Locations/{locationName}` has **no** seed entry — the seed still
   maps the flat `/api/v2/Locations/{id}` route removed by ADR-0022 nesting. v2 returns 403 where v1
   returns 200 (encoded as `StatusRemap(200→403)` in `ResourceProviderLocationByIdParityTests`).
2. `POST /api/v2/Providers/{providerName}/Reactivate` is mapped to `perm:provider:remove`, held only by
   Admin — but the endpoint is a ProviderAdmin surface (`ApiAudience.ProviderAdmin`) and its handler
   already enforces "Admin any / otherwise own provider only" (`ProviderWriteAuthorization`, ADR-0011).
   A role-bearer ProviderAdmin is gated to 403 at the Gateway before the handler can allow them.
3. `GET /api/v2/ResourceRequests/{requestId}` (status polling of the async resource-creation workflow,
   ADR-0057 / #215) has **no** seed entry at all — 403 for every caller; the 202-Accepted workflow
   cannot be polled.
4. `GET /api/v2/enums/allocation-unit-types` (added with #215) has no seed entry — 403 for every caller,
   unlike the other enum routes which are mapped with empty permission lists.
5. `GET /api/v2/Public/*` is missing from the Gateway anonymous allow-list — `PublicEndpointsOptions.GetDefaultPaths()`
   contains only the v1 `/api/Public` prefix, so anonymous v2 action-link flows get 401 where v1 gets 200
   (skipped `PublicParityTests`).

## Decision

1. **Nested location GET**: add seed entry `GET /api/v2/Providers/{providerName}/Locations/{locationName}`
   → `perm:location:list` (same permission as the flat list). Remove the stale `/api/v2/Locations/{id}` entry.
2. **Reactivate**: remap `POST /api/v2/Providers/{providerName}/Reactivate` from `perm:provider:remove` to
   **`perm:provider:update`** (held by Admin and ProviderAdmin). No role definitions change; the handler's
   own-provider scoping (ADR-0011) remains the fine-grained gate. Deactivate (`DELETE`) stays on
   `perm:provider:remove` (Admin-only) by design.
   > Amended by [ADR-0105](0105-provider-write-authority-is-the-admin-role-or-the-callers-own-provider.md) (2026-09-12): that closing Deactivate clause is superseded for the **v2** route only — `DELETE /api/v2/Providers/{providerName}` moves to `perm:provider:update` by this item's own pattern; v1 `DELETE /api/Provider/{Id}` keeps `perm:provider:remove` and stays `Admin`-only.
3. **Resource-request status polling**: add seed entry `GET /api/v2/ResourceRequests/{requestId}` →
   `["perm:resource:create", "perm:project:resource:request"]` (any-of). This admits the initiating
   ProviderAdmin and Admin (`perm:resource:create`) **and** ProjectOwner/ProjectManager
   (`perm:project:resource:request`), per the product decision that project management may observe
   resource-creation progress. The handler must not scope reads more narrowly than this set; if it does,
   it is relaxed to match.
4. **Allocation unit types**: add seed entry `GET /api/v2/enums/allocation-unit-types` → `[]`
   (authenticated-only), consistent with the other enum routes.
5. **v2 public allow-list**: add `{ Pattern = "/api/v2/Public", MatchType = Prefix }` to
   `PublicEndpointsOptions.GetDefaultPaths()`. The two v2 public routes are already seed-mapped with empty
   permission lists; only the anonymous-scheme selection was missing.

Regression pinning: seed-completeness unit tests (pattern of `OnboardingRbacMappingTests`) assert each
fixed mapping; the two parity encodings are flipped (drop the `StatusRemap` from
`ResourceProviderLocationByIdParityTests`, un-skip `PublicParityTests`).

## Consequences

- The F7 parity suite loses its two known fail-forward exceptions and asserts strict parity on both routes.
- Seed JSON edits alone do not reach deployed environments (seed loads only into an empty DB) — the additive
  startup reconciliation (ADR-0060) propagates the **new** entries; the **changed** reactivate mapping and
  the **removed** stale route require a one-off manual import (`overwriteExisting=true`) documented in the
  MR runbook.
- ProviderAdmin gains no new permission keys; reactivation authority derives from an existing key plus the
  handler's own-provider check.
