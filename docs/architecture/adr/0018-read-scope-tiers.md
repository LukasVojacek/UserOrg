# ADR 0018 - Role-based read scoping: per-tier branching in the use-case, resolved by a shared precedence helper

- Date: 2026-06-10
- Status: accepted

## Decision

Every v2 **read** endpoint that more than one kind of caller can invoke scopes the data it returns by a **read-scope tier** resolved from the caller, with three tiers in strict precedence:

- **Admin** — `caller.Role` equals the admin role (`WellKnownRoles.DefaultAdminRoleName`). Sees **everything**; no scope filter.
- **Provider identity** — the internal token carries a `providerName` claim (`caller.ProviderName is { }`). Sees only **that provider's** data. The scoping key is the **`providerName` claim**, the same key writes use (ADR-0011) — **never** the `ProviderAdmin` role name.
- **Caller** — otherwise. Sees only the projects in the caller's scope (`caller.Scope`, flattened from the `projectRoles` claim). An empty scope sees nothing.

The tier is computed by one shared resolver in `Core/Shared/Auth/ReadScope.cs`: a closed discriminated union `ReadScope` (`All` / `Provider(ProviderName)` / `Scoped(IReadOnlySet<ProjectShortName>)`) plus `caller.ResolveReadScope(roles)` and `caller.IsAdmin(roles)`. The precedence (admin > provider identity > caller scope) mirrors `ProviderWriteAuthorization.Authorize` exactly, so reads and writes make the same trust decision in the same order.

Each endpoint **branches explicitly in `HandleAsync`** on the resolved `ReadScope`, translating the tier into its query filter, while returning the **same Response shape** regardless of tier. The authorization/scoping decision is therefore visible at the use-case boundary (where `ProviderWriteAuthorization` already lives), not hidden in the query layer. **Admin "sees all" is expressed as a no-op scope** (a `null` scope input the filters already treat as "no restriction"), not a separate `/administration/*` endpoint.

