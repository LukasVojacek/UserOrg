# PRD — Core: claims-first caller resolution & read-path deepening

> Local PRD document (not published to an issue tracker).
> Vocabulary follows [CONTEXT.md](../../CONTEXT.md). Decision recorded in [ADR-0006](../architecture/adr/0006-core-trusts-internal-token-claims.md).

## Context

An architecture review of `src/Modules/Core` surfaced five shallow spots where the same interface is re-stated inline across endpoints. Grilling against the codebase reframed the two largest (caller scope, effective roles) around a single root finding: **the Gateway's internal token already carries the caller's identity (`sub` → `IdpId`), effective `role`, and `projectRoles` (role → project short-names) — and Core ignores them, re-deriving the same data from SQL and the Forge role service on every request.** This PRD makes Core **claims-first**, removing the duplicated work, and folds in the remaining review candidates (value-object binding, the write-aggregate leak on single reads, and the UserInfo folder unification).

## Problem Statement

Today, every authenticated read in Core re-computes information the Gateway already resolved and embedded in the internal token:

- Each scoped endpoint re-derives the caller inline: `IdpId → UserId → project IDs`, with its own ad-hoc `401`/`404` wording, duplicated across endpoints.
- The UserInfo endpoints fetch the caller's permissions from Forge **twice** per request — once to build the output-cache key, once to build the body — when the same `role`/`projectRoles` data is already a validated claim.
- Typed value objects (`ProjectShortName`, `Email`) are bindable (`IParsable`) but each endpoint still re-hand-rolls `TryParse → ValidationProblem`.
- `GetProject` reads through the **write** port `IRepository<Project>` and serialises the aggregate to the wire — the only read that crosses into the write model.
- Single-project reads apply **no resource-level scope check**: any authenticated caller can read any project by short-name, while the list endpoints correctly scope to the caller.
- `UserInfo` lives under a second folder root (`Endpoints/UserInfo/.Base/.Extended`) with an older convention, while everything else is `Features/<X>/<Verb>`.

The result is duplicated I/O, inconsistent authorization, a leaked write model, and a module that is harder to navigate and test.

## Solution

Core treats the **internal token** as the source of truth for the **caller** — identity, effective role, and **caller scope** — and reads them from claims instead of re-querying. A single `Caller` value object, bound from the request, replaces the inline `IdpId → UserId → scope` chains. The `v2` endpoint group is placed behind authorization so handlers receive a guaranteed-present `Caller`. Read endpoints filter by `IdpId` and project short-name; the redundant SQL scope query and the duplicate Forge fetch are deleted. Value-object route parameters bind automatically. `GetProject` returns a read DTO and, like the other single-project reads, is scoped to the caller. `UserInfo` moves under `Features/` and is reworked to read its role/scope from claims, calling the role service only to expand a role name into its permission list.

From the caller's perspective nothing changes for valid requests, except that a caller can no longer read a single project they have no role on.

## User Stories

