# Migrating from v1 to v2 endpoints

**Audience:** API consumers (UI, DDI, HEAppE, service-to-service callers).
**Status:** v2 covers the whole v1 surface. v1 is deprecated and will be removed once parity testing and legacy stack removal finish.
**Verified against:** the committed OpenAPI snapshots at the repo root (`API_administration.json`, `API_user.json`, `API_service.json`, `API_v2-admin.json`, `API_v2-provideradmin.json`, `API_v2-user.json`) — 2026-09-30.

> **Internal companions.** This guide is the client-facing view. The engineering ledgers live in
> [`ddd-migration/v1-retirement-parity.md`](../ddd-migration/v1-retirement-parity.md) (per-route disposition,
> what still blocks deleting `src/Core`) and
> [`ddd-migration/legacy-to-refactored-endpoint-migration.md`](../ddd-migration/legacy-to-refactored-endpoint-migration.md)
> (long-form per-endpoint diffs and verification notes).

---

## The short version

| | v1 | v2 |
|---|---|---|
| Path prefix | `/api/...` | `/api/v2/...` |
| Path casing | `PascalCase` | `PascalCase` literal segments (ADR-0020, T-242); see [`v2-route-casing-map.md`](v2-route-casing-map.md) |
| Addressing | surrogate `Guid` | natural key (`projectShortName`, `providerName`, `resourceName`, `tagName`) or a generated `slug` where no natural key exists |
| Write target | often in the request body | always in the route |
| Errors | bare strings, mixed shapes | RFC 9457 `ProblemDetails` with a stable `code` |
| Enums | integers | strings |
| Lists | plain arrays | paged, with `X-Total-Count` / link headers |
| Concurrency | none | `ETag` on single-item GET, `If-Match` required on updates of mutable aggregates |
| OpenAPI | `User`, `Administration` | audience-scoped: `v2-User`, `v2-ProviderAdmin`, `v2-Admin` |

Routing is handled by the Gateway, so **the path change alone is not the breaking part** — the identifier
type, body shape, and status-code changes are.

## What changed globally

1. **Natural keys, zero surrogate GUIDs in contracts** (ADR-0022, ADR-0024, ADR-0029). Anything a client
   sends or receives is a name or a slug. GUIDs stay inside the database. Where a name is only unique per
   parent — a resource name, a location name — the route is nested under that parent
   (`/providers/{providerName}/resources/{resourceName}`, ADR-0026).
2. **Uniform errors** (ADR-0020). Every failure is `application/problem+json` with a stable machine-readable
   `code`. Do not parse error strings.
3. **Read scoping by caller tier** (ADR-0018). There are no longer separate `Administration/*` read routes:
   the same route returns more rows for an admin, provider-scoped rows for a provider identity, and
   caller-scoped rows for a plain user. Rows you may not see are hidden as `404`, not `403`.
4. **Conditional requests** (ADR-0023). Single-item GETs return a strong `ETag`; updates of mutable
   aggregates require `If-Match` and answer `412` on a stale token. See
   [Working with ETags](api-conditional-requests-etag.md).
5. **Soft-deleted rows no longer leak.** Some v2 reads legitimately return fewer rows than v1 did.
6. **Audience-scoped OpenAPI** (ADR-0021). Pick the document matching your role instead of filtering one
   large document.

## How to migrate

