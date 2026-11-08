# Provider-admin locations endpoint is read-scoped and gated by perm:provider:admin

## Context

MR !276 added `GET api/v2/ProviderAdmin/Locations`
(`Core.Features.Location.GetLocationsProviderAdmin`), the headline provider-admin feature. As written:

- Its handler takes no `Caller`, resolves no scope, and calls the **parameterless**
  `ILocationQuery.GetLocationsAsync(ct)` — an unfiltered global projection of **all** locations.
- It relies solely on `.WithGroupName(ApiAudience.ProviderAdmin)`. Per this repo's convention that
  group name is **OpenAPI-documentation grouping, not authorization** — the route has **no** Gateway
  RBAC entry in `roleManagmentRBAC.json`, so it is not actually gated.
- It is also not mapped in `EndpointsExtensions`, so it does not exist at runtime yet.

The sibling non-provider-admin `GetLocations` already models the correct pattern (ADR-0018): it takes
`Caller`, resolves `ReadScope` (All / Provider / Scoped), and uses the filtered
`GetLocationsAsync(LocationFilter, page, limit, ct)` overload where `LocationFilter.ProviderName`
joins on `Provider.Name`.

## Decision

1. **Read-scope it via `ReadScope`.** The provider-admin endpoint takes `Caller` and resolves read
   scope like its sibling: a platform admin (`ReadScope.All`) sees all locations; a provider identity
   (`ReadScope.Provider`) sees only its own provider's locations, passed through
   `LocationFilter.ProviderName` into the filtered query overload. It no longer calls the unfiltered
   parameterless query.
2. **Gate it at the Gateway with `perm:provider:admin`.** Add an `endpointMappings` entry to
   `roleManagmentRBAC.json` for `pathTemplate: /api/v2/ProviderAdmin/Locations`, `httpMethod: GET`,
   `permissionKeys: [ "perm:provider:admin", "perm:platform:admin" ]` — mirroring the existing
   provider-admin routes (e.g. the Heappe `perm:provider:admin` + `perm:platform:admin` pair). This is
   the real authorization; the `ApiAudience.ProviderAdmin` group name stays only for docs.
3. **Map it.** Add `MapGetLocationsProviderAdminEndpoint()` to `EndpointsExtensions.MapCoreEndpoints`.

## Consequences

- A provider-admin can no longer enumerate every provider's locations — they see their own; platform
  admins retain full visibility. This matches ADR-0018 and the sibling endpoint's behaviour.
- Authorization is enforced at the Gateway (defense in depth: the perm key is the gate, the group name
  is cosmetic). The gateway path/perm-key matchers accept the hyphenated `provider-admin` segment
  (path matcher `[^/]+`); the perm key is static, not route-substituted, so the hyphen is safe.
- The response stays the minimal `(Name, Endpoint)` projection; scoping changes *which* rows, not the
  shape. Pinned by endpoint tests for both an admin caller (all) and a provider caller (own only).
