# v2 Endpoint Test Plan

Branch-coverage analysis and prioritized test plan for all **44 endpoints** created during the
`src/Core` → `src/Modules/Core` refactor. Produced from per-endpoint analysis (inputs × external
sources × branches). Trivial mapping tests and "exception propagates to middleware" tests are
deliberately excluded — the focus is on **uncovered logical branches** and **genuine correctness/auth
questions**.

> Convention note: `extension(IEndpointRouteBuilder app)` is valid C# 14 extension-member syntax (build
> is green). Any tooling flagging it as a compile error is a false positive.

## 1. Coverage status

| Feature | Endpoints | Existing test coverage |
|---|---|---|
| Enums | GetLocationTypes, GetOperationModes, GetRequestStates, GetRequestTypes, GetProjectDomainTypes | none |
| Meta | GetVersion | none |
| Location | GetLocation, GetLocations | none |
| ProjectTag (read) | GetProjectTag, GetProjectTags | E2E only, no unit |
| Provider | GetProvider, GetProviders | query-level only |
| Request (read) | GetPublicRequestInfo, GetRequests, GetSentRequests | query-level only, no endpoint tests |
| Request (write) | CreateRequest, AcceptRequest, RejectRequest, HandleActionLink | partial (happy + some scope/conflict) |
| User | GetUsers, GetUserPreferences, UpdateUserPreferences | only UpdateUserPreferences (2 partial) |
| UserInfo | GetUserInfo, GetExtendedUserInfo | GetUserInfo basic; **GetExtendedUserInfo: none** |
| Project (read) | GetProjects, GetProject, GetExtendedProject, GetProjectUsers, GetProjectTemplates, GetValidRoles | only GetProjectTemplates (1/15), GetValidRoles (3/8) |
| Project (tag write) | CreateProjectTag, AssignProjectTag, UnassignProjectTag | aggregate covered; endpoint partial |
| Project (lifecycle/user write) | UpdateProject, CreateProject, DeleteProject, AssignProjectUser, UnassignProjectUser, ChangeProjectUserRole | aggregate strong (~38); ~9 endpoint gaps |
| Country / Statistics / Resource | GetCountries, GetStatistics, GetResources, GetResource | 8 tests (see §4) |

## 2. Genuine findings (decide before/while testing — not just coverage)

| # | Endpoint | Finding | Verdict | Action |
|---|---|---|---|---|
| F1 | GetCountries | No-match returns `200 []`; legacy returned `400`. | Intentional behavioral change (verified live). | Regression test pinning `200 []`. |
| F2 | GetPublicRequestInfo | Anonymous endpoint returns requester **UserName + Email**; `RequestId` is a GUID. | Legacy parity, but a PII/enumeration surface. | **Product decision** — confirm intended; otherwise tighten. |
| F3 | ChangeProjectUserRole | `requesterLevel` defaults to `0` when caller has no project role. | **Fail-closed** (denies all) + scope gate already applies — not a bypass. | Regression test: no-role requester → 403. |
| F4 | RejectRequest | No cache eviction (Accept evicts). | **Correct by design** (reject changes no membership). | None. |
| F5 | GetProjectUsers | Unknown `RoleId` silently maps to `RoleName = ""`. | Silent data degradation. | Test the fallback; consider logging. |
| F6 | GetVersion | Reads `APP_VERSION` via `Environment.GetEnvironmentVariable` (not injectable). | Not unit-testable as-is. | Integration test, or introduce small provider abstraction. |
| F7 | GetExtendedUserInfo | Zero endpoint tests; non-trivial resource grouping/location nesting. | Coverage gap on a complex projection. | Baseline + projection tests (P1). |

## 3. Prioritized test implementation plan

### P1 — uncovered critical branches (auth/scope, write status codes, complex reads)

**Project writes (endpoint-level; aggregate already strong):**
- AssignProjectUser: `409` user already assigned; `409` ReadOnly.
- UnassignProjectUser: `404` user not assigned; `409` ReadOnly.
- ChangeProjectUserRole: `409` ReadOnly; `403` role below member baseline; `403` requester with no project role (F3 regression).
- UpdateProject: `400` invalid email; `400` description > 500.
- CreateProject: `400` description > 500; `502` role service failure.

