# Legacy → Refactored Endpoint Migration Map

> **Client-facing guide:** consumers migrating their calls should read
> [Migrating from v1 to v2 endpoints](../guides/v1-to-v2-migration.md) — a normalized route map
> generated from the committed OpenAPI snapshots. This document is the internal ledger behind it.

**Status:** Living document — updated as the migration progresses
**Last verified against code:** 2026-07-13 — every legacy Core route is now ported, re-homed, or consciously dropped. Authoritative coverage ledger: [v1-retirement-parity.md](./v1-retirement-parity.md).
**Scope:** Maps every legacy Core API endpoint to its refactored (v2) counterpart, with a concise breaking-change summary.

---

## Purpose

The Core API is mid-migration from the legacy monolith (`src/Core`) to the new
modular core (`src/Modules/Core`). This document is the single source of truth for
**where each legacy endpoint went** and **what changed in a way that breaks
clients**.

Related reading:

- [DDD Migration Index](./README.md)
- [Target architecture & plan](./core-modules-target-architecture-and-plan.md)
- [API versioning via Gateway](./api-versioning-gateway-routing.md)

---

## Conventions

| Aspect | Legacy | Refactored |
|---|---|---|
| Route prefix | `api/...` (mapped via `app.MapModules()`) | `v2/...` (mapped via `app.MapCoreEndpoints()`, see `src/Modules/Core/Composition/EndpointsExtensions.cs`) |
| Style | Mixed: minimal-API lambdas grouped in `src/Core/API/API/*Endpoint.cs` | Minimal API, one endpoint per file under `src/Modules/Core/Features/<Feature>/...` |
| Identifiers | Often raw `Guid` (`{Id:GUID}`) | Prefer natural IDs (e.g. `ShortName`, `ProviderName`) |

### What counts as a breaking change here

Per the agreed definition, **the endpoint path itself is NOT a breaking change**
(the Gateway handles routing). A breaking change *is*:

- a change to an **identifier type** (e.g. `Guid` → natural name),
- a **renamed / retyped / removed** route or query parameter,
- a change to the **request or response shape** (fields added/removed/renamed/retyped),
- a change to **status codes** or **observable behavior**.

### Status legend

- ✅ **Migrated** — a v2 endpoint exists.
- ⏳ **Not yet migrated** — only the legacy endpoint exists.

> **Migration coverage is complete.** Every legacy Core route is ported to v2, re-homed to another
> module (Heappe / DDI), or consciously dropped with a rationale. This includes the once-pending
> ProjectResource CRUD (F4), resource↔project assignment (F5), ProjectTag delete (F1), admin role
> management (F3), Administration reads (F0.3), cache reset (F2), DataStaging / Service (DDI), and the
> Heappe file download (B1). The per-route disposition ledger is
> [v1-retirement-parity.md](./v1-retirement-parity.md). What remains before v1 can be deleted is
> **automated v1↔v2 parity testing and legacy stack removal**, not endpoint coverage.

---

## Project

