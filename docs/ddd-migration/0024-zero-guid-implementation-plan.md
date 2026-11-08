# Implementation plan — ADR-0024 (v2 contracts: zero surrogate GUIDs)

Companion to [ADR-0024](../architecture/adr/0024-v2-contracts-zero-surrogate-guids.md). TDD, ponytail-minimal.

## Ground rules

- **TDD vertical slices.** One behaviour → one RED test → minimal GREEN. Extend the existing
  `Tests/Modules/Core/Core.UnitTests/**/*EndpointTests.cs` files; naming `Should_<X>_When_<Y>`; mock at Ports.
- **Each slice ships green.** Order is low-risk (pure projection) → high (schema). Stop after any slice and
  what's merged is coherent.
- **Confirmed out of scope** (data-verified 2026-06-22): no `ProviderName` column, no `Provider.Name`
  alternate key, no legacy/Waldur writer changes, no DB trigger, no backfill, no invariant #5, no
  `GetResource` resolution rewrite. v1 and v2 coexist; v2 addresses only v2-created entities.

## Migrations (EF Core, `Core.DataAccess/Migrations`)

> **⚠ EF migration drift (found 2026-06-22):** `dotnet ef migrations add` against `Core.DataAccess`
> scaffolds ~150 operations — the model snapshot has drifted from the migration history (the refactor
> changed FK names/delete-behaviors without migrations). So M1–M3 are delivered as **hand-written,
> idempotent SQL deployment scripts** under `docs/ddd-migration/sql/`, verified against the populated DB
> in a rolled-back transaction — not as EF migrations. EF entity *config* (mapping + `HasIndex`) is still
> updated so the runtime model is correct; it must be kept in sync with the SQL scripts by hand.

> **UPDATE 2026-06-27 (cleanup):** the hand-SQL scripts below were **replaced by tracked EF migrations**;
> the standalone `sql/0024-M*.sql` files were deleted.
> - **M1 + M2** → EF migration `20260627145647_AddRequestAndAssignedLocationSlug` (legacy
>   `Infrastructure.DataAccess`). Identical shape (nullable `nvarchar(120)` + filtered unique index).
> - **M3** → **superseded by ADR-0026.** Global `(ServiceName, Name)` uniqueness is replaced by per-provider
>   `(ProviderId, Name)` (a resource belongs to one provider). Not migrated. See
>   `0026-deployment-runbook.html`.

| ID | Change | Notes |
|---|---|---|
| **M1** | `Request.Slug` nullable `nvarchar(120)` + filtered unique index `WHERE Slug IS NOT NULL` | Now in EF migration `…AddRequestAndAssignedLocationSlug`. No default, no backfill. |
| **M2** | `AssignedLocation.Slug` nullable + filtered unique index `WHERE Slug IS NOT NULL` | Now in the same EF migration as M1. Same shape. |
| **M3** | ~~`ProjectResource` filtered unique index `(ServiceName, ResourceName)`~~ | **Superseded by ADR-0026** — replaced by per-provider `(ProviderId, Name)`. Not applied. |

## Shared building blocks (build first, with unit tests)

- **`RequestSlug.New(Email, ProjectShortName) : string`** → `req-{e3}-{shortname}-{r6}`. `e3` = first 3
  `[a-z0-9]` of the email local part, lower-cased; `r6` = 6 random base36. Pure; the format rules live here.
- **`AllocationSlug.New(ResourceName) : string`** → `asg-{resourcename}-{r6}`.
- Both: deterministic except `r6`; caller retries on the (rare) unique-index violation.

---

## Track ① — contract cleanup (NO schema)

Pure request/response/filter shape changes; each resolves natural keys that already exist (reuse
`IUserDirectory` / `IProjectDirectory` / provider lookups in the read queries).

- [x] **①.1 `GetExtendedProject.AssignedTags`** `IReadOnlyList<Guid>` → tag **names** (`string`). **DONE.**
  *Test:* `GetExtendedProjectEndpointTests` — response tags are names, no GUIDs. *Impl:* project tag names in `ExtendedProjectSummary`.
- [x] **①.2 + ①.3 `GetRequests` / `GetSentRequests`** — **DONE (merged).** They share `RequestSummary` + `RequestDetailsQuery.GetRequestsAsync`, so one slice fixed both. Filters `?projectId/userId` → `?projectShortName/idpUserId` (no `providerId` filter existed); `RequestSummary.{ProjectId,UserId,ProviderId}` → `ProjectShortName/IdpUserId/ProviderName`. Query: natural-key filters via EXISTS, `ShortName`/`IdpId` via correlated `IgnoreQueryFilters().FirstOrDefault()` subqueries (resolves soft-deleted names, degrades dangling FKs), `ProviderName` via two-pass stitch (paged overload out of entity slots).
  *Test:* `GetRequestsEndpointTests`, `GetSentRequestsEndpointTests`, `RequestQueryTests` (regression). *Routes unchanged → no RBAC impact.*