1. As an authenticated caller, I want my identity and project access taken from the token I already present, so that the API doesn't redundantly re-query the database on every request.
2. As an authenticated caller, I want list endpoints to return only the projects in my caller scope, so that I see exactly what I'm entitled to.
3. As an authenticated caller, I want to be denied (404) when I request a single project I have no role on, so that resource access is consistent with the list endpoints.
4. As a frontend calling `userinfo` on login, I want my user info returned correctly with my roles and permissions, so that the app can initialise my session.
5. As a frontend, I want `userinfo` and `UserInfo/Extended` to remain backward-compatible in shape, so that no client change is required.
6. As an unauthenticated client, I want a `401` before any handler runs, so that protected data is never reachable without a valid token.
7. As an API consumer, I want malformed route identifiers to return an RFC-9457 ProblemDetails `400`, so that errors are machine-readable and consistent.
8. As an API consumer of the multi-field project filter, I want to know which filter field was invalid, so that I can correct my query — these endpoints keep a field-named validation response.
9. As a caller whose roles changed, I want the change reflected once a fresh internal token is issued, so that access tracks my current permissions within the token lifetime.
10. As a maintainer, I want one `Caller` module that owns claim parsing, so that caller-resolution bugs concentrate in a single place.
11. As a maintainer, I want the `401` contract expressed once at the group level, so that it isn't restated in every handler.
12. As a maintainer, I want endpoints to receive a typed `Caller` parameter, so that handlers read as honest functions of their inputs.
13. As a maintainer, I want single-project read endpoints to share the read seam (`IProjectQuery` + read DTO), so that the write aggregate stops leaking to the wire.
14. As a maintainer, I want value-object route parameters to bind via `IParsable`, so that endpoints stop hand-rolling parse-and-400.
15. As a maintainer, I want `UserInfo` under the single `Features/<X>/<Verb>` convention, so that the module has one navigable layout.
16. As a maintainer, I want the UserInfo output-cache key derived from the `projectRoles` claim, so that the cache varies correctly without a second Forge call.
17. As a maintainer, I want the static multi-parameter `UserInfoLoader` replaced by a use-case reading the `Caller`, so that the read path is testable without HTTP plumbing.
18. As a maintainer, I want `GetUserProjectIdsAsync` and `GetUserIdByIdpIdAsync` removed once unused, so that no dead read paths remain.
19. As a developer writing a new scoped endpoint, I want to add a `Caller` parameter and filter by `caller.Scope`, so that I get caller resolution for free.
20. As a developer writing a unit test, I want to construct a `Caller` directly from values, so that I can test handlers without a `ClaimsPrincipal` or DI.
21. As a security reviewer, I want resource-level scope enforced in Core and endpoint-level permission enforced at the Gateway, so that the authorization split is explicit and defensible.
22. As a security reviewer, I want the `v2` group to require authorization in Core, so that defence-in-depth doesn't rely solely on the Gateway.
23. As an operator, I want one fewer SQL scope query and one fewer Forge round-trip per request, so that read latency and load drop.
24. As a caller authenticating by a non-JWT method, I want predictable behavior given an empty `projectRoles` claim, so that I either receive the populated claim or legitimately resolve to empty scope.

## Implementation Decisions

- **Claims-first source of truth (ADR-0006).** Core trusts the internal token's `sub`, `role`, and `projectRoles` claims for identity, effective role, and caller scope. No Gateway change — these claims already exist on the token (the Gateway builds the `role` and `projectRoles` claims from the same `ToProjectRecords` logic Core's `UserInfoLoader` re-derives today). The role service is still used by UserInfo to expand a role name into its full permission list (not present in the token).
- **`Caller` value object as the seam.** A new immutable `Caller` exposing `IdpId`, effective `Role`, optional display `Name`, `ProjectRoles` (role → set of `ProjectShortName`), and a derived `Scope` (flat set of `ProjectShortName`). Resolved through a static `BindAsync(HttpContext)` so it appears as an endpoint parameter; built from the validated `ClaimsPrincipal`, including a reader for the `projectRoles` JSON claim. Mirrors the existing `IdpId.From(HttpContext)` idiom.
- **Authorization gate.** The `v2` endpoint group gains `RequireAuthorization()` (matching the existing `api` group convention). Unauthenticated requests are rejected by middleware before any handler; the `Caller` parameter is therefore non-nullable and handlers drop the `idpId == default` check. Closes the current gap where v2 endpoints are mapped without an auth gate.
- **Read-query re-keying.** Caller scope is expressed as project **short-names**. `ProjectFilter` / `RequestFilter` scope by `ProjectShortName` (not `ProjectId` GUIDs). Per-caller filters (e.g. requests sent by the caller) join `User` on `IdentityProviderUserId`, removing the `IdpId → UserId` round-trip. A new `IProjectQuery.GetProjectAsync(ProjectShortName)` read method plus a read DTO backs `GetProject`.
- **Value-object binding.** Route-id parameters bind via the value objects' existing `IParsable` implementations; the manual `TryParse → ValidationProblem` blocks for route ids are removed. Invalid input yields a generic ProblemDetails `400` (rendered by the existing `AddProblemDetails` + `UseStatusCodePages` wiring). Multi-field query-filter endpoints keep explicit `Results.ValidationProblem` to retain the field-named `errors` dictionary.
- **Single-project reads.** `GetProject` (and `GetExtendedProject`, `GetProjectUsers`) route through `IProjectQuery` read DTOs and return `404` when the short-name is not in `caller.Scope`. Resource-level scope is a Core responsibility; endpoint-level permission remains a Gateway responsibility.
- **UserInfo rework + relocation.** `Endpoints/UserInfo/**` moves to `Features/UserInfo/**` (`Core.Features.UserInfo.*`). The output-cache policy derives its vary-by key by hashing the `projectRoles` claim instead of calling the role service. The static `UserInfoLoader` becomes a use-case that reads the `Caller`, loads user details via `IUserInfoQuery`, and calls the role service only for role→permission expansion. The duplicate permission fetch is eliminated.
- **Cleanup.** `IProjectQuery.GetUserProjectIdsAsync` and `IUserInfoQuery.GetUserIdByIdpIdAsync` are removed once no callers remain.
- **Non-JWT auth constraint.** `projectRoles` is only populated for `JwtBearerToken` today; any non-JWT method that must reach a scoped endpoint either populates the claim at the Gateway or legitimately resolves to empty scope. (Tracked as an ADR-0006 consequence; no Gateway work in this PRD unless a non-JWT path to a scoped endpoint is confirmed.)

