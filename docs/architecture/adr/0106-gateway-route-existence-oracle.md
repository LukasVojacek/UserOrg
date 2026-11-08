# ADR 0106 - Gateway: a route-existence oracle derived from the published OpenAPI surface

- Date: 2026-09-22
- Status: accepted

## Context

The gateway proxies every path to Core through one catch-all forwarder
(`UserOrg.Gateway/Startup/UserOrgForwardingExtensions.cs:33`, `MapForwarder("/{**catch-all}")`), so it has
no unmatched-route state of its own: route existence is only ever known upstream. A path no API route
serves was therefore answered out of the gateway's own authentication and authorization stage:

- without a token, `401 auth.unauthorized` (`Startup/InternalTokenTransformExtensions.cs:62-65`);
- with a valid token, `403 auth.no_permission_mapping` (`Startup/AuthorizationHelpers.cs:54-85`), because
  the RBAC endpoint map had no entry for it.

Both are wrong for a typo'd URL, and the second is the face most clients see. Had the request been
forwarded, Core would have answered 404 (`src/Core/API/Program.cs:201`, `UseStatusCodePages` after
`MapModules`).

The obvious fix - turn the endpoint map's miss into a 404 - is the trap. That map is an independently
administered RBAC table seeded additively from `roleManagmentRBAC.json`, not derived from upstream routes,
and its own error is even named `EndpointNotFound` (`Forge.Core/.../EndpointPermissionService.cs:118`).
[ADR-0059](0059-v2-gateway-rbac-mapping-gap-fixes.md) is a catalogue of five routes that existed upstream
and were missing from that table; each was found because it produced a loud 403. Deny-by-default there is
load bearing.

## Decision

The gateway gets a **route-existence oracle of its own, separate from the RBAC map**, derived from the published
OpenAPI documents, `ApiDocuments.Published(isV1ApiEnabled)`: `Service`, `v2-User`, `v2-ProviderAdmin` and
`v2-Admin` always, `User` and `Administration` only while `V1Api` is on (amended 2026-09-30, see
[ADR-0021](0021-role-scoped-openapi-documents-v2-only.md)). Core serves them anonymously at `/api/{documentName}/openapi.json`
(`src/Core/API/Program.cs:226`, `.AllowAnonymous()`).

1. **Two answers are kept apart.** A path no document describes is `404` with
   `application/problem+json` and code `route.not_found`. A path a document does describe but the RBAC map
   has no entry for stays `403 auth.no_permission_mapping`, as loud as it is today.
2. **Loading.** `Features/RouteExistence/RouteSurfaceLoader` is a `BackgroundService` that reads every
   published document straight from Core through service discovery (`https+http://userorg`), never through the
   gateway's own proxy, and publishes an immutable `RouteSurface` into a singleton provider. It never
   blocks startup and reconciles on an interval (default 5 minutes, retry 15 seconds), the way the RBAC
   seed reconciles its own table. `RouteExistence:Enabled=false` is the kill switch.
   *(Amended 2026-09-30.)* The gateway reads `FeatureManagement:V1Api` (default `true`) once at startup to
   choose the set, so a runtime flip on the API reaches the gateway at its next restart. Both hosts must
   carry the same value; the AppHost feeds both from its one `FeatureV1Api` parameter.
3. **It fails towards today's behaviour, never towards 404, except a V1-off gateway (below).** The surface is published only when every
   listed document was read in one pass, so no request ever sees a half-loaded surface. Before the first
   successful load the surface is empty and judges nothing. Every answer other than "no route serves this"
   leaves the request exactly where it was: the oracle can only answer 404 or defer, never grant.
   *(Amended 2026-09-30.)* V1 on at the gateway and off at the API publishes nothing and logs a Warning per
   refresh. V1 off at the gateway and on at the API answers 404 for the gated legacy routes the API still
   serves.
