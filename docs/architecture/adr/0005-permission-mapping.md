# ADR 0005 - Permission mapping: central endpoint→permission mapping (Forge.Core)

- Date: 2026-02-02
- Status: accepted

## Decision

Use a centralized endpoint→permission mapping stored and served via Forge.Core components. The Gateway queries `IEndpointPermissionService` to obtain required permission templates for a given path and method and uses `IPermissionMatcher` to determine whether the requestor has any required permission. `PermissionKey` is modeled as a value object with a custom JSON converter for consistent (de)serialization.

## Context

- Authorization rules must be manageable without redeploying backend services.
- The team wants a UI and API for managing roles, permissions, and endpoint mappings.

## Options considered

1. Attribute‑based authorization inside each service.
   - Pros: Localized control and visibility in service code.
   - Cons: Duplication, drift, and harder runtime changes.

2. External policy engine (OPA) for fine‑grained policies.
   - Pros: Policy as code and flexible decision points.
   - Cons: Integration and learning curve; may complicate claims mapping.

3. Centralized endpoint→permission mapping consumed by gateway (chosen).
   - Pros: Central single source of truth, changeable at runtime, manageable via UI (Forge role management feature).
   - Cons: Gateway is a critical path for auth decisions and the mapping store becomes a critical dependency.

## Rationale

Code shows the gateway invoking `IEndpointPermissionService.GetEndpointPermissionsByPathAsync()` and using `IPermissionMatcher` to evaluate required permissions (see `src/Modules/Services/Gateway/UserOrg.Gateway/Program.cs`). This model matches the goal of runtime manageability and a centralized policy surface.

## Consequences

- Ensure mapping store is highly available and protected; audit changes to mappings.
- Maintain consistent `PermissionKey` format across services; converters are available to help.

## Code references

- `src/Modules/Services/Gateway/UserOrg.Gateway/Program.cs` — endpoint permission retrieval and matcher usage.
- `src/UserOrg.Shared/PermissionKeyJsonConverter.cs` and `src/UserOrg.Shared/PermissionKey.cs`.

## Review / Notes

- Add CI/PR processes for changes to permission mappings and consider an approval workflow for sensitive mapping changes.
- Narrowed by ADR-0117: Forge.Core serves no HTTP API; the role-management console is the UI and the only admin surface.
