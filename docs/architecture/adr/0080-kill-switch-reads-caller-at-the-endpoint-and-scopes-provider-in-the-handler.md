# Kill-switch reads the caller from claims at the endpoint and enforces provider scoping in the handler

## Context

The three `/api/Heappe/KillSwitch` endpoints are `perm:provider:admin` +
`perm:platform:admin` (same pair as `POST /api/Heappe/CommandTemplates`). But the
Gateway does **not** scope providers: for system roles it explicitly skips the
project-membership check and leaves scoping to the API (`AuthorizationHelpers.cs:102-113`,
mirroring ADR 0070). So a provider admin could otherwise freeze *any* provider's Location.
Scoping must be enforced by the handler:

- Provider admin → only `Location.ProviderId == caller.ProviderId`, else **403**.
- Platform admin → any Location.

Two facts shaped how the handler learns who the caller is:

- The issue proposed extending `Heappe.HostContracts.IUserorgService` with a
  `GetCallerAsync()` that reads `HttpContext`. But that adapter
  (`Userorg.Adapter.Heappe...UserorgService`) has **no HttpContext/claims access** today
  (ctor takes only `IHeappeIntegrationService`) — that route drags HTTP ambient state into
  a persistence-style adapter.
- The Heappe module's minimal-API endpoints already read claims directly via
  `IHttpContextAccessor` (`CommandTemplatePermissionsEndpoint.cs:29` uses
  `httpContext.TryGetUserIdentityProviderId`). Core's richer helpers
  (`TryGetProviderId`, `OpenIdUserInfoModel`) are `internal` to Core API and **not
  reachable** from the module; only `UserOrg.Shared.AuthenticationExtensions` is visible.
- v2 (`feat/refactor-core`) already does exactly the endpoint-boundary pattern: a `Caller`
  value record bound at the minimal-API seam via `BindAsync(HttpContext)`, passed as a
  handler parameter, with provider scoping in `ProviderWriteAuthorization.Authorize(caller,
  target, roles)` returning 403. No `IHttpContextAccessor` in the module.

## Decision

**Read the caller from claims at the KillSwitch endpoint delegate, bundle it into a small
`CallerInfo(Guid UserId, Guid? ProviderId, bool IsPlatformAdmin)`, and pass it into the
handler as a parameter. The handler enforces provider scoping. Do not add
`IUserorgService.GetCallerAsync`; do not inject `IHttpContextAccessor` into any adapter.**

1. Add one shared helper to `UserOrg.Shared.AuthenticationExtensions` (module-visible):
   read `provider_id` → `Guid?` (`AuthConstants.ProviderIdClaimType`) and is-platform-admin
   (role claim `== RoleInfoConstants.PlatformAdministratorRoleName` `"Admin"`). The IdP user
   id comes from the existing `TryGetUserIdentityProviderId`; resolve it to the internal
   `Guid UserId` the same way the module already resolves users.
2. Endpoints read the caller via `IHttpContextAccessor` (matching the existing Heappe
   endpoint), build `CallerInfo`, pass it down.
3. Handler, **precedence matters**: check `IsPlatformAdmin` FIRST → allow any Location/provider.
   A platform admin has **no** `provider_id` claim (Core injects it only for
   ProviderAdministrator), so `CallerInfo.ProviderId` is null for them — the platform-admin
   short-circuit MUST run before the ownership check, or a legitimate platform admin would 403.
   Only if not a platform admin: provider admin → `Location.ProviderId == caller.ProviderId`
   else **403** (also 403 when a provider admin's `provider_id` claim is missing — fail-closed).
   Same precedence on the provider-wide route (ADR-0082). An explicit positive test pins that a
   platform admin (null provider_id) is allowed on any location and any provider.

## Consequences

- **Legacy-consistent now, v2-portable later.** Matches how the Heappe module already reads
  claims on `main`; the later v2 port swaps only the binding seam for `Caller.BindAsync` —
  the handler + scoping logic move unchanged. The `GetCallerAsync` route would be thrown
  away at the port and couples the module to HTTP for no gain.
- **Fail-open risk is real and must be tested, not trusted.** `provider_id` is injected by
  **Core API** on `OnTokenValidated` (`AuthenticationExtensions.cs:120`); if these endpoints
  ever run in a host that doesn't inject it, scoping silently passes. Test #3 (provider admin
  on a foreign Location → 403) is mandatory and guards exactly this.
- **First explicit ownership→403 on `main`.** Legacy code scopes by passing `providerId`
  into services (null ⇒ admin) and has no resource-ownership→403 precedent (the nearest,
  `ProjectResourceAssignmentEndpoint.cs:68`, returns 400). The issue requires 403 (a state
  the caller cannot change by editing the body), so this is intentional.
- **Location resolution is fail-closed.** `Location.Name` has no unique index; if
  `(providerName, locationName)` is ambiguous, return **409** rather than guessing. The
  legacy migration is not raised to add the index (out of scope for a hotfix).