1. Find your endpoint in the [route map](#route-map) below and note the `What changed` column.
2. Replace GUID lookups with the natural key. If you only hold a GUID, resolve it once through the
   corresponding v1 read before switching over.
3. Move write targets out of the body into the route, and stop sending surrogate ids in bodies.
4. Handle `ProblemDetails` on every non-2xx, keying off `code`.
5. Read the paging headers instead of assuming the whole collection arrived.
6. For updates: `GET` first, keep the `ETag`, send it back as `If-Match`, retry on `412`.
7. Switch enum handling from ints to strings.

---

## Route map

Legend: `--` in the v2 column means there is deliberately no v2 route; the note says why.

### Project

| v1 | v2 | What changed |
|---|---|---|
| `GET /api/Project/{ProjectShortName}/Users` | `GET /api/v2/Projects/{projectShortName}/Users` | Members carry `IdpUserId` and the role **name**, not a `RoleId` GUID. |
| `GET /api/Project` | `GET /api/v2/Projects` | Caller-scoped list; adds `shortName`/`contactEmail`/`domainTypeName`/`startDate`/`endDate` filters and paging headers. The domain type is filtered by its **name** from `GET /api/v2/ProjectDomainTypes`, not an id; an unknown name returns an empty list, and the removed `domainId` is `400 validation.removed_parameter`. |
| `POST /api/Project` | `POST /api/v2/Projects` | `201` + `Location` header pointing at the natural-key URL; the body no longer echoes surrogate ids. |
| `PUT /api/Project` | `PUT /api/v2/Projects/{projectShortName}` | Target moves from body to route; `If-Match` required (ADR-0023). |
| `PUT /api/Project/{ProjectShortName}/User/{IdpUserId}/Assign/{RoleId}` | `PUT /api/v2/Projects/{projectShortName}/Users/{idpUserId}/Role` | `{RoleId}` GUID -> role **name** in the request body, `{ "RoleName": … }`, matched case-insensitively; an unknown name is `400 project.unknown_role`. |
| `DELETE /api/Project/{ProjectShortName}` | `DELETE /api/v2/Projects/{projectShortName}` | Same addressing; `204`. |
| `PUT /api/Project/{ProjectShortName}/ProjectResource/{ProjectResourceName}` | `PUT /api/v2/Projects/{projectShortName}/Providers/{providerName}/Resources/{resourceName}` | Provider-nested: a resource name is unique only per provider (ADR-0026). ProviderAdmin document. |
| `DELETE /api/Project/{ProjectShortName}/ProjectResource/{ProjectResourceName}` | `DELETE /api/v2/Projects/{projectShortName}/Providers/{providerName}/Resources/{resourceName}` | Provider-nested (ADR-0026). |
| `PUT /api/Project/{ProjectShortName}/User/{IdpUserId}` | `PUT /api/v2/Projects/{projectShortName}/Users/{idpUserId}` | Pluralised path only. |
| `DELETE /api/Project/{ProjectShortName}/User/{IdpUserId}` | `DELETE /api/v2/Projects/{projectShortName}/Users/{idpUserId}` | Pluralised path only. |
| `PUT /api/Project/{ProjectShortName}/ProjectTag/{ProjectTagId}` | `PUT /api/v2/Projects/{projectShortName}/Tags/{tagName}` | Tag GUID -> tag name. Creating a *new* tag while assigning is a separate call, `POST /api/v2/Projects/{projectShortName}/Tags` (ADR-0007). |
| `DELETE /api/Project/{ProjectShortName}/ProjectTag/{ProjectTagId}` | `DELETE /api/v2/Projects/{projectShortName}/Tags/{tagName}` | Tag GUID -> tag name. |
| `GET /api/Project/TemplatesInfo/{projectShortName}` | `GET /api/v2/Projects/{projectShortName}/Templates` | Project-nested; the HEAppE catalog is fetched once per URL (ADR-0038). |
| `GET /api/Project/ExtendedProjectInfo` | `GET /api/v2/Projects/{projectShortName}/Extended` | Identifier moves query -> route; returns a **single object**, not a list; soft-deleted rows no longer leak. Carries `DomainTypeName` instead of `DomainTypeId`, and `AllocationUnitType` (a name from `GET /api/v2/AllocationUnitTypes`, `NodeHours`) instead of `AllocationUnitTypeName` (`node-hours`). |
| `GET /api/Project/{ProjectShortName}/ValidRoles` | `GET /api/v2/Projects/{projectShortName}/ValidRoles` | Same path under `/api/v2`; items carry the role `Name`, `Description` and `Permissions`, no id. The `Name` is what the role change sends. |

### Project resources and assignments

| v1 | v2 | What changed |
|---|---|---|
| `GET /api/ProjectResourceAssignment` | -- | **Dropped.** The resource read already returns its assignments (ADR-0016). |
| `POST /api/ProjectResourceAssignment` | `POST /api/v2/Providers/{providerName}/Locations/{locationName}/Assignments` | Writes go through the provider aggregate (ADR-0016); an assignment is addressed by a generated `slug` (ADR-0024). `AllocationUnitTypeId` -> `AllocationUnitType`, a name from `GET /api/v2/AllocationUnitTypes`: an unknown name is `400 validation.invalid_format`, `None` is `422 provider.allocation_unit_type_not_found`. |
| `PUT /api/ProjectResourceAssignment` | `PUT /api/v2/Providers/{providerName}/Locations/{locationName}/Assignments/{slug}` | Target moves body -> route. `AllocationUnitTypeId` -> `AllocationUnitType` name, as for the create. |
| `GET /api/ProjectResourceAssignment/{Id}` | -- | **Dropped.** As above. |
| `DELETE /api/ProjectResourceAssignment/{Id}` | `DELETE /api/v2/Providers/{providerName}/Locations/{locationName}/Assignments/{slug}` | GUID -> `slug`. |
| `GET /api/ProjectResource` | `GET /api/v2/Providers/{providerName}/Resources` | Nested under the provider (ADR-0022). Read scope by tier: admin sees everything, everyone else only the resources reachable through their project scope (ADR-0018). A provider identity reads its own provider's resources on `GET /api/v2/ProviderAdmin/Providers/{providerName}/Resources`. The location type is filtered by its name, `locationType`; the removed `locationTypeId` is `400 validation.removed_parameter`. |
| `POST /api/ProjectResource` | `POST /api/v2/Providers/{providerName}/Resources` | Creates the resource and allocates it to one or more locations in one call (ADR-0043, ADR-0050). |
| `PUT /api/ProjectResource` | `PUT /api/v2/Providers/{providerName}/Resources/{resourceName}` | Name is immutable; target in route. |
| `GET /api/ProjectResource/{Id}` | `GET /api/v2/Providers/{providerName}/Resources/{resourceName}` | GUID -> `(providerName, resourceName)`. Wrong provider gives `404`. |
| `DELETE /api/ProjectResource/{Id}` | `DELETE /api/v2/Providers/{providerName}/Resources/{resourceName}` | Soft delete. |

### Project tags

| v1 | v2 | What changed |
|---|---|---|
| `GET /api/ProjectTag` | `GET /api/v2/ProjectTags` | Pluralised path. |
| `POST /api/ProjectTag` | `POST /api/v2/Projects/{projectShortName}/Tags` | **Partial.** v2 only creates a tag in the context of a project; there is no global tag-create. |
| `PUT /api/ProjectTag` | -- | **Dropped.** A v2 tag is name-only - rename is delete + recreate. |
| `GET /api/ProjectTag/{Id}` | `GET /api/v2/ProjectTags/{tagName}` | GUID -> tag name. |
| `DELETE /api/ProjectTag/{Id}` | `DELETE /api/v2/ProjectTags/{tagName}` | GUID -> name; `409` while the tag is still assigned. |

### Providers and locations

| v1 | v2 | What changed |
|---|---|---|
| `GET /api/Location` | `GET /api/v2/Locations` | Flat list kept for reads; the provider-scoped variant is `GET /api/v2/ProviderAdmin/Locations`. Items carry `Mode` as its name (`ReadOnly`, `Editable`), not an int. The location type is filtered by its name, `locationType`; the removed `locationTypeId` is `400 validation.removed_parameter`. |
| `POST /api/Location` | `POST /api/v2/Providers/{providerName}/Locations` | A location is owned by a provider; it is no longer a free-standing aggregate. |
| `PUT /api/Location` | `PUT /api/v2/Providers/{providerName}/Locations/{locationName}` | Target moves body -> route. |
| `GET /api/Location/{Id}` | `GET /api/v2/Providers/{providerName}/Locations/{locationName}` | GUID -> `(providerName, locationName)`; location names are canonicalised (ADR-0012). Carries `Mode` as its name. |
| `DELETE /api/Location/{Id}` | `DELETE /api/v2/Providers/{providerName}/Locations/{locationName}` | GUID -> natural key. |
| `GET /api/Provider` | `GET /api/v2/Providers` | Tiered read scope (ADR-0018). Items carry `CountryCode` (the `Code` of `GET /api/v2/Countries`) instead of `CountryId`, and the `countryCode` filter replaces `countryId`; an unknown code returns an empty list, and the removed `countryId` is `400 validation.removed_parameter`. |
| `POST /api/Provider` | `POST /api/v2/Providers` | Admin document. |
| `PUT /api/Provider` | `PUT /api/v2/Providers/{providerName}` | Target in route; `If-Match`. |
| `GET /api/Provider/{Id}` | `GET /api/v2/Providers/{providerName}` | GUID -> provider name. Carries `CountryCode` instead of `CountryId`. |
| `DELETE /api/Provider/{Id}` | `DELETE /api/v2/Providers/{providerName}` | Deactivation (soft). Reversible with `POST /api/v2/Providers/{providerName}/Reactivate`, new in v2. |

### Administration

| v1 | v2 | What changed |
|---|---|---|
| `GET /api/Administration/Project` | `GET /api/v2/Projects` | No separate admin route: the same route widens with the caller's tier (ADR-0018). Provider-wide list: `GET /api/v2/ProviderAdmin/Projects`. |
| `GET /api/Administration/ProjectTag` | `GET /api/v2/ProjectTags` | Tier-covered. |
| `GET /api/Administration/User` | `GET /api/v2/Users` | Admin document. |
| `GET /api/Administration/Location` | `GET /api/v2/Locations` | Tier-covered. |
| `GET /api/Administration/Location/{Id}` | `GET /api/v2/Providers/{providerName}/Locations/{locationName}` | Tier-covered; natural key. |
| `GET /api/Administration/Provider` | `GET /api/v2/Providers` | Tier-covered. |
| `GET /api/Administration/Provider/{Id}` | `GET /api/v2/Providers/{providerName}` | Tier-covered; natural key. |
| `PUT /api/Administration/PromoteToProviderAdmin` | `PUT /api/v2/Users/{idpUserId}/ProviderAdmin/{providerName}` | Both identifiers move body -> route (ADR-0032). |
| `PUT /api/Administration/ResetToPlatformUser` | `DELETE /api/v2/Users/{idpUserId}/ProviderAdmin` | Becomes a `DELETE` - it removes the provider-admin grant (ADR-0032). |
| `GET /api/Administration/Requests` | `GET /api/v2/Requests` | Tier-covered. |
| `DELETE /api/Administration/Cache` | `DELETE /api/v2/Caches` | Plural resource name. |

### Requests and public action links

| v1 | v2 | What changed |
|---|---|---|
| `GET /api/Requests` | `GET /api/v2/Requests` | Tiered read scope. |
| `POST /api/Requests` | `POST /api/v2/Requests` | Self-onboarding only on the write path (ADR-0009). |
| `GET /api/Requests/SentRequests` | `GET /api/v2/Requests/Sent` |  |
| `PUT /api/Requests/{RequestId}/Accept` | `PUT /api/v2/Requests/{slug}/Accept` | GUID -> `slug` (a request has no natural key, ADR-0024). Accept is one transaction (ADR-0019). |
| `PUT /api/Requests/{requestId}/Reject` | `PUT /api/v2/Requests/{slug}/Reject` | GUID -> `slug`. |
| `POST /api/Public/Handle/{actionLink}` | `POST /api/v2/Public/ActionLinks/{actionLink}` | Anonymous, as in v1. |
| `GET /api/Public/RequestInfo/{requestId}` | `GET /api/v2/Public/Requests/ByLink/{actionLink}` | Addressed by the action link rather than the request GUID. Anonymous. `RequestState` and `RequestType` are names (`Pending`, `ProjectResourceAssignment`), not ints. |

### User

| v1 | v2 | What changed |
|---|---|---|
| `GET /api/User/Preferences` | `GET /api/v2/Users/Me/Preferences` | `me` sub-resource. |
| `PUT /api/User/Preferences` | `PUT /api/v2/Users/Me/Preferences` | `me` sub-resource. |
| `GET /api/UserInfo` | `GET /api/v2/UserInfo` | Same path under `/api/v2`. |
| `GET /api/UserInfo/Extended` | `GET /api/v2/UserInfo/Extended` | Same path under `/api/v2`; the response drops `IsDeleted` — a deactivated caller gets 403 `user.deactivated` instead of a 200 carrying the flag (ADR-0103). `Clusters[].AllocationUnit` is the allocation unit type name (`NodeHours`), not the lookup label (`node-hours`). |

### Enums, other, version

| v1 | v2 | What changed |
|---|---|---|
| `GET /api/Enums/LocationTypes` | `GET /api/v2/LocationTypes` | As above. |
| `GET /api/Enums/RequestStates` | `GET /api/v2/RequestStates` | Enum lists are first-class resources and their values are **strings**, not ints (ADR-0020). Items are `{ Name }` only; the v1 `Id` is gone. |
| `GET /api/Enums/RequestTypes` | `GET /api/v2/RequestTypes` | As above. |
| `GET /api/Enums/OperationModeResponses` | `GET /api/v2/OperationModes` | As above. |
| `GET /api/Other/Country` | `GET /api/v2/Countries` |  |
| `GET /api/Other/Statistics` | `GET /api/v2/Statistics` |  |
| `GET /api/Other/CyclopsUsage/{ProjectShortName}/{ResourceName}` | `GET /api/Cyclops/Usage/{ProjectShortName}/{ProviderResourceName}` | **Not versioned**, but moved out of the `Other` grab-bag. Served by the Gateway as a pass-through to Cyclops and listed in the v2-User document. `GET /api/Cyclops/TotalUsage/{ResourceName}` returns the same usage aggregated across all projects. |
| `GET /api/version` | `GET /api/v2/Version` | The body is `{"Version":"…"}` JSON instead of bare text. Anonymous at the Gateway, like v1. |

### DDI service endpoints

| v1 | v2 | What changed |
|---|---|---|
| `GET /api/DataStaging/DataStagingLocations` | `GET /api/v2/DataStaging/DataStagingLocations` | **Re-homed** to the DDI module. |
| `POST /api/DataStaging/DataStagingLocations` | `POST /api/v2/DataStaging/DataStagingLocations` | **Re-homed** to the DDI module. |
| `GET /api/Service/SyncDetails` | `GET /api/v2/Service/SyncDetails` | **Re-homed** to DDI; composes reads with the role service (ADR-0042). `Locations[].AllocationUnitType` is the allocation unit type name (`NodeHours`), not the lookup label (`node-hours`). |
| `GET /api/Service/ProjectsInfo` | `GET /api/v2/Service/ProjectsInfo` | **Re-homed** to the DDI module. `Assignments[].AllocationUnitTypeName` (`node-hours`) becomes `AllocationUnitType` (`NodeHours`). |
| `GET /api/DataStaging/WorkerData` | `GET /api/v2/DataStaging/WorkerData` | **Re-homed** to the DDI module (ADR-0017); path casing preserved for its service-to-service callers. |

### HEAppE module

| v1 | v2 | What changed |
|---|---|---|
| `GET /api/Heappe/KillSwitch` | -- | **HEAppE module route** - never part of the legacy Core surface and not versioned by this migration. Published in the `Service` document. |
| `GET /api/Heappe/KillSwitch/{ProviderName}/{LocationName}` | -- | **HEAppE module route** - never part of the legacy Core surface and not versioned by this migration. Published in the `Service` document. |
| `PUT /api/Heappe/KillSwitch/{ProviderName}/{LocationName}` | -- | **HEAppE module route** - never part of the legacy Core surface and not versioned by this migration. Published in the `Service` document. |
| `PUT /api/Heappe/KillSwitch/{ProviderName}/{LocationName}/Whitelist` | -- | **HEAppE module route** - never part of the legacy Core surface and not versioned by this migration. Published in the `Service` document. |
| `PUT /api/Heappe/KillSwitch/{ProviderName}` | -- | **HEAppE module route** - never part of the legacy Core surface and not versioned by this migration. Published in the `Service` document. |
| `GET /api/Heappe/CommandTemplatePermissions/{HeappeIdentifier}` | -- | **HEAppE module route** - never part of the legacy Core surface and not versioned by this migration. Published in the `Service` document. |
| `GET /api/Heappe/CommandTemplate/{ProjectShortName}/{ProjectResourceName}` | -- | **HEAppE module route** - never part of the legacy Core surface and not versioned by this migration. Published in the `Service` document. |
| `PUT /api/Heappe/CommandTemplate/Activate` | -- | **HEAppE module route** - never part of the legacy Core surface and not versioned by this migration. Published in the `Service` document. |
| `PUT /api/Heappe/CommandTemplate/Deactivate` | -- | **HEAppE module route** - never part of the legacy Core surface and not versioned by this migration. Published in the `Service` document. |
| `POST /api/Heappe/CommandTemplate/Synchronization` | -- | **HEAppE module route** - never part of the legacy Core surface and not versioned by this migration. Published in the `Service` document. |
| `GET /api/Heappe/Partitions/{projectShortName}/{resourceName}` | -- | **HEAppE module route** - never part of the legacy Core surface and not versioned by this migration. Published in the `Service` document. |
| `POST /api/Heappe/CommandTemplates` | -- | **HEAppE module route** - never part of the legacy Core surface and not versioned by this migration. Published in the `Service` document. |
| `GET /api/Heappe/ListChangedFilesForJob/{projectShortName}/{resourceName}/{submittedJobInfoId}` | -- | **HEAppE module route** - never part of the legacy Core surface and not versioned by this migration. Published in the `Service` document. |
| `POST /api/Heappe/ResourceAudit/run` | -- | **HEAppE module route** - never part of the legacy Core surface and not versioned by this migration. Published in the `Service` document. |

## v2-only endpoints (no v1 counterpart)

| v2 | OpenAPI document | What it is |
|---|---|---|
| `GET /api/v2/ProviderAdmin/Projects` | ProviderAdmin | List projects |
| `POST /api/v2/Providers/{providerName}/Reactivate` | ProviderAdmin | Reactivate a provider |
| `GET /api/v2/ProviderAdmin/Locations` | ProviderAdmin | List locations |
| `GET /api/v2/ProviderAdmin/Providers` | ProviderAdmin | List the administered providers with their locations (ordered by `Name`), paged via `?page=&limit=` (bare array; `X-Total-Count` and `Link` headers): every provider for a platform admin, the caller's own provider for a provider identity, none otherwise. `includeInactive=true` also lists deactivated providers, flagged `IsDeleted: true`, with their non-deleted locations |
| `POST /api/v2/Providers/{providerName}/Resources/{resourceName}/Publish` | ProviderAdmin | Publish a provider's resource |
| `GET /api/v2/Projects/{projectShortName}` | User | Get project by short name |
| `POST /api/v2/Projects/{projectShortName}/OnboardingRequests` | User | Create an onboarding request |
| `PUT /api/v2/Projects/{projectShortName}/OnboardingRequests/{slug}/Accept` | User | Accept an onboarding request |
| `PUT /api/v2/Projects/{projectShortName}/OnboardingRequests/{slug}/Reject` | User | Reject an onboarding request |
| `GET /api/v2/Users/Me/OnboardingRequests` | User | List the onboarding requests the current user sent and those received on projects where the user is Owner or Manager. `Direction=Sent` or `Direction=Received` (case-sensitive) keeps one half; without it both are returned. `State` defaults to all states. `ProjectShortName` narrows both halves to one project. Each item carries `Direction` (`Sent` or `Received`). A request the user sent to a project they manage is listed once, as `Received`, and as `Sent` under `Direction=Sent` |
| `GET /api/v2/ProjectDomainTypes` | User | List project domain types |
| `GET /api/v2/AllocationUnitTypes` | User | List allocation unit type names (`[{ "Name": … }]`, no id, no `None`) |
| `GET /api/v2/AuthenticationTypes` | User | List the configured cluster authentication type names, the values `PreferredAuthenticationType` takes |
| `GET /api/v2/ResourceRequests/{requestId}` | User | Get the status of an async resource-creation request |
| `PUT /api/v2/Projects/{projectShortName}/ResourceRequests/{slug}/Cancel` | User | Cancel a resource-assignment request |
| `POST /api/v2/Projects/{projectShortName}/ResourceRequests/{slug}/ResendApproval` | User | Re-send the PI approval link for a resource-assignment request |
| `GET /api/v2/Public/ResourceRequestActions/{token}` | User | Preview a resource-request action link |
| `POST /api/v2/Public/ResourceRequestActions/{token}` | User | Settle a resource request via its action link |
| `POST /api/v2/Projects/{projectShortName}/ResourceRequests` | User | Request that a provider's resource be assigned to a project |
| `GET /api/v2/Projects/{projectShortName}/ResourceRequests` | User | List resource-assignment requests on a project |

<!-- generated from the committed OpenAPI snapshots: 71 v1 operations in User and Administration, 20 ungated legacy operations in Service, 85 v2 operations -->

---

## Endpoints that stay on v1 paths

- `GET /api/Cyclops/Usage/{ProjectShortName}/{ProviderResourceName}` and `GET /api/Cyclops/TotalUsage/{ProviderResourceName}`
  — pass-throughs to Cyclops served by the Gateway, deliberately not re-versioned; they are published in the
  `v2-User` document on these unversioned paths (the v1 path, `/api/Other/CyclopsUsage/{sn}/{res}`, is still
  served for backward compatibility).
- `/api/Heappe/*` — owned by the HEAppE module, never part of the legacy Core surface.
  This includes the kill-switch routes (`/api/Heappe/KillSwitch/...`, ADR-0077, ADR-0082) and the
  resource-validity audit (ADR-0025). They are described by the always-published `Service` document, not by
  `User` or `Administration`, and stay served with `V1Api` off.

## What `V1Api=false` hides

With `FeatureManagement:V1Api=false` on the API and the gateway (both must carry the same value; the
gateway reads it at startup):

- every v1 route mapped by the legacy Core groups answers `404`; through the gateway the body is
  `route.not_found`;
- the `User` and `Administration` documents are not registered: `/api/User/openapi.json` and
  `/api/Administration/openapi.json` answer `404` at the API, are no longer public at the gateway, and
  Scalar offers only `Service`, `v2-User`, `v2-ProviderAdmin` and `v2-Admin`.

The one exception: the gateway still serves the v1 Cyclops path `/api/Other/CyclopsUsage/{sn}/{res}`
through its own forwarder (`MapCyclopsUsageV1Forwarder`), which `V1Api` does not gate.

The ungated legacy endpoints stay served on unchanged paths and moved from `User` and `Administration` to
the `Service` document (`API_service.json`): RemI (`GET /api/remi/users`, `GET /api/remi/projects`),
`GET /api/version`, `GET` and `POST /api/DataStaging/DataStagingLocations`, `GET /api/DataStaging/WorkerData`
and the `/api/Heappe/*` routes above. `/api/Service/*` (for example `GET /api/Service/ProjectsInfo`) is a
v1 route: it stays gated by `V1Api` and is not in the `Service` document; its replacement is
`/api/v2/Service/*` in `v2-User`.

## Regenerating this document

The route tables are derived from the committed OpenAPI snapshots, so they cannot silently drift from the
build. After changing endpoints, rebuild `src/Core/API` (which rewrites the `API_*.json` snapshots) and
re-check the tables — any route missing from the mapping shows up as an **Unmapped** row.