- [x] **①.4 `GetUsers.AssignedProviderId`** (Guid) → `AssignedProviderName` (string). **DONE.** `UserSummary` field → `ProviderName?`; query resolves names via a two-pass `IgnoreQueryFilters` lookup (added `IDataQuery<Provider>` to the ctor — generic registration, no DI change).
  *Test:* `GetUsersEndpointTests`.
- [x] **①.5 Drop response self-`Id` GUIDs** — **DONE** (4 micro-slices). (a) `GetProjectTag(s)` — `Id` dropped from Response + `ProjectTagSummary` + all 3 query projections. (b) `GetLocation(s)` — `Id` dropped from Response + `LocationSummary` + both projections; query tests switched their paging-identity from `.Id` to `.Name`. (c) `GetResource(s)` — resource-level `Id` dropped from `ResourceResponse` + `ResourceSummary` + both projections (nested assignment GUIDs deferred to ②.7). (d) `AddLocation` — was a **runtime bug** (`CreatedAtRoute("GetLocationV2", {id})` → non-existent route, wrong route values); rewritten to `Results.Created($"v2/providers/{name}/locations/{locationName}", …)` with a GUID-free natural-key body — explicit URL so the Location is set here, not deferred to execution (mirrors the AssignResource dead-route guard). Added the missing success test.
  *Test:* `GetProjectTag(s)EndpointTests`, `GetLocation(s)EndpointTests`, `LocationDetailsQueryTests`, `GetResourceEndpointTests`, `ResourceDetailsQueryTests`, `ProviderWriteEndpointTests`.

## Track ② — generated slugs (M1, M2)

- [x] **②.6 Request addressed by slug.** **DONE** (foundation + addressing rewire).
  - [x] `RequestSlug.New(Email, ProjectShortName)` helper — pure, unit-tested (`RequestSlugTests`).
  - [x] `Request.Slug` domain property + additive optional `Create(slug:)` param (`RequestAggregateTests`).
  - [x] EF config: `Slug nvarchar(120)` + filtered unique index; **M1 SQL script written & DB-verified**.
  - [x] `CreateRequest` generates+sets the slug, `Location → requests/{slug}`, `Response` is `{Slug}` (`RequestId` dropped). **No regenerate-retry**: the `r6` suffix makes a global collision ~1/2e9 per requester+project; a hit throws a clean-rollback 500 (whole create is one UnitOfWork) and the client re-POSTs to a fresh slug — add a retry loop only if a collision is ever observed.
  - [x] `Accept`/`Reject` routed to `{slug}` (authenticated + scope-checked, so a low-entropy id is safe); resolve via new `IRequestQuery.FindIdBySlugAsync` then load the keyed repo; v1-created (null slug) → same `404` as a genuine miss.
  - [x] **`GetPublicRequestInfo` addressed by the action-link token, NOT a slug** (`GET v2/Public/Requests/ByLink/{actionLink}`), `RequestId` dropped from `Response`. *Deviation from the original "route to {slug}" plan, decided 2026-06-22:* it is anonymous and dispenses the accept/reject tokens, so a ~31-bit slug would be brute-forceable; the 122-bit action-link token (the natural key the approver actually holds) keeps it unguessable. Reuses `FindActionLinkTargetAsync`. Legacy email→link→decide flow preserved; flow to be revisited later.
  - [x] **Gateway RBAC** (`roleManagmentRBAC.json`): `{requestId}`→`{slug}` on accept/reject and the public route → `by-link/{actionLink}`. **Verified non-load-bearing**: the path matcher (`EndpointPermissionService.BuildRegexFromTemplate`) compiles `{x}` to `(?<x>[^/]+)` — hyphen-safe and name-agnostic — and the request perms are literal (no `{slug}` substitution), so auth matches regardless; the json edits keep the templates honest.
  - *Test:* `AcceptRequestEndpointTests`, `RejectRequestEndpointTests`, `GetPublicRequestInfoEndpointTests`, `CreateRequestEndpointTests` — all green (484 unit tests pass).