## Testing Decisions

- **What makes a good test here:** assert external behavior through the highest existing seam — the endpoint handler — not internal parsing steps. Prefer constructing a `Caller` value and a fake `IProjectQuery`/`IRequestQuery`/`IUserInfoQuery` over driving HTTP, except where the auth gate or binding is itself under test.
- **`Caller` binding (new seam):** unit-test `Caller.BindAsync` against a constructed `ClaimsPrincipal` — sub→`IdpId`, `role`, and a `projectRoles` JSON claim → `ProjectRoles`/`Scope`; assert null when required claims are absent. This is the one genuinely new test surface.
- **Scoped read endpoints:** test `HandleAsync` directly with an in-scope vs out-of-scope `Caller` — in-scope returns data, out-of-scope returns `404`; scope filter passes the expected short-names to the fake query. Mirrors the existing Core endpoint tests in `tests/Modules/Core/`.
- **UserInfo use-case:** test that the response is built from claims + `IUserInfoQuery` + role→permission expansion, and that the role service is called **once** (no duplicate permission fetch). Snapshot the response shape to prove backward compatibility.
- **Auth gate:** an integration test (WebApplicationFactory, per the project's integration pattern) asserting an unauthenticated request to a `v2` route returns `401` before the handler.
- **Binding/400 contract:** integration test that a malformed route id returns a ProblemDetails `400`, and that the multi-field filter endpoint still returns a field-named validation response.
- **Mock only at Ports** (`IProjectQuery`, `IRequestQuery`, `IUserInfoQuery`, the role service); domain value objects (`Caller`, `ProjectShortName`) are constructed directly.

## Out of Scope

- Any change to the Gateway, the internal-token contract, or `TokenService` (the needed claims already exist).
- Adding the domain `UserId` or `ProjectId` GUIDs to the internal token (explicitly rejected in grilling in favour of keeping Core's primary key out of the auth boundary).
- Populating `projectRoles` for non-JWT auth methods (only flagged; addressed only if a non-JWT path to a scoped endpoint is confirmed).
- Migration-plan items beyond these five candidates: write-model purity, DataAccess duplicate-config collapse, composition rename `AddCoreIntegration`/`MapV2Endpoints` → `AddCore`/`MapCoreEndpoints`. These may be sequenced separately.
- Flattening read DTOs from domain value objects to primitives (open question in the migration plan).

## Further Notes

- ADR-0006 records the claims-first decision so future reviews don't re-suggest re-querying the DB. A second decision — resource-level scope enforced in Core vs endpoint-level permission at the Gateway — is a candidate for ADR-0007 (offered; pending).
- Suggested execution order (one commit per step, each independently verifiable): (A) `Caller` + `RequireAuthorization`; (B) read-query re-keying + `GetProjectAsync`; (C) endpoint migration per feature; (D) UserInfo move + claims-first rework; (E) delete unused query methods.
- Verification end-to-end: `dotnet build` (analyzers/style enforced), `dotnet test --no-build`, then run via `aspire run` and exercise `GET /v2/Projects`, `/v2/Projects/{shortName}` (in- and out-of-scope), `/v2/UserInfo`, and an unauthenticated request to confirm the `401` gate.