In the data-access layer, **scope resolution is separated from scalar loading**: a query first resolves the set of entity ids the tier may see (e.g. resource ids via the caller's projects, or via the provider's locations), then loads rows constrained to those ids — *without* an inner join through projects. This is what lets the admin and provider tiers see rows that are not attached to any project (e.g. a provider's resource with no project assignment), matching legacy.

`ApiAudience` (the `.WithGroupName(...)` label) is **not** part of this: it only chooses which OpenAPI document describes an endpoint. Who may call it is the Gateway's RBAC; how its data is scoped is this tier.

## Context

- A migration audit (F0.0, see [f0-role-based-reads.md](../../ddd-migration/f0-role-based-reads.md)) found v2 reads split two opposite ways against legacy parity:
  - `GetProjects` / `GetResources` / `GetRequests` were **always caller-scoped** with no admin bypass — an admin with no project roles saw **zero** rows, while legacy `Administration/*` returned everything. Eight `Administration/*` GET routes were falsely marked ported.
  - `GetProviders` / `GetLocations` were **not scoped at all** — they returned every row, **including private providers/locations**, to any role-holder. Legacy returned only public/own (or project-reachable). This was a parity regression *and* a private-data exposure.
- Legacy scoping was per-tier and granular: `ProjectResourceService` had an explicit `isAdmin` bypass; `ProviderService` split `GetPublicProvidersAsync` from `GetRelevantProvidersAsync` (project-reachable, including private); `LocationService` scoped by provider key or by the user's projects.
- The provider **write** path (ADR-0011) already established claims-only, name-keyed scoping: the actor is a provider service-key identity carrying a `providerName` claim, not an interactive `ProviderAdmin` user. Reads must use the **same** key, or reads and writes would disagree about who "is" a provider. An earlier draft that keyed the read tier on the `ProviderAdmin` role name conflicted with this and with the CONTEXT.md glossary; it was corrected.
- RBAC already lets Admin (all permissions) and `ProviderAdmin` (`*:list`) call the branch read routes, so widening the data is reachable without RBAC changes; `GetRequests` is the exception (no `user:request:manage` for a provider identity), which is why it has no provider tier.

## Considered options

- **Per-tier branch in the use-case, shared `ReadScope` resolver (chosen).** Precedence lives in one tested place; handlers stay explicit and auditable next to `ProviderWriteAuthorization`; the Response contract is identical across tiers; admin is a no-op scope. Cost: each multi-tier endpoint carries a small branch, and some queries need a provider/relevant scope path that did not exist read-side.
- **Implicit scoping in the query layer** (filter inspects the caller). Rejected: hides the authorization decision from the use-case boundary, making "who sees what" invisible at review time and easy to get wrong per-query.
- **Separate `/v2/administration/*` admin-wide endpoints** (mirroring legacy controllers). Rejected: duplicates every read endpoint and its Response/DI wiring for a pure data-scope difference; the tier branch is far cheaper and keeps one contract per resource.
- **Scattered `if (caller.IsAdmin) … else if (providerName) …` in each handler, no shared union.** Rejected: re-implements the three-way precedence five times; one handler getting the order wrong (checking provider before admin) is a silent over/under-exposure. The closed union makes the `switch` exhaustive and tests it once.
- **Key the provider tier on the `ProviderAdmin` role name.** Rejected: contradicts ADR-0011 and the glossary — the role name is not the scoping mechanism; the `providerName` service-key claim is.

## Consequences

- **Precedence is defined once.** `ReadScope` + `ResolveReadScope` are the single source of the admin > provider > caller order; endpoints and any future scoped read reuse them. A bare user with no projects resolves to `Scoped(empty)` — sees nothing — not `All`.
- **Queries gained tier inputs and a scope/scalar split.** `ProviderFilter.OnlyPublic`, `LocationFilter.ScopeShortNames`, and `ResourceFilter` (nullable `ScopeShortNames` + `ScopeProviderName`) express the tiers; `ResourceDetailsQuery` resolves candidate ids per tier before loading scalars so project-less rows survive for admin/provider.
- **Soft-delete must be explicit on link entities.** `AssignedLocation` and `AssignedProjectResourceToProject` have **no** global query filter, so every tier query filters `!IsDeleted` by hand. Missing one of these is a data leak — review found and fixed exactly such a pre-existing case in the resource assignment load.
- **GetProviders user tier = "relevant", not public-only** (decided 2026-06-10). To match legacy `GetRelevantProvidersAsync`, the caller tier returns public **plus** private providers reachable through the caller's own projects; otherwise a user would lose sight of the private provider hosting their project (a v1 regression). This requires a "providers reachable via projects" query (the inverse of the location-scope chain). `ProviderFilter.OnlyPublic` alone does not cover this tier.
- **By-id reads need the same tiering.** `GetResource` / `GetProvider` / `GetLocation` (single-item) must branch on the tier too, not only the list endpoints — the legacy `Administration/*` set included `Provider/{id}` and `Location/{id}`.
- **Depends on the provider identity carrying both the `providerName` claim and a role with `*:list` permissions.** The claim drives the data scope; the role drives RBAC reachability. This holds for the established provider service-key flow (ADR-0011); a provider key with a different role lacking `*:list` would pass the scope branch but be blocked at the gate.
- **Rollout is incremental.** F0.1 shipped the resolver; F0.2 shipped the query/filter capability with EF in-memory tests; the per-endpoint branches (F0.3) are where the user-facing behavior — including closing the private-data exposure — actually flips. Until then the filters carry the new inputs but endpoints still pass their legacy values.

## Amendment (2026-06-15) — existence-hiding rider for the v2 error contract

ADR-0020 makes every failure an endpoint-owned `ProblemDetails` carrying a stable `code`. That freedom can
silently undo the existence-hiding this ADR relies on, so it is constrained:

**Within a single endpoint, "resource genuinely missing" and "resource hidden because it is outside the
caller's read scope" MUST emit a byte-identical `ProblemDetails`** — same status (`404`), same `code`, same
`title`/`detail`. If the scope-hidden case returned a distinct code (e.g. `*.forbidden`) while the missing
case returned `*.not_found`, the *difference itself* becomes an enumeration oracle: an authenticated caller
with no scope could probe guessable identifiers — project **short-names** especially are short and
human-chosen — and map which ones exist without ever being authorized to view one. The 404-for-out-of-scope
choice (this ADR) only hides existence if the response is indistinguishable from a true 404.

A distinct case is **the caller's own identity not being provisioned** in Core (`userDirectory.FindUserIdAsync`
returns null): that is about the caller, not a hidden resource, so it may carry its own code safely.