| Legacy (method + path) | Refactored (method + path) | Status | Breaking change summary |
|---|---|---|---|
| `GET api/Project` | `GET v2/Projects` | ✅ | Response shape differs: legacy `List<ProjectModel>` → v2 list of `{ShortName, Name, Email?, ExternalId?, Mode, ServiceName, StartDate, EndDate}`. Extra query filters in v2 (`shortName`, `contactEmail`, `domainTypeName`, `startDate`, `endDate`). **Caller-scoped**: returns only projects the authenticated user has a project role on (resolved from token; `401` invalid token, `404` unknown user) — like legacy, but without admin-specific logic. |
| `GET api/Project/ExtendedProjectInfo?ProjectShortName=` | `GET v2/Projects/{projectShortName}/Extended` | ✅ | Identifier moved from query to route; **single object** instead of `List<ExtendedProjectModel>`; response reshaped (`AssignedTags: Guid[]`, `AssignedResources[...]`). Can return `400`/`404`. **Intended behavior difference**: v2 excludes soft-deleted resources/locations (DbContext-wide `!IsDeleted` default), so it may return fewer rows than legacy, which leaked soft-deleted rows. |
| `GET api/Project/{ProjectShortName}/Users` | `GET v2/Projects/{projectShortName}/Users` | ✅ | Response field set differs: v2 returns `{IdpUserId, Email, UserName, RoleName}` (vs legacy `ProjectUserModel`). |
| `GET api/Project/TemplatesInfo/{projectShortName}` | `GET v2/Projects/{projectShortName}/Templates` | ✅ | **Requires `X-Original-Authorization` header** (Keycloak bearer) for the HEAppE call. Response reshaped to `{IsAvailable, ClusterName, LocationId, ClusterId?, Nodes[...]}`. Output cache is suppressed on HEAppE failures. |
| `GET api/Project/{ProjectShortName}/ValidRoles` | `GET v2/Projects/{projectShortName}/ValidRoles` | ✅ | Lists the project roles the caller may assign to others (strictly below the caller's level, no lower than the member baseline). Response reshaped to `{Name, Id, Description, Permissions[]}` — **the permissions field is renamed `SystemPermissions` → `Permissions` (verified live)**, same role set as legacy otherwise. Caller's project roles are read from the `projectRoles` claim (no DB lookup of the caller). `404` out of scope. |
| `POST api/Project` *(create)* | `POST v2/Projects` | ✅ | Creates the project **and** grants the creator the project owner role (single owner). Body `{ShortName, Name, Description?, ContactEmail?, StartDate, EndDate, DomainTypeName}` — **domain type by name** (string), not `DomainId` (int); **`Description` is now first-class** on create. Returns `201 Created` with a `v2/Projects/{shortName}` location (legacy also `201` but via `CreatedAtRoute`). `400` on validation / unknown domain type; `409` when the short name already exists. Offset-aware dates (UTC instant), like v2 update. **Eventual consistency (verified live):** the creator's owner role lands in the `projectRoles` claim only after the Gateway's permission-cache TTL (~30 s), so an immediate `GET v2/Projects/{shortName}` by the creator returns `404` (out of scope) until the next token mint — see [ADR-0008](../architecture/adr/0008-core-owns-project-role-writes.md). Applies to all project-role writes (assign/unassign/edit role). |
| `DELETE api/Project/{ProjectShortName}` *(delete)* | `DELETE v2/Projects/{projectShortName}` | ✅ | Soft-deletes the project and **cascade soft-deletes every project role** (`UserPermission`) on it; evicts the affected users' UserInfo caches. Returns `204`; `404` out of scope / not found; `409` when read-only. |
| `PUT api/Project/{ProjectShortName}/User/{IdpUserId}` *(assign user)* | `PUT v2/Projects/{projectShortName}/Users/{idpUserId}` | ✅ | Assigns the user as a project **member** (membership + project role, coordinated as one unit). Idempotent reactivation of a previously removed user. **Status changed**: legacy `200 OK` + `"LEXIS User was assigned…"` → v2 `204`. `404` out of scope / unknown user; `409` already an active member or read-only. |
| `DELETE api/Project/{ProjectShortName}/User/{IdpUserId}` *(unassign user)* | `DELETE v2/Projects/{projectShortName}/Users/{idpUserId}` | ✅ | Removes membership **and** project role together. **The project owner cannot be removed** (`409`). Returns `204`; `404` out of scope / not an active member; `409` read-only. (Legacy's `roleId ∈ 2..5` requester guard is replaced by Gateway permission gating.) |
| `PUT api/Project/{ProjectShortName}/User/{IdpUserId}/Assign/{RoleId}` *(edit role)* | `PUT v2/Projects/{projectShortName}/Users/{idpUserId}/Role` | ✅ | Changes a member's project role. **REST naming: the role id moved from the route into the request body** `{ roleId }` — the membership holds exactly one role, so a singleton `/role` sub-resource is modelled instead of a `/roles/{roleId}` collection (legacy `/Assign/{RoleId}`). Enforces the relative **role hierarchy** (strictly below the caller's level, no lower than the member baseline → `403`) and the **single-owner** invariant (`409`), both in the Project aggregate. **Status changed**: legacy `200 OK` + text → v2 `204`. `400` unknown role; `404` out of scope; `409` read-only. |
| `GET api/Project/{ProjectShortName}` *(implicit single get)* | `GET v2/Projects/{projectShortName}` | ✅ | New explicit single-get. Response `{ShortName, Name, Description, Email?, ExternalId?, Mode, ServiceName, StartDate, EndDate, DomainType, IsDeleted, Tags[], Users[], Resources[]}`. Returns `404` when not found. |
| `PUT api/Project` *(update; ShortName in body)* | `PUT v2/Projects/{projectShortName}` | ✅ | **`ShortName` is now immutable** — it is the route key and cannot be changed (legacy update could rewrite it). **Domain type is now addressed by name** (`DomainTypeName`, string) instead of `DomainId` (int). Body `{Name, Description?, ContactEmail?, StartDate, EndDate, DomainTypeName}`. **Success response changed**: legacy returns `200 OK` with the text body `"LEXIS Project was modified."`; v2 returns `204 No Content` (empty). `400` on validation / unknown `DomainTypeName`; `404` out of caller scope or not found; `409` when the project is per-project read-only. Caller-scoped (`404` if the project is not in the caller's scope). **Date/offset semantics differ (verified live):** legacy binds `StartDate`/`EndDate` as `DateTime` and persists the *wall-clock* component, dropping the timezone offset (an input of `2025-09-23T11:48:00+02:00` is stored as `11:48`, shifting the instant by the offset); v2 is offset-aware and persists the UTC instant (the same input stored as `09:48Z`, round-tripping to `+02:00`). Clients sending non-UTC offsets get different stored instants between legacy and v2 — send UTC (`Z`) for identical behavior. |
| `POST api/ProjectTag` *(create tag)* + assign | `POST v2/Projects/{projectShortName}/Tags` *(create-and-assign)* | ✅ | **New combined create-and-assign.** Body `{Name}`. Creates the named tag in the shared catalogue if absent, then assigns it to the project; if it already exists it is only assigned. Requires `perm:project:tag:create` (Owner/Manager/Admin) — separating tag minting from assignment (see [ADR-0007](../architecture/adr/0007-project-tags-by-name-create-vs-assign.md)). Idempotent. Returns `204`; `400` on invalid tag name; `404` out of caller scope / project / unknown actor; `409` when the project is per-project read-only. **Status & semantics differ (verified live):** legacy `POST api/ProjectTag` is **create-only** — body `{Name, Description?}`, returns `201 Created` + a `Location` header with the new tag `Guid`, and does **not** touch any project (assignment was a *separate* `PUT .../ProjectTag/{guid}` call). v2 folds create+assign into one call addressed by project + name and returns `204` (no body, no `Location`). Legacy `Description` has no v2 equivalent (v2 tags are name-only). |
| `PUT api/Project/{ProjectShortName}/ProjectTag/{ProjectTagId:GUID}` *(assign tag)* | `PUT v2/Projects/{projectShortName}/Tags/{tagName}` | ✅ | **Identifier changed**: tag is addressed **by name**, not `Guid`. **Behavior: assign existing only** — returns `404` when no tag with that name exists (legacy parity for the assign path); it never creates one (use `POST .../tags` to create). Gated by `perm:project:project:tag:assignment` (all project roles). Idempotent (re-assigning an existing tag is a no-op). Returns `204`; `400` on invalid tag name; `404` out of caller scope / project / unknown tag; `409` per-project read-only. |
| `DELETE api/Project/{ProjectShortName}/ProjectTag/{ProjectTagId:GUID}` *(unassign tag)* | `DELETE v2/Projects/{projectShortName}/Tags/{tagName}` | ✅ | **Identifier changed**: tag by **name**, not `Guid`. Removes only the association (the shared tag is not deleted). Idempotent. Returns `204`; `400` on invalid tag name; `404` out of caller scope / project not found; `409` per-project read-only. |

> `ProjectShortName` remains the identifier across legacy and v2 — **not** a breaking change.

#### Verification notes (gateway round-trip, project `roletest`, 2026-06-03)

The migrated write endpoints were exercised live through the Gateway and compared against
their legacy counterparts. Confirmed behaviors and migration-window caveats:

- **Update**: v2 `PUT` → `204` empty vs legacy `200` + `"LEXIS Project was modified."`; date/offset
  semantics differ (see the update row above).
- **Create-and-assign tag**: v2 `POST .../tags` → `204` (creates the catalogue tag when missing, then
  assigns; idempotent), gated by `perm:project:tag:create`. Contrast with legacy `POST api/ProjectTag`
  → `201 Created` + `Location` (create-only; never assigns to a project).
- **Assign tag**: v2 `PUT .../tags/{name}` → `204` (assign existing only, idempotent on re-assign,
  **`404` when no tag with that name exists** — legacy parity for the assign path) vs legacy
  `PUT .../ProjectTag/{guid}` → `200` + text, and **`404`
  `"ProjectTag with id:\"…\" does not exist!"`** when the tag GUID is unknown.
- **Unassign tag**: v2 `DELETE .../tags/{name}` → `204` (idempotent). **The legacy
  `DELETE /api/Project/{ProjectShortName}/ProjectTag/{ProjectTagId}` is not reachable through the
  Gateway — `roleManagmentRBAC.json` maps only the `PUT`, so the legacy `DELETE` returns `403`
  (no permission mapping).** v2 therefore *adds* a reachable unassign path.
- **Output-cache invalidation gap (dual-stack window):** `GET v2/Projects/{shortName}` is output-cached
  (`ProjectCache`) and is only evicted by the **v2** write endpoints. A **legacy** write to the same
  project does not evict the v2 cache, so v2 reads can be stale until the cache expires. The uncached
  legacy `ExtendedProjectInfo` reflects writes immediately.
- **Fixed during verification:** the v2 `ProjectTag` EF mapping omitted the `UtcDateTimeOffsetConverter`
  on `CreatedAt`/`ModifiedAt` (domain `DateTimeOffset` vs legacy `datetime` columns), so v2
  assign/unassign threw `500` ("expected `DateTimeOffset` but actual value was `DateTime`") whenever an
  existing tag row was read. Added the converter in `ProjectTagConfiguration` to mirror
  `ProjectConfiguration`; assign/unassign now return `204`.
- **RBAC mapping required for every new v2 route + snapshot-cache gotcha:** a v2 endpoint with no entry
  in `roleManagmentRBAC.json` is rejected by the Gateway with a `403` `application/problem+json` body carrying
  `code: auth.no_permission_mapping` (it is **not** the endpoint's own `404`). `POST /v2/Projects/{projectShortName}/Tags`
  needed a new mapping → `perm:project:tag:create` (mirroring legacy `POST api/ProjectTag`). Caveat: the
  Gateway resolves mappings from a `HybridCache` snapshot (`EndpointPermissionService:Snapshot`) that is
  only invalidated by the management service's own create/update/delete calls — a **bulk seed/import
  writes the rows directly and does not invalidate it**, so a freshly imported mapping keeps returning
  `403` until the Gateway is restarted (or the snapshot otherwise expires). After importing RBAC changes,
  **restart the Gateway** before re-testing.

All Project mutations are now migrated — see [Migration coverage — complete](#migration-coverage--complete).

#### Verification notes (gateway round-trip, Slice D project-role write path, 2026-06-03)

The project-role write endpoints were exercised live through the Gateway on a throwaway project
(create → assign/unassign user → edit role → delete), with legacy counterparts interleaved:

- **valid-roles**: v2 `GET .../valid-roles` → `200`, identical role set to legacy `ValidRoles`; only the
  permissions field is renamed (`SystemPermissions` → `Permissions`).
- **Create**: `POST /v2/Projects` → `201`; the creator is granted `Owner`. **Confirmed the
  eventual-consistency window**: an immediate `GET v2/Projects/{sn}` by the creator returned `404`
  (out of scope) and only `200` after the Gateway permission-cache TTL (~30 s), once the new `Owner`
  role reached the `projectRoles` claim (ADR-0008).
- **Assign / unassign user**: v2 `PUT`/`DELETE .../users/{idpUserId}` → `204`; legacy `PUT` → `200` +
  `"LEXIS User was assigned…"`, legacy `DELETE` → `204`. Both stacks write the same `UserPermission`
  row.
- **Edit role**: v2 `PUT .../users/{idpUserId}/role` (role id in body) → `204`; role change reflected in
  `GET .../users`.
- **Invariants enforced in Core (not the Gateway)**: removing the owner → `409`
  `"The project owner cannot be unassigned from the project."`; assigning a role at/above the caller's
  own level → `403` `"You can only assign a role strictly below your own level…"`.
- **Delete**: `DELETE /v2/Projects/{sn}` → `204`; soft-deletes the project **and cascade soft-deletes
  the project roles** — confirmed by the project dropping out of the caller's scope after the cache
  refresh and disappearing from the legacy filtered list.
- **RBAC snapshot gotcha (recurred):** the six new v2 routes returned `403 "no permission mapping"`
  until `roleManagmentRBAC.json` was reimported and the Gateway rebuilt/restarted — same caveat as the
  tag endpoints above.

#### Verification notes (gateway round-trip, project `testproject`, 2026-06-16) — read parity

Live v1↔v2 data comparison through the Gateway (caller = `Manager` on `testproject`, system `Admin`). Each pair
was fetched and compared field-by-field (order-insensitive); confirmed **data parity** with only the documented
shape changes:

- **`GET v2/Projects` (list)** vs `GET api/Project` — both return the same 6 caller-scoped projects. `testproject`
  entry: `Name`/`ShortName` equal, `ContactEmail`→`Email`, `OperationMode`→`Mode` (`"Editable"`), dates offset-aware
  (`…+00:00`); v2 drops `Id`/`DomainId`/`AssignedProjectResourceIds`/`AssignedTags` and adds `ExternalId`(null) +
  `ServiceName` (`"LEXIS"`).
- **`GET v2/Projects/testproject/Extended`** vs `GET api/Project/ExtendedProjectInfo?ProjectShortName=testproject`
  — root fields equal after `ContactEmail`→`Email`, `DomainId`→`DomainTypeId`, dates offset-aware. Resource list:
  legacy 15 vs v2 13 — **all 13 v2 rows are an exact subset of the legacy 15** (after `ProjectResourceName`→
  `ResourceName`, spec `Key`→`Name`; v2 adds `ResourceDescription`, `LocationTypeName`). The 2 rows v2 drops
  (`ATR-25-4 / iRODS EFP`, `iRODS 5.1 Resource / iRODS 5.1`) are the soft-deleted assignments/locations v2 excludes
  by design — confirms the documented intended behavior.
- **`GET v2/Projects/testproject/Users`** vs `GET api/Project/testproject/Users` — identical set of 25 users
  (same `(IdpUserId, Email, RoleName)`); v2 only adds `UserName`.
- **`GET v2/Projects/testproject/ValidRoles`** vs `GET api/Project/testproject/ValidRoles` — identical 5 roles
  (`Name`/`Id`/`Description`/permissions all equal); the only change is the field rename `SystemPermissions` →
  `Permissions`.
- **`GET v2/Projects/testproject` (single-get)** — v2-only, `200`; returns the `{…, DomainType (name), IsDeleted,
  Tags[], Users[], Resources[]}` projection as documented.
- **`GET v2/Projects/testproject/Templates`** vs `GET api/Project/TemplatesInfo/testproject` — same 3 clusters and
  node structure, **except** the HEAppE command-template `Parameters[].Description`: legacy emits the placeholder
  string `"placeholder"` where v2 emits `""` or `null` for the same parameters (e.g. `BulkCopy`'s `sourceDir`/
  `targetDir`). Data otherwise identical — a v2 mapping difference worth noting for clients that rendered the legacy
  placeholder text.

---

## Provider

| Legacy (method + path) | Refactored (method + path) | Status | Breaking change summary |
|---|---|---|---|
| `GET api/Provider?CountryCode=` | `GET v2/Providers?name=&countryCode=` | ✅ | **Query params changed**: `name` (string) added next to `countryCode` (string, the `Code` of `GET v2/Countries`). Behavior differs: legacy returns provider-scoped or public set based on token; v2 returns a filtered list. Response shape `{Name, Description?, SupportEmail?, ExternalId?, Mode, ServiceName, IsPrivate, CountryCode}`. |
| `GET api/Provider/{Id:GUID}` | `GET v2/Providers/{providerName}` | ✅ | **Identifier type changed**: `Guid` → `providerName` (natural). Response adds `Locations[...]`, `CountryCode`, `IsDeleted`; returns `404` when not found. |
| `POST api/Provider` *(create)* | `POST v2/Providers` | ✅ | Same body field set (`{Name, CountryCode, Description?, SupportEmail?, IsPrivate}`). **Admin-only in Core** ([ADR-0011](../architecture/adr/0011-provider-write-authorization.md)) — a provider service-key gets `403` (legacy gated only at the Gateway). **Name uniqueness enforced in app code** (including deactivated providers) → `409` on duplicate. Returns `201` with a `v2/Providers/{providerName}` location + body `{Name}` (legacy: `201 CreatedAtRoute` by `Guid`, no body). Name is canonicalized ([ADR-0012](../architecture/adr/0012-provider-location-name-canonicalization.md)); `Description` limit relaxed 150 → 500. |
| `PUT api/Provider` *(update; Id + Name in body)* | `PUT v2/Providers/{providerName}` | ✅ | **Identifier moved from body `Guid` to route name; the provider name is immutable** (legacy body carried `Name` and could rewrite it). Body `{CountryCode, Description?, SupportEmail?, IsPrivate}`. **Status changed**: legacy `200` + `"LEXIS provider was modified."` → v2 `204` (empty). **Actor model widened**: legacy required a provider service-key (`TryGetProviderId`, otherwise `403`); v2 allows Admin on any provider and a provider key only on its own (`providerName` claim, ADR-0011). |
| `DELETE api/Provider/{Id:GUID}` | `DELETE v2/Providers/{providerName}` | ✅ | **Identifier type changed**: `Guid` → name. Soft-deletes (deactivates) the provider. Returns `204` (parity); `404` unknown provider; `409` when already deactivated. |
| — *(no legacy counterpart)* | `POST v2/Providers/{providerName}/Reactivate` | ✅ | **v2-only addition** — reactivates a deactivated provider (the by-name repository lookup bypasses the soft-delete filter for this purpose). Returns `204`; `404` unknown provider; `409` when not deactivated. |

All provider mutations carry `DisabledWhen("ReadOnlyMode")` and are RBAC-mapped under `perm:provider:*`
(the Gateway's permission gate has a dedicated provider branch — see ADR-0011).

---

## Request / Public

| Legacy (method + path) | Refactored (method + path) | Status | Breaking change summary |
|---|---|---|---|
| `GET api/Requests?State=&Type=&ProjectShortName=` | `GET v2/Requests?projectShortName=&idpUserId=&state=` | ✅ | **Query filters changed**: `Type` filter dropped; `State` is now a stable name, not a legacy int. Response reshaped to `{ContactEmail?, Type, State, ProjectShortName, IdpUserId, ProviderName?, ProjectResourceName?}`, with **`Type`/`State` retyped in the response as well**: legacy emits the persisted ints (`RequestModel.Type`, `RequestModel.State`), v2 emits the stable PascalCase names (e.g. `SelfOnboarding`, `Pending`). **Fields dropped, not renamed**: legacy's `UserName`, `LastModification` have no v2 equivalent; `RequestId` is deliberately not exposed ([ADR-0024](../architecture/adr/0024-v2-contracts-zero-surrogate-guids.md), zero surrogate GUIDs) — do not restore it. **Caller-scoped**: a non-admin sees only requests on projects they hold a role on, an admin sees all (`401` invalid token, `400` unparseable `state`/`projectShortName`). **Breaking vs legacy**: `ContactEmail` is the request's *own* contact email (`Request.ContactEmail`; empty for legacy rows that predate the field), whereas legacy `Email` returned the requestor's account email (`User.Email`). |
| `GET api/Requests/SentRequests?State=&Type=&ProjectShortName=` | `GET v2/Requests/Sent?state=` | ✅ | Same response reshape as above, including the `Type`/`State` int→name retype (same dropped fields: `UserName`, `LastModification`; `RequestId` deliberately omitted per ADR-0024); only `state` query remains (user inferred from token). Same `ContactEmail` semantics as `v2/Requests` — the request's own contact email (`Request.ContactEmail`), **breaking vs legacy** which returned the requestor's account email (`User.Email`). |
| `GET api/Public/RequestInfo/{requestId:guid}` | `GET v2/Public/Requests/ByLink/{actionLink}` | ✅ | Anonymous in both. **Addressed by the single-use action-link token, not a surrogate GUID** ([ADR-0024](../architecture/adr/0024-v2-contracts-zero-surrogate-guids.md)): the approver holds the link from their email; either sibling token resolves the request, and the 122-bit token keeps this token-dispensing endpoint unguessable where a low-entropy slug would not. v2 returns `422` (validation problem) on invalid request; response reshaped to `{RequestorUserName?, RequestorEmail?, ProjectShortName, ProjectResourceName?, ProviderName?, RequestState, ValidTo, RequestType, AcceptActionLink?, RejectActionLink?}` (**`RequestId` dropped** — ADR-0024). |
| `POST api/Requests` | `POST v2/Requests` | ✅ | **Self-onboarding only** ([ADR-0009](../architecture/adr/0009-request-write-path-self-onboarding-only.md)): body `{ProjectShortName, RequestType}`, only `RequestType=1` accepted (`422` otherwise) — a `ProjectResourceAssignment` body that legacy accepts is rejected; that flow stays on legacy. Returns `201` (legacy returned `200` + a message); `404` unknown project/caller; `409` already a member or duplicate pending request. Action links + notifications produced inline after save. **Response is `{Slug}` and `Location: v2/requests/{slug}`** ([ADR-0024](../architecture/adr/0024-v2-contracts-zero-surrogate-guids.md)) — the GUID-free public id (`req-{e3}-{shortname}-{r6}`) generated at create; `RequestId` is **not** exposed. **Error-contract note**: `{"RequestType":"HEAppECreation"}` is a valid, parseable name (`RequestType._names`), so it now falls through to the unsupported-type check — `422 request.unsupported_type`, not `400 request.invalid_type` (`CreateRequestEndpoint.cs:43,46-47`). |
| `PUT api/Requests/{RequestId}/Accept` | `PUT v2/Requests/{slug}/Accept` | ✅ | Self-onboarding only (`422` otherwise). **Addressed by the GUID-free slug** ([ADR-0024](../architecture/adr/0024-v2-contracts-zero-surrogate-guids.md)); a v1-created (null-slug) request returns the same `404` as a genuine miss. **Project-scoped in Core** (ADR-0009): caller must hold a role on the request's own project (`404` outside scope) — closes the legacy request-id-only authz gap. Accepts → assigns the requester as a member (reuses `Project.AssignUserWithRoleAsync`). Returns `204` (legacy `200` + message); `409` when already settled. |
| `PUT api/Requests/{requestId}/Reject` | `PUT v2/Requests/{slug}/Reject?reason=` | ✅ | Self-onboarding only (`422` otherwise). **Addressed by the GUID-free slug** (ADR-0024); null-slug v1 rows → `404`. Project-scoped (`404` outside scope). Optional `reason` accepted for parity but, as in legacy, not persisted. Returns `204` (legacy `200` + message); `409` when already settled. |
| `POST api/Public/Handle/{actionLink}` | `POST v2/Public/ActionLinks/{actionLink}` | ✅ | Anonymous in both; single-use (handling one link invalidates its siblings). **REST naming: `handle` (a verb) was replaced by the `action-links` resource** — the stored token is opaque, so this is a route rename only (no request/response change). v2 settles **self-onboarding** requests fully — accept grants membership (stamped as self-initiated, as the anonymous link carries no approver identity), unlike legacy whose public-handle accept skipped self-onboarding membership. Emailed links resolve to this v2 route via the configured public base, not the legacy `/api/Action/Handle/` path. Returns `200`; `400` for unknown/expired/handled links. |

---

## User / UserInfo

| Legacy (method + path) | Refactored (method + path) | Status | Breaking change summary |
|---|---|---|---|
| `GET api/UserInfo` | `GET v2/UserInfo` | ✅ | Response uses typed value objects `{IdentityProviderUserId, Name?, UserName?, Email?, SystemRoles[]}`. Adds output cache + **ETag** support. |
| `GET api/UserInfo/Extended` | `GET v2/UserInfo/Extended` | ✅ | As above plus extended roles (`ProjectResources`, `SystemPermissionTypes`). Adds **ETag / `304 Not Modified`** support — clients must handle conditional responses. |
| `GET api/User/Preferences` | `GET v2/Users/Me/Preferences` | ✅ | **Shape change**: legacy returns the raw JSON preferences string; v2 wraps it in an envelope `{"Preferences": {…}}` (`application/json`). **REST naming: explicit `me` segment** — the caller is implicit (resolved from the token), so `users/me/...` reads unambiguously and avoids colliding with the `users/{id}` slot. |
| `PUT api/User/Preferences` | `PUT v2/Users/Me/Preferences` | ✅ | Replaces the caller's JSON preferences; **upserts** on first write (legacy `User.UpdateUserPreferences` was a no-op when no preference existed). **Shape change**: body is now the same `{"Preferences": {…}}` envelope as the GET response (not a raw JSON string); a body missing `Preferences` returns `400`. Returns `204` (legacy `200` with no body); `404` unknown caller. Evicts the user-info cache. **REST naming: explicit `me` segment** (see GET row). |
| `GET api/Administration/User?ProjectShortName=&IdpUserId=` | `GET v2/Users?projectShortName=&idpUserId=` | ✅ | Moved out of `Administration`. Response reshaped to `{IdpUserId, Email, UserName, Projects[], AssignedProviderId?}`. |

---

## Location

| Legacy (method + path) | Refactored (method + path) | Status | Breaking change summary |
|---|---|---|---|
| `GET api/Location?ProviderName=&LocationTypeId=` | `GET v2/Locations?providerName=&locationTypeId=` | ✅ | Response adds `Mode`, `DefaultServiceIntegrationUrl`, `LocationTypeName` (vs legacy `LocationModel`). |
| `GET api/Location/{Id:GUID}` | `GET v2/Providers/{providerName}/Locations/{locationName}` | ✅ | **Identifier changed** (2026-07/08): the flat GUID route was replaced by the provider-nested natural key (ADR-0022, ADR-0024). Same added fields as the list. `404` when the provider or location is unknown. |
| `POST api/Location` | `POST v2/Providers/{providerName}/Locations` | ✅ | **Route moved under the provider** — the owning provider is the route's natural name, not a body `ProviderId` (`Guid`). Body `{Name, Endpoint, IsPrivate, IsShared, Description?}`: the legacy `LocationTypeId` and `DefaultServiceIntegrationUrl` fields are **not settable in v2** (EF shadow properties). A new location starts with **no resource allocations** (legacy parity) — assignments are created separately through the resource-assignment write surface (ADR-0016). Returns `201` with a `v2/Providers/{providerName}/Locations/{locationName}` location header + body `{Id}`; `403` not allowed (ADR-0011); `404` unknown provider; `409` duplicate location name under the provider. |
| `PUT api/Location` *(Id in body)* | `PUT v2/Providers/{providerName}/Locations/{locationName}` | ✅ | **Identifier moved from body `Guid` to route names** (provider + location). **Mutable surface narrowed**: body is `{Endpoint, Description?}` only — legacy could also rewrite `Name`, `IsPrivate`, `IsShared`, `LocationTypeId`, `DefaultServiceIntegrationUrl`. **Status changed**: legacy `200` + `"LEXIS Location was modified."` → v2 `204`. `404` unknown provider/location; `409` when the location is deleted. |
| `DELETE api/Location/{Id:GUID}` | `DELETE v2/Providers/{providerName}/Locations/{locationName}` | ✅ | **Identifier changed**: `Guid` → provider + location names. Soft-deletes the location. Returns `204` (parity); `404` unknown provider/location; `409` when already deleted. |

All location mutations carry `DisabledWhen("ReadOnlyMode")`, authorize via `ProviderWriteAuthorization`
(Admin any provider / provider service-key its own — ADR-0011) and are RBAC-mapped under `perm:provider:*`.

---

## ProjectTag

| Legacy (method + path) | Refactored (method + path) | Status | Breaking change summary |
|---|---|---|---|
| `GET api/ProjectTag?TagName=` | `GET v2/ProjectTags?tagName=` | ✅ | Query param casing `TagName` → `tagName`. Response `{Id, Name}` (unchanged shape). |
| `GET api/ProjectTag/{Id:GUID}` | `GET v2/ProjectTags/{tagName}` | ✅ | **Identifier changed** to the tag name (ADR-0024). Response `{Name}`. Returns `404` when not found. |

ProjectTag mutations are migrated: **create-and-assign** is `POST v2/Projects/{projectShortName}/Tags`
(see the Project section, ADR-0007); **delete** is `DELETE v2/ProjectTags/{tagName}` (F1, blocks-if-in-use
→ `409`). The standalone global `POST api/ProjectTag` (create outside a project) is not reproduced — tag
minting always happens in a project context — and `PUT api/ProjectTag` (rename) is **dropped by design**:
v2 tags are name-only, so a rename is a delete + recreate ([f1 plan](./f1-projecttag-delete-plan.md)).

---

## Enums

| Legacy (method + path) | Refactored (method + path) | Status | Breaking change summary |
|---|---|---|---|
| `GET api/Enums/LocationTypes` | `GET v2/LocationTypes` | ✅ | **No breaking change** — both return `{Id, Name}` items (legacy typed as `IEnumerable<object>` but JSON is identical). **REST naming: promoted out of the `enums/` group to a top-level lookup collection** (`enums` leaked the implementation type and was inconsistent with the top-level `countries`/`resources`/`locations` reference collections). |
| `GET api/Enums/RequestStates` | `GET v2/RequestStates` | ✅ | No breaking change (`{Id, Name}`). v2 values are hardcoded but match the enum. Promoted to top-level (see LocationTypes row). |
| `GET api/Enums/RequestTypes` | `GET v2/RequestTypes` | ✅ | **Shape unchanged (`{Id, Name}`), one name differs**: v2 lists the same 4 members — `Default` (0), `SelfOnboarding` (1), `ProjectResourceAssignment` (2), `HEAppECreation` (3) — but names `Id:0` `Default` where legacy names it `Unknown`: a conscious deviation, since `Name` is now part of the v2 wire contract and `Default` is the better name for a real value. Promoted to top-level. |
| `GET api/Enums/OperationModeResponses` | `GET v2/OperationModes` | ✅ | No breaking change to payload (`{Id, Name}`). Promoted to top-level. |
| — *(v2-only)* | `GET v2/ProjectDomainTypes` | ✅ | Lists project domain type names (used as `domainTypeName` on project create/update). Promoted to top-level alongside the other lookups (was `v2/enums/project-domain-types`). |

---

## Meta / Version

| Legacy (method + path) | Refactored (method + path) | Status | Breaking change summary |
|---|---|---|---|
| `GET api/version` | `GET v2/Version` | ✅ | Anonymous at the Core endpoint in both (legacy's `api/version` group carries no authorization requirement, `ServiceEndpoint.cs:82`; v2 declares `AllowAnonymous`), and both `/api/version` and `/api/v2/Version` are exact entries on the Gateway's default public-path list ([`PublicEndpointsOptions.cs`](../../src/Modules/Services/Gateway/UserOrg.Gateway/Configuration/PublicEndpointsOptions.cs)), so both are anonymous end to end. **Breaking change**: legacy returns a bare `text/plain` version string; v2 returns `{"Version":"…"}` (`application/json`). |

---

## Resource

| Legacy (method + path) | Refactored (method + path) | Status | Breaking change summary |
|---|---|---|---|
| `GET api/ProjectResource?ProjectShortName=&LocationTypeId=&StartDate=&EndDate=` | `GET v2/Providers/{providerName}/Resources?projectShortName=&locationTypeId=&startDate=&endDate=` | ✅ | **Route moved under the provider** — a resource's natural key is `(providerName, resourceName)`, so the read is nested (ADR-0022); the path `providerName` narrows the result to that provider (resources with an active assignment on one of its locations). Query param casing lowercased; dates are now **offset-aware** (`DateTimeOffset`) — **verified live**: legacy `"2026-03-01T00:00:00"` → v2 `"2026-03-01T00:00:00+00:00"` (same instant). **Caller-scoped**: returns only resources assigned to a project the caller has a role on (from the `projectRoles` claim), further narrowed to the path provider — v2 **drops the legacy admin/provider branches**. **Verified live (2026-06-16):** `GET v2/Providers/IT4I/Resources` → `200` (7 in-scope IT4I resources); the legacy `projectShortName=testproject` set matches v2 (identical 8 rows when not provider-narrowed), name-ordered. Response renamed: `{Id, Name, Description, PrimaryInvestigatorEmail, StartDate, EndDate, OperationMode, Assignments[...]}`; `OperationMode` is the domain enum name string (e.g. `"Editable"`, matches legacy `EOperationModeResponse` serialization). **Assignment `Specifications` field renamed `Key` → `Name`** (verified live): legacy serialized `IList<KeyValuePair<string,string>>` as `{"Key","Value"}`, v2 emits `{"Name","Value"}` — same data. Soft-deleted assignments/locations are excluded (DbContext-wide `!IsDeleted`). The unused legacy `statusId` parameter is dropped. **Was briefly broken (`500`) on 2026-06-16 — see the systemic orderby note below.** |
| `GET api/ProjectResource/{Id:GUID}` | `GET v2/Providers/{providerName}/Resources/{resourceName}` | ✅ | **Identifier changed: `Guid` → full natural key `(providerName, resourceName)`** (ADR-0022). A resource name is unique only *per provider*, so the provider qualifies it — addressing a name under the wrong provider resolves to `404` (same as missing). **Verified live (2026-06-16):** `GET v2/Providers/IT4I/Resources/ATR-25-4` → `200`; `GET v2/Providers/EFP-Core/Resources/ATR-25-4` → `404` (right name, wrong provider). Missing, wrong-provider and out-of-scope all return the identical `404` (no existence leak, ADR-0018). Same response shape as the list item. **Nested allocations are addressed by their GUID-free `Slug` ([ADR-0024](../architecture/adr/0024-v2-contracts-zero-surrogate-guids.md)) — the assignment `Id`, `ProjectResourceId` and `LocationId` GUIDs are dropped; the location is identified by its `LocationName`.** |

Resource (`ProjectResource`) mutations are now migrated (**F4**): `POST v2/Providers/{pn}/Resources`
(create), `PUT v2/Providers/{pn}/Resources/{name}` (update, name immutable), `DELETE …/{name}`
(soft-delete) — per-provider identity, [ADR-0030](../architecture/adr/0030-projectresource-write-model.md).
The `ProjectResourceAssignment` **writes** are migrated to the provider-nested surface
`v2/Providers/{providerName}/Locations/{locationName}/Assignments` (POST/PUT/DELETE, [ADR-0016](../architecture/adr/0016-resource-assignment-writes-through-provider-aggregate.md)); its standalone **GET** reads are
consciously **dropped** — assignments are read back through the resource read (`v2/.../resources`).
Resource↔project assignment (`PUT/DELETE v2/Projects/{sn}/Providers/{pn}/Resources/{name}`) is **F5**.

#### Verification notes (gateway round-trip, project `testproject`, 2026-06-16) — systemic `ORDER BY value-object.Value` regression (fixed)

Live v1↔v2 comparison through the Gateway (caller = `Manager` on `testproject`, system `Admin`) surfaced a
**systemic regression that 500'd five v2 list reads** — `GET v2/resources`, `GET v2/Providers`, `GET v2/Locations`,
`GET v2/Requests` and `GET v2/Requests/Sent` — all fixed the same day.

- **Symptom & root cause.** Each paged list query ordered by a *value-converted* property's `.Value`
  (`orderby pr.ResourceName.Value`, `.OrderBy(p => p.Name.Value)`, `.OrderBy(r => r.ProjectId.Value)`). Calling
  `.Value` inside `ORDER BY` is **not SQL-translatable** (it is fine in a `SELECT` projection), so EF threw
  *"The LINQ expression … could not be translated"* → `500 internal_error`. The value-object `.Contains` scope
  filter was **not** the cause — it translates. The EF in-memory test provider does not translate to SQL, so the
  unit tests never caught it (see the parity-test gap in [v1-retirement-parity.md](./v1-retirement-parity.md)).
- **Fix.** Order by the converted property **directly** (`orderby pr.ResourceName`, `p.Name`, `r.ProjectId`) — EF
  emits `ORDER BY` the mapped column — and make the value objects `IComparable` (`ResourceName`, `ProviderName`,
  `LocationName`, `ProjectId`) so the in-memory provider can still sort. Files:
  `ResourceDetailsQuery.cs`, `LocationDetailsQuery.cs`, `ProviderDetailsQuery.cs`, `RequestDetailsQuery.cs`.
- **Verified after fix.** All five endpoints return `200`; `v2/resources?projectShortName=testproject` matches the
  legacy 8 rows (name-ordered); `v2/Providers` matches the legacy 3 providers. Core.UnitTests 462 passed / 8 skipped.
  (`v2/Locations` returns more rows than legacy — the separate, pre-existing F0 caller-scoping difference, unrelated
  to this fix.)

---

## Other

| Legacy (method + path) | Refactored (method + path) | Status | Breaking change summary |
|---|---|---|---|
| `GET api/Other/Country?CountryCode=` | `GET v2/Countries?countryCode=` | ✅ | Promoted out of the `Other` grab-bag to a top-level route. Query param casing `CountryCode` → `countryCode` (still a substring match on the country `Name` column). Response `{Code, Name}` **unchanged — verified live byte-identical** (251 = 251 rows, same order; filter `=BE` returns `[{"Code":"BE","Name":"Belgium"}]` on both); legacy maps the `Name` column to `Code` and `Description` to `Name`. **Behavioral change** (verified live): a filtered result with no match now returns `200 []` instead of legacy `400`. |
| `GET api/Other/Statistics?fromDate=&toDate=` | `GET v2/Statistics?fromDate=&toDate=` | ✅ | Promoted out of `Other`. Same date defaults (fromDate = first of current month, toDate = today, widened to a week if inverted). **Global / non-scoped** (parity). Response `{NumberOfProjects, NumberOfUsers, NumberOfResources, WeeklyData[...]}` (single object, matching the legacy runtime body — the legacy OpenAPI `IEnumerable` annotation was inaccurate). Timestamps are normalised to UTC for weekly bucketing; the clock is injected (`TimeProvider`) so the computation is deterministic/testable. **Verified live byte-identical** to legacy for both the default window and a multi-week range (`fromDate=2026-05-01&toDate=2026-06-15` → identical 6 Monday-anchored weekly buckets and totals 68/68/61). |
| `GET api/Other/CyclopsUsage/{ProjectShortName}/{ResourceName}` | `GET api/Cyclops/Usage/{ProjectShortName}/{ProviderResourceName}` *(doc-only, v2-User)* | ✅ | **Not served by Core — by design (F2 C1).** The real request is intercepted by the Gateway, which authenticates and proxies to the external Cyclops usage API. Core does not implement it; `CyclopsUsageDocumentTransformer` injects the operation into the **v2-User** OpenAPI document so it stays discoverable in Scalar after v1 retirement (path/params + a note that the Gateway serves it). The Gateway also serves `GET api/Cyclops/TotalUsage/{ResourceName}`, the same data aggregated across every project the resource is used by. |

---

## Migration coverage — complete

**Every legacy Core route is now ported, re-homed, or consciously dropped.** No Core-scope legacy route
remains without a disposition. The per-route ledger with owners and ADR links is
[v1-retirement-parity.md](./v1-retirement-parity.md) (verified against code 2026-07-08); this section
records the dispositions that were previously pending.

### Now migrated (were pending in earlier revisions of this doc)

| Area | Legacy endpoints | Disposition |
|---|---|---|
| ProjectResource CRUD | `POST/PUT api/ProjectResource`, `DELETE api/ProjectResource/{Id}` | ✅ **F4** → `POST/PUT/DELETE v2/Providers/{pn}/Resources[/{name}]` (per-provider identity, [ADR-0030](../architecture/adr/0030-projectresource-write-model.md)) |
| Resource↔project assignment | `PUT/DELETE api/Project/{sn}/ProjectResource/{name}` | ✅ **F5** → `PUT/DELETE v2/Projects/{sn}/Providers/{pn}/Resources/{name}` (ADR-0031) |
| ProjectTag delete | `DELETE api/ProjectTag/{Id}` | ✅ **F1** → `DELETE v2/ProjectTags/{tagName}` (blocks-if-in-use → `409`) |
| Admin role management | `PUT api/Administration/PromoteToProviderAdmin`, `PUT .../ResetToPlatformUser` | ✅ **F3** → `PUT v2/Users/{idp}/ProviderAdmin/{pn}`, `DELETE v2/Users/{idp}/ProviderAdmin` |
| Administration reads | `GET api/Administration/{Project,ProjectTag,User,Location,Location/{id},Provider,Provider/{id},Requests}` | ✅ **F0.3** — tier-covered via `caller.ResolveReadScope` ([ADR-0018](../architecture/adr/0018-read-scope-tiers.md)); admin sees all, provider its own, user project-scoped |
| Cache reset | `DELETE api/Administration/Cache` | ✅ **F2** → `DELETE v2/Caches` (Admin-only, evict-all) |
| Self-onboarding write path | `POST api/Requests`, `PUT .../Accept`, `PUT .../Reject`, `POST api/Public/Handle/{link}`, `PUT api/User/Preferences` | ✅ self-onboarding only, project-scoped in Core ([ADR-0009](../architecture/adr/0009-request-write-path-self-onboarding-only.md)) |
| Project-role write path | create/delete, assign/unassign user, edit role, `ValidRoles` | ✅ Core owns the writes into `UserPermission`; decoupled from the `projectRoles` claim (~30 s eventual consistency, [ADR-0008](../architecture/adr/0008-core-owns-project-role-writes.md)) |
| DataStaging (Vault) | `GET WorkerData`, `GET/POST DataStagingLocations` | 🟦 **re-homed to DDI** module `v2/DataStaging/*` ([ADR-0017](../architecture/adr/0017-ddi-service-api-as-separate-module.md)) |
| Service (S2S) | `GET api/Service/SyncDetails`, `GET api/Service/ProjectsInfo` | 🟦 **re-homed to DDI** `v2/Service/*` |
| HEAppE file download | `GET api/Heappe/ListChangedFilesForJob/{sn}/{res}/{jobId}` | 🟦 **re-homed to Heappe** module (ADR-0053, #236); legacy endpoint deleted |

`api/Heappe/*` stays **unversioned in its entirety** — the re-homed `ListChangedFilesForJob` above keeps
its legacy path, and so does every other route in the group (`HeappeAdapterExtensions.cs`). The whole
group is mapped outside `MapV2CoreEndpoints`/the `FeatureManagement:V2Api` gate and has no `v2/`
counterpart, even after v1 retirement.

### Consciously dropped (no v2 endpoint, by design)

| Legacy endpoint | Why dropped |
|---|---|
| `GET api/ProjectResourceAssignment`, `GET …/{Id}` | Assignments are read back through the resource read (`v2/.../resources`); no standalone assignment resource in v2 ([ADR-0016](../architecture/adr/0016-resource-assignment-writes-through-provider-aggregate.md)). |
| `POST api/ProjectTag` (global create) | Tag minting always happens in a project context (`POST v2/Projects/{sn}/Tags`); create-outside-project is unused. |
| `PUT api/ProjectTag` (rename) | v2 tags are name-only → a rename is delete + recreate ([f1 plan](./f1-projecttag-delete-plan.md)). |
| `GET api/Other/CyclopsUsage/{sn}/{res}` | Served by the **Gateway** on `api/Cyclops/Usage/{sn}/{res}` (proxies external `cyclops-api`); Core never implements it — kept discoverable via `CyclopsUsageDocumentTransformer` in the v2-User Scalar doc (F2 C1). The legacy Core stub is still present in Core, unreachable behind the Gateway, which shadows and serves the route. |

---

## Summary

**Coverage is complete** — the remaining work before v1 can be deleted is testing and stack removal, not
endpoint migration. See [next-phase-plan.md](./next-phase-plan.md) for the cutover sequence.

| Metric | Count |
|---|---|
| Legacy endpoints (total) | ~61 |
| Ported to v2 (`/v2/*`) | ~49 |
| Re-homed to another module (DDI / Heappe) | 6 |
| Consciously dropped (with rationale) | ~4 (standalone assignment GETs, global tag create, tag rename, Cyclops) |
| Pending endpoint migration | **0** |

- **Both stacks still run in parallel** — `Program.cs` maps every legacy `api/*` route and the v2 +
  module routes side by side; the discriminator is the `api/v2/` prefix.
- **Before cutoff (not endpoint coverage):** automated v1↔v2 **parity testing** (F7 reads-parity done,
  #238/ADR-0055; writes + DDI/S2S reads are Phase 2), applying the deferred unique indexes after the
  legacy writers are decommissioned, then **deleting `src/Core`** (`Domain`, `Application`,
  `Infrastructure.DataAccess`, the v1 endpoints, `AddV1Core`/`MapV1CoreEndpoints`, v1 OpenAPI docs).
- Most v2 breaking changes are **response-shape refinements** (typed value objects, added fields) plus a
  few **identifier and query-parameter** changes (Provider id `Guid`→name; Request filters; Provider
  `CountryCode`→`countryCode`; Project tag assignment by name; `ShortName` immutable on update).
- One **value-semantics** change: `v2/requests*` `ContactEmail` is the request's own contact email
  (`Request.ContactEmail`), not the requestor's account email (`User.Email`) as in legacy.
- **One enum name change, plus version**: `GET v2/RequestTypes` names `Id:0` `Default` where legacy
  names it `Unknown`; `GET v2/Version` returns `{"Version":"…"}` instead of a bare `text/plain` string.
- Most v2 breaking changes are **response-shape refinements** (typed value objects,
  added fields) plus a few **identifier and query-parameter** changes
  (Provider id `Guid`→name; Request filters; Provider `CountryCode`→`countryCode`).
  New write-side identifier change: Project **tag assignment is now by name**, not `Guid`,
  and `ShortName` is **immutable** on update. **Tag minting is split from assignment**: `POST .../tags`
  creates-and-assigns (`perm:project:tag:create`), `PUT .../tags/{name}` assigns existing only
  (`404` if absent), per [ADR-0007](../architecture/adr/0007-project-tags-by-name-create-vs-assign.md).
  **Provider and Location writes are likewise name-addressed**: the provider name is immutable (route
  key), locations are nested under their provider, and the location's mutable surface narrowed to
  `{Endpoint, Description?}`.
- One **value-semantics** change: `v2/requests*` `ContactEmail` is now the request's own
  contact email (`Request.ContactEmail`), not the requestor's account email (`User.Email`) as
  in legacy.
- **One enum name change, plus version**: `GET v2/RequestTypes` names `Id:0` `Default` where legacy
  names it `Unknown`; `GET v2/Version` returns `{"Version":"…"}` instead of a bare `text/plain` string.