- [x] **②.7 Allocation addressed by slug.** **DONE** (helper + M2 + addressing rewire).
  - [x] `AllocationSlug.New(ResourceName)` → `asg-{resourcename}-{r6}` — pure, unit-tested (`AllocationSlugTests`). Random `Random6()` is a deliberate 6-line twin of `RequestSlug`'s; not abstracted (two uses).
  - [x] `AssignedLocation.Slug` domain property + additive optional `Create(slug:)`, threaded through `Location.AssignResource`/`Provider.AssignResource`. Update/Remove now address by `string slug` (in-aggregate `FindActiveAssignment(slug)`), not the surrogate `Guid`.
  - [x] EF config: `Slug nvarchar(120)` + filtered unique index; now in EF migration `…AddRequestAndAssignedLocationSlug` (was hand-SQL, DB-verified: `INDEX_BUILT`, 187 nulls coexist, duplicate blocked Msg 2601, clean rollback).
  - [x] `AssignResource` mints the slug from the resolved `ResourceName` (added to `ResourceAssignmentTarget`), `Location → …/assignments/{slug}`, `Response` is `{Slug}`. Same no-retry stance as ②.6.
  - [x] `Update`/`Remove` routed to `…/assignments/{slug}` (string param); null-slug v1 rows → same not-found.
  - [x] `GetResource(s)` projection + `ResourceAssignmentResponse`/`ResourceAssignmentSummary` expose `Slug` and drop **all three** assignment GUIDs (`Id`/`ProjectResourceId`/`LocationId`) — `Id`/`ProjectResourceId` kept only as internal projection keys. Location identified by `LocationName`.
  - [x] **Gateway RBAC** (`roleManagmentRBAC.json`): Update/Remove `{id}`→`{slug}` (non-load-bearing, same `[^/]+` matcher reasoning as ②.6).
  - *Test:* `AllocationSlugTests`, `ResourceAssignmentDomainTests`, `ResourceAssignmentEndpointTests`, `ResourceDetailsQueryTests`, `ResourceAssignmentQueryTests`, `GetResourceEndpointTests`, `LocationDetailsQueryTests` — all green (486 unit tests).

## Track ③ — resource addressed by name (M3)

- [x] **③.8 Unique-name guard.** **DONE.** M3 filtered unique index `(ServiceName, Name) WHERE IsDeleted = 0` ([`sql/0024-M3-projectresource-unique-name.sql`](sql/0024-M3-projectresource-unique-name.sql)) + EF `HasIndex`. **DB-verified** against 94 active rows: pre-flight 0 dupes, index built, active duplicate rejected (Msg 2601), a soft-deleted row may reuse an active name, rolled back. (The v2 resource *write* path is deferred — ADR-0009 — so the guard is the DB index; no in-memory test can enforce a unique index.)
- [x] **③.9 `AssignResource` by `resourceName`.** **DONE.** Body takes `ResourceName` (drops `Guid ProjectResourceId`); `IResourceAssignmentQuery.GetResourceForAssignmentByNameAsync` resolves the single active resource (M3-unique) and returns its id to drive the aggregate; unknown name → 404; resolved **pre-assignment** (no provider). `ResourceAssignmentTarget` now carries `ProjectResourceId` (the ②.7 `ResourceName` bridge removed — assign has the name from the body). Case-insensitive addressing (the column collation), confirmed 2026-06-22.
  *Test:* `ResourceAssignmentEndpointTests`, `ResourceAssignmentQueryTests` (`GetResourceForAssignmentByNameAsync` resolve + unknown→null) — green (488 unit tests).

---

## Cross-cutting (do alongside the slices that touch each route)

- [x] **Gateway RBAC** (`roleManagmentRBAC.json`): updated for every changed *route* — requests `{requestId}`→`{slug}`, public info → `by-link/{actionLink}`, assignments `{id}`→`{slug}`. The `resourceName`-body change (③.9) is bodyonly so the assign route is unchanged → no RBAC edit. **Verified non-load-bearing**: the path matcher compiles `{x}` to `(?<x>[^/]+)` (hyphen-safe, name-agnostic) and request/assignment perms are literal — see [[gateway-two-wildcard-regexes]].
- [x] **OpenAPI/Scalar docs:** each changed endpoint's `MapDocumentation` updated where the body/route shape changed (`AssignResource` body, request slug routes); `Produces<>`/`Accepts<>` auto-reflect the new record shapes; audience `WithGroupName` unchanged.
- [x] **Output cache tags** stay GUID-keyed internally (`CacheTags.*`) — unchanged; slugs are contract-only. (Nothing to do — confirmed.)
- [x] **`dotnet format`** scoped to changed files per slice (whitespace-only; see memory: format scope gotcha).

## Verification per slice

- `dotnet test <project> --no-build --filter-query "/*/*/*/<Method>"` green.
- After M3: re-run the saved SQL diagnostics against the populated DB (`127.0.0.1,44985 / UserOrgDBContext`) to confirm the unique index builds clean.
- Final: `dotnet build` (analyzers) + run the app, smoke-test a v2 slug/name round-trip.

## Suggested commit sequence

`①.1 → ①.2 → ①.3 → ①.4 → ①.5` (no-schema, fast) · `M1+②.6` · `M2+②.7` · `M3+③.8 → ③.9`.
Each is one small, green, independently-revertable commit.

## Risks / watch

- **Gateway route patterns** for `{slug}` — the most likely place a contract change silently breaks auth.
- **Collation** on the `ResourceName` unique index (CI index vs CS value object) — pick one rule.
- **`r6` collision** — handled by the filtered unique index + a regenerate-and-retry in the use-case.
