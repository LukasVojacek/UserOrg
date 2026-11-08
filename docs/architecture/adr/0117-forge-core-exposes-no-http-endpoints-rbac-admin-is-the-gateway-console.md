# ADR 0117 - Forge.Core exposes no HTTP endpoints; RBAC administration is the gateway console

- Date: 2026-10-22
- Status: accepted
- Relates: ADR-0005 (permission mapping), ADR-0109 (audit coverage), ADR-0110 (console sign-in)

## Context

Issue #359 lists defects found while building the API map. Forge.Core carried 14 minimal-API endpoint
classes under `Features/{Permission,Role,EndpointMap,ImportExport}/Endpoints` (relative route templates
`roles`, `permissions`, `endpoints`, `export` and `import`, plus their `{role}`, `{permissionName}` and
`{endpointName}` sub-routes) plus `MapForgeImportExportEndpoints`. The gateway mapped them under `/internal`
from 6658f494 (2026-01-26) until 5d2f92cf (2026-02-05) replaced them with the role-management console. No
release tag contains that mapping, and since then no host has mapped them, so none was reachable (defect 2). The unmapped `DeletePermission` endpoint was meant to answer
`409` when a role held the permission, and #359 asked to test that branch; the branch was
dead code (defect 1). The live delete path is the gateway role-management console:
`RoleManagementService.DeletePermissionAsync` calls `PermissionService.DeletePermission`, wrapped by
`AuditingRoleManagementService`, which writes `gateway.rbac_permission.deleted` per ADR-0109.

## Decision

1. **Forge.Core serves no HTTP API.** The 14 endpoint classes and `MapForgeImportExportEndpoints` are
   deleted.
2. **The gateway role-management console is the only RBAC administration surface.** Its writes go through
   `AuditingRoleManagementService`, so every change is audited.
3. **The refusal to delete an assigned permission lives in the service.** `PermissionService.DeletePermission`
   returns `PermissionErrors.PermissionAssignedToRoles` and keeps the row; tests pin the refusal, the kept
   row and the log events `DeletedPermission` (1107) and `PermissionDeleteRefusedAssigned` (1108).

## Consequences

- The #359 acceptance item "delete answers 409" becomes a service-level `PermissionAssignedToRoles` refusal
  with a check that the permission row stays.
- `ImportExportService`, `AddForgeImportExport`, `ImportExportLogging` and `ImportSummary` lost their only
  consumers and are deleted. `IForgeImportExport` and its implementations stay; the gateway
  `RoleManagementService` is their only consumer.
- The `/internal/*` requests in `UserOrg.Gateway.http` are removed.
- A future HTTP API for RBAC needs a new decision covering authorization and audit.

## Alternatives rejected

- **Map the endpoints in the gateway behind RBAC**: a second write path that bypasses
  `AuditingRoleManagementService`, so RBAC changes would go unaudited.
- **Keep the classes unmapped**: dead code that the API map and readers take for a live surface.