The shared `Problems.NotFound` helper takes the same code for both the missing and scope-hidden paths, and
this rule is a code-review checklist item for every v2 read endpoint.

## Amendment (2026-06-15) — status-observability exclusion rule

A list read **excludes a row whose related entity's status the response cannot express; if the response
surfaces that status, the row may be kept.** Driven by a concrete bug: after the read queries moved to
root-first SQL paging, `GetLocations`/`GetResources` began *counting* a location/resource whose Provider was
deactivated (provider soft-delete does **not** cascade to its locations) and then **threw** on the
provider-name stitch (the inner join over the active-provider set found no row). The pre-refactor single
inner-join query had silently *dropped* those rows — never shown, never counted.

Because `LocationSummary`/`ResourceSummary` carry no field expressing "the provider behind this is
deactivated," the consumer could not interpret such a row, so the correct behaviour is to **exclude it at the
root** (constrain the paged root query to active-provider rows via `EXISTS`), keeping count, page, and stitch
consistent and restoring the pre-refactor result set. Should a future response add a provider-status field,
the same rule permits keeping the rows instead. The principle: never surface a row whose degraded/ inactive
relationship the client cannot see and reason about.

## Amendment (2026-06-16) — tier adoption deferred; v2 reads stay user-scoped

**Superseded (2026-09-29).** The tier branches are adopted. `caller.ResolveReadScope`
(`Core/Shared/Auth/ReadScope.cs`) has six callers: `GetResources`, `GetLocations`, `GetLocationsProviderAdmin`,
`GetProjectsProviderAdmin`, `GetProviders` and `GetProvidersProviderAdmin`. The by-natural-key reads
`GetProvider`, `GetLocation` and `GetResource` reach it through `caller.ResolveProjectScopeFor` (`ReadScope.cs:52-53`).
The text below is kept as history.

The per-endpoint tier branches (F0.3 above) are **deliberately deferred, not merely pending**. As of this
date every v2 read endpoint resolves its scope from `caller.Scope` only — the **caller tier** — and does
**not** branch on admin or provider-identity tiers. Concretely: `caller.RequireProjectScope` and the list
filters are fed `caller.Scope` unconditionally; the `ReadScope` union and `caller.ResolveReadScope`
(`Core/Shared/Auth/ReadScope.cs`) remain in the tree **dormant, with zero callers**.

This is a scoping decision, recorded so an architecture review does not read the unused `ReadScope` resolver
as dead code to delete or an oversight to "finish" by wiring it in:

- **Why defer.** Widening reads to admin-sees-all and provider-sees-its-data is a behavioural and
  data-exposure change (it closes the private-data parity gap *and* opens cross-project visibility). It
  belongs to the provider/admin surface, which is out of the current user-space scope of work. Adopting a
  three-tier union while only one tier is ever exercised is a shallow seam — one real adapter, no variation
  across it.
- **What is kept.** `ReadScope` / `ResolveReadScope` and the tier inputs already on the query filters
  (`ResourceFilter.ScopeProviderName`, etc.) stay in place as the half-built seam this ADR's rollout
  describes; F0.3 completes them when the provider/admin surface is actually built.
- **Unchanged.** The existence-hiding rider (2026-06-15 amendment) still governs the caller tier: the shared
  `caller.RequireProjectScope` guard and the genuinely-missing path emit a byte-identical `project.not_found`
  `ProblemDetails`, and `caller.not_provisioned` keeps its own code.