**Project reads (currently 0 tests each):**
- GetProjects: `400` per invalid filter (shortName/email/startDate/endDate); `200 []` empty scope.
- GetProject: `404` out of scope; `404` not found; `200` in-scope found.
- GetExtendedProject: `404` out of scope; `404` not found; empty-resources; full tree.
- GetProjectUsers: `404` out of scope; `502` role service failure; unknown-RoleId fallback (F5).
- GetProjectTemplates (**highest-value read** — HEAppE orchestration): `401` missing/empty bearer; `404` out of scope / project not found; `200 []` no links / all outside date window; HEAppE failure modes (dead host, null, non-success, timeout) → `200` unavailable entries + cache-suppress; merge/dedup of duplicate links.
- GetValidRoles: finish the 5 gaps — `502`; member-baseline filter; multi-role highest-level; strictly-below boundary (level==level excluded); permission mapping.

**Request writes:**
- CreateRequest: `400` invalid ProjectShortName; `404` requester/project not found; verify action-link generation + owner notification + cache evict.
- AcceptRequest: `502` role service / member role missing; UserInfo cache evict; requester notification.
- HandleActionLink: sibling-link single-use invalidation; `400` request null / non-self-onboarding / null shortName; `502` accept role-service failure.

**Request reads (0 endpoint tests):**
- GetPublicRequestInfo: `422` not found / not pending (state 4,5) / expired (`ValidTo` boundary) / wrong type; `200` happy path with name+email (F2).
- GetRequests: scope gating (`200 []` empty scope; out-of-scope project → `[]`); each filter (ProjectId/UserId/State); invalid State int handling.
- GetSentRequests: sender-only filter (`SenderIdpId == caller`); state filter; different caller → `[]`.

**UserInfo / User:**
- GetExtendedUserInfo (F7): baseline `200`; resource→project grouping; empty-resource project role; location nesting.
- UpdateUserPreferences: directory-lookup `404` (distinct from aggregate `404`); cache eviction verify; create vs update path; self-only authorization question.
- GetUsers: `400` ProjectShortName too short/long; `200 []` no match; null IdpUserId filter ignored.

**Country / Statistics / Resource (from earlier round):**
- Country: null filter → all; no-match → `200 []` (F1); empty-string filter → all.
- Statistics: `to<=from` correction; `to==from` expansion; `from>today` → empty weekly + non-zero totals; single-bound default.
- WeeklyBuckets: long range bounded by today; Sunday-midnight lower-bound exclusion.
- Resources: locationTypeId in-memory filter (keep + return all assignments / exclude); empty scope → `[]`; by-id out-of-scope → `404`; ReadOnly/Unspecified operation-mode mapping.

### P2 — secondary branches & reference reads
- Provider (GetProviders/GetProvider): name filter exact-match, blank-name ignored, countryId filter, AND-combine, empty result, `404` not found, name-validation error path.
- Location (GetLocations/GetLocation): provider/locationTypeId filters + AND, empty-providerName guard, `404` not found / soft-deleted.
- ProjectTag reads: filter match/no-match/null; by-id `404` not found / deleted (note: RBAC currently blocks the by-id GET — confirm).
- Enums (×5): assert full enum/static-list contents + ordering (RequestStates, RequestTypes hardcoded — pin values).
- GetProjectDomainTypes: data / empty / (query failure).
- ProjectTag writes: TagName > 100 → `400`; cache-evict verify; endpoint-level idempotence (assign existing / unassign absent).

### P3 — low value
- GetVersion (F6): integration test with env set, or add provider abstraction first.
- Trivial response-field mapping where a happy-path test already exercises the projection.

## 4. Notes on test mechanics

- Test naming `Should_<Expected>_When_<Context>`; unit tests drive `HandleAsync` directly with mocked
  ports (`IRepository`, `I*Query`, `IProjectRoleHandler`, `IRoleListService`, `IOutputCacheInvalidator`,
  `INotificationSender`, `TimeProvider`). Aggregate invariants tested directly on entities (no mocks).
- Query-internal branches (EF joins, scope SQL, soft-delete filters, `DistinctBy`, date SQL) are **not**
  reachable from endpoint unit tests with a mocked `I*Query` — they need integration tests against a real
  / in-memory DB. The Country/Statistics/Resource live verification already exercised several of these.
- Cache policies (`CacheOutput`, vary-by, ETag/304, storage suppression) are middleware behavior — verify
  via integration tests, not endpoint unit tests.
- Test runner is xUnit v3 / MTP: run `dotnet test <csproj>` without `--filter` (filters yield 0 tests).
- Use `DateTimeKind.Utc` and `.ConfigureAwait(false)` consistently (matches existing tests).