4. **Only the documented space is judged.** A path outside `/api` is not the oracle's business, and inside
   it `/api`, `/api/scalar*.js` and `/api/{documentName}/openapi.json` are passed through, as are `/health`,
   `/alive`, the JWKS endpoints, the role-management UI and the Cyclops forwarders, which the catch-all
   never receives because they are mapped ahead of it.
5. **The `V1Api` feature flag.** A document describes the routes its process actually mapped, so with the
   flag off the gated legacy endpoints are absent from the `User` and `Administration` documents and the
   oracle reports them as not served. That is the same `404` Core's own `FeatureFlagMiddleware` answers for
   a flag-gated route, so no caller sees a different status than it does today; only the body differs
   (`route.not_found` rather than Core's). The gateway cannot do better: it has no flag-independent view of
   the legacy route surface, and inventing one from the build-time `API_*.json` snapshots would be a
   different, staler oracle. What the decision does guarantee is the fail-safe underneath it: **a space no
   document described at all is left unjudged**, so a legacy document that came back empty can never make
   the whole legacy surface look non-existent. *(Amended 2026-09-30.)* The gateway no longer relies on a flag-off legacy
   document: with `V1Api` off it does not read `User` or `Administration` at all, and the legacy space is
   judged through the `Service` document alone, which describes the ungated legacy endpoints (RemI,
   `api/version`, `api/DataStaging/*`, `api/Heappe/*`). Every other legacy path is then `404 route.not_found`.
6. **The check decides on the path alone**, not the method: a method a route does not serve keeps today's
   answer from the RBAC map rather than becoming a 404 the gateway invents.
7. **A 404 writes no audit record.** A path no route serves is not a security event and auditing it would
   fill the log with typos. This narrows what `AuthorizationHelpers.cs:70-79` records today: the
   `no_permission_mapping` audit stays for the real-route-unmapped denial, which is the case worth seeing.
8. **Where it runs.** A single request transform, `AddRouteExistenceTransform`, registered in the catch-all
   forwarder's chain ahead of `AddPermissionCheckTransform` and `AddInternalTokenTransform`
   (`UserOrgForwardingExtensions.cs:64`). Both of those transforms repeat the same unauthenticated check
   and the same `AuthorizeRequestAsync` call, and placing the oracle before both is what makes them agree
   without touching either. `AddPermissionCheckTransform` is shared with the Cyclops forwarder
   (`CyclopsForwardingExtensions.cs:146,299`), whose route-existence story is its own, and it is left
   untouched.

## Options considered

1. **404 from the RBAC endpoint map.** Two lines, and it silently hides every ADR-0059 class of gap:
   an unmapped-but-real protected route would flip 403 to 404. Rejected.
2. **Forward first and let Core 404.** Costs the gateway-side permission check
   [ADR-0001](0001-gateway-internal-token.md) requires before minting an internal token, and an
   unauthenticated request has no `sub` to mint from (`InternalTokenTransformExtensions.cs:155-163`). It
   weakens the 401 invariant directly and would need its own ADR. Rejected.
3. **A change in Core alone.** Cannot work: the gateway never forwards the request
   (`AuthorizationHelpers.cs:85`), and Core already 404s an unknown path. **No Core-side change is needed
   or made by this decision.**
4. **Per-route YARP routes instead of the catch-all.** A plausible larger answer to the same problem and a
   separate proposal; not taken here.

## Consequences

- A typo'd URL answers 404 for every caller, authenticated or not, instead of 401 or 403.
- The gateway gains a runtime dependency on Core's published documents. It is a soft one: if they cannot be
  read, nothing is published and the gateway answers as it did before this ADR. The cost is that the 404 is
  not available until the first successful load, which is the direction the decision deliberately fails in.
- A route added upstream is picked up on its own: the documents are generated from the endpoints ApiExplorer
  actually mapped, the legacy, `Service` and v2 `ShouldInclude` filters between them cover every mapped
  endpoint (`V1CoreExtensions.cs:106,119`, `V2CoreExtensions.cs:105`, where an untagged v2 endpoint defaults to the
  `User` audience), and the gateway reads the live documents rather than the checked-in `API_*.json`
  snapshots. A gateway that was already running when Core gained the route 404s it for at most one refresh
  interval.
- The one way to break that: an endpoint mapped under `/api` and deliberately kept out of OpenAPI with
  `ExcludeFromDescription`, or tagged with an `ApiAudience` no document collects. The gateway would then
  404 a route that exists. `ApiRouteDocumentationCoverageGuardrailTests` (T-191) closes that door: it boots
  the API host in-process, compares the `/api` route templates of `EndpointDataSource` against the paths the
  documents `ApiDocuments.Published(flag)` names describe, with `V1Api` on and off, and fails the build on any route described by none of them. Both ways
  in were seen to fail it against `/api/v2/Caches`. The Scalar and OpenAPI routes are served under `/api`
  and described by no document by design, so the guard exempts them by path and a second test pins that
  allowlist to routes that are really mapped, so a stale exemption cannot cover a real endpoint. That
  allowlist is wider than `RouteSurface.DocumentationSurfacePaths`: the oracle does not exempt
  `/api/favicon.svg` or `/api/{documentName}` (the Scalar per-document page), both of which it therefore
  answers 404 for. Bringing the two lists together is the follow-up.
- The authorization matrix gains a `NotFound404` outcome, and its `Allowed` branch now also rejects the
  gateway's own route-existence 404 - a plain "not 404" would be wrong there, because the matrix aims most
  Allowed-tier writes at an absent child on purpose.
- Path matching is per space, and inside a space the parameterless templates (about half the published
  surface) answer from a set lookup before any regex runs. What remains is a linear scan of compiled
  regexes, the same shape `EndpointPermissionService` already uses for the RBAC map. The surface is
  rebuilt only when the fetched documents actually changed, so a refresh on an unchanged surface compiles
  nothing.
- The template compiler is **not** shared with `EndpointPermissionService.BuildRegexFromTemplate`, though
  both turn a path template into a `Regex`. They read different languages: that one reads ASP.NET route
  templates and emits named capture groups, including `{**catchall}`, which OpenAPI never produces; this
  one reads OpenAPI path templates, where a parameter name such as `{user-id}` is legal and would make a
  named-group compiler throw. Sharing it would trade a duplication that cannot drift on this corpus for a
  failure mode that can.
- `RouteSurfaceLoader.DocumentNames` is no longer a hand-written copy of Core's document registrations
  (T-190). Both sides read `UserOrg.Shared.OpenApi.ApiDocuments.Published(isV1ApiEnabled)` (formerly `Names`), which `V1CoreExtensions`,
  `V2CoreExtensions`, Scalar's `AddDocuments` and the gateway's `PublicEndpointsOptions` also register
  from, so a name added there reaches the loader at once. The residual hole, an `AddOpenApi` call written
  with a literal rather than a name from `ApiDocuments`, is closed by
  `ApiDocumentRegistrationGuardTests`, which compares the documents the composed host actually registers
  against that list and fails on any document the gateway would not read.
- The diagnostics short circuit above the check answers `/health` and `/alive` through `ProblemResponse`
  as well (T-192), so both 404 writers in the chain now share one shape. It carries its own code,
  `diagnostics.not_served`, rather than `route.not_found`: the oracle judges only the `/api` spaces and
  answers `Unknown` outside them, so it never said these paths are unserved, and the two 404s have
  different causes. `route.not_found` is a caller's wrong path; `diagnostics.not_served` is a gateway
  process that did not map its own diagnostics endpoints, which is an ops-side fault. Nothing in the
  repository consumed the empty body: the gateway resource deliberately carries no `WithHttpHealthCheck`
  (`Tests/AppHost/UserOrg.AppHost.Tests/AppHostCompositionCharacterizationTests.cs`, where a probe on it
  would deadlock the graph), and there is no compose, Kubernetes or `HEALTHCHECK` manifest in the tree.
