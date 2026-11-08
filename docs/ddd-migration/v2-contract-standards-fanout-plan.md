# v2 Contract Standards — Fan-out Implementation Plan

Repeatable per-aggregate playbook for rolling the [v2 HTTP contract standards](../architecture/adr/0020-v2-http-contract-standards.md)
across every refactored endpoint. Source findings: [minimal-api-audit.md](../minimal-api-audit.md).

This document covers the **fan-out** stage only. It assumes the two prior PRs are merged:

- **PR 1 — Foundation** ships the shared seam with *no* endpoint behavior change.
- **PR 2 — Pilot (Project)** applies the full standard to the `Project` aggregate end-to-end and produces the
  reusable contract-test helpers. Each fan-out PR copies the Project slice as its template.

> If PR 1 / PR 2 are not yet merged, stop — fan-out is mechanical *only because* the seam and the template
> exist. Building them mid-fan-out defeats the staging.

---

## What the foundation gives you (the toolbox)

Every fan-out edit is expressed in terms of these PR-1 primitives:

| Primitive | Location | Use |
|---|---|---|
| `Problems.NotFound(code, detail?)` | `Core/Shared/Problems.cs` | replaces every `Results.NotFound()` |
| `Problems.Conflict(code, detail)` | `Core/Shared/Problems.cs` | replaces `Results.Conflict("…")` + inline `Results.Problem(…,409)` |
| `Problems.Forbidden(code, detail?)` | `Core/Shared/Problems.cs` | replaces `Results.Forbid()` |
| `Problems.Validation(field, code)` | `Core/Shared/Problems.cs` | replaces `Results.ValidationProblem(…)` |
| `Problems.Unprocessable(code, detail)` | `Core/Shared/Problems.cs` | replaces inline `Results.Problem(…,422)` |
| `Problems.Upstream(code, detail)` | `Core/Shared/Problems.cs` | replaces `Results.Problem(…,502)` |
| `error.ToProblem()` | `Core/Shared/DomainErrorResults.cs` | already emits `extensions["code"]` after PR 1 |
| `PagedResult<T>` / `PageMeta` | `Core/Shared/Paging/` | the uniform list envelope |
| `IDataQuery.ToPagedListAsync(...)` | `Core.DataAccess` (paged wrapper) | pushes `Count` + `Skip/Take` to SQL |
| `PageRequest` (`[AsParameters]`) | `Core/Shared/Paging/` | binds `?page=&limit=` with default + max cap |
| `RequestState.Name` / `Parse` | `Core.Domain/Request/` | enum name ↔ int (Request aggregate only) |

All failures carry `requestId` automatically (PR 1 configured `CustomizeProblemDetails`); no per-endpoint work.

---

## The per-endpoint transformation recipe

Apply the matching transforms to each endpoint. Every transform is local to one endpoint file (+ its query
for pagination).

> **Superseded — `PagedResult<T>` body envelope.** *Every* mention of `PagedResult<T>` / `PageMeta` in this
> document is dead: the toolbox row above, T5's `Results.Ok(PagedResult.Offset(...))` /
> `PagedResult.Complete(...)` recipe, T7's `.Produces<PagedResult<Response>>`, the **T5-reference**
> annotations in the fan-out PR table, and DoD items 2 and 3. The
> [2026-06-16 amendment](../architecture/adr/0020-v2-http-contract-standards.md#amendment-2026-06-16--pagination-moves-from-the-body-envelope-to-http-headers)
> reverses them: a list body is now a **bare array**, with paging metadata carried in `Link` / `X-Total-Count`
> headers.
> **Live replacement:** `PagedResponse.Offset(http, data, page, limit, totalItems)` and
> `PagedResponse.Complete(http, data)` in
> [`Core/Shared/Paging/PagedResponse.cs`](../../src/Modules/Core/Shared/Paging/PagedResponse.cs) — both set
> the headers and return the bare array; document them with `.Produces<IReadOnlyList<Response>>`.
> Those sections are recorded debt — not rewritten here; read them through this amendment.

### T1 — Bare-string conflict → coded ProblemDetails
```csharp
// before
return Results.Conflict($"A provider named '{name.Value}' already exists.");
// after
return Problems.Conflict("provider.name_already_exists", $"A provider named '{name.Value}' already exists.");
```

### T2 — Empty NotFound → coded ProblemDetails (⚠ honor the ADR-0018 rider)
```csharp
// before
return Results.NotFound();
// after — genuine miss
return Problems.NotFound("request.not_found");
```
**Rider (security-critical):** when the *same* endpoint returns 404 for an out-of-scope resource, it MUST
emit the **identical** problem — same code, title, detail — so missing and scope-hidden are
indistinguishable ([ADR-0018 amendment](../architecture/adr/0018-read-scope-tiers.md)). Example
([AcceptRequest:44-55](../../src/Modules/Core/Features/Request/AcceptRequest/AcceptRequestEndpoint.cs)):
```csharp
var settled = await requests.FindAsync(RequestId.Create(request.RequestId), ct);
if (settled is null) return Problems.NotFound("request.not_found");          // genuine miss
...
if (shortName is null || !caller.Scope.Contains(shortName.Value))
    return Problems.NotFound("request.not_found");                            // scope-hidden — SAME problem
```
The "caller not provisioned" case is *not* a hidden resource and may carry its own code:
```csharp
var assignedBy = await userDirectory.FindUserIdAsync(caller.IdpId, ct);
if (assignedBy is null) return Problems.NotFound("caller.not_provisioned");   // safe to distinguish
```

### T3 — Empty Forbid → coded ProblemDetails
```csharp
// before
return Results.Forbid();
// after
return Problems.Forbidden("provider.write_forbidden",
    "This identity may not write to this provider.");
```

### T4 — Inline Results.Problem → Problems.*
```csharp
// before
return Results.Problem(detail: "Only self-onboarding…", statusCode: StatusCodes.Status422UnprocessableEntity);
// after
return Problems.Unprocessable("request.unsupported_type", "Only self-onboarding requests are accepted here.");
// upstream
return Problems.Upstream("upstream.role_service_unavailable", "Failed to retrieve roles.");
```

### T5 — List → uniform envelope
**Growable list** (honors paging):
```csharp
// signature: add PageRequest
internal static async Task<IResult> HandleAsync(
    [AsParameters] Request request, Caller caller,
    [AsParameters] PageRequest page,                 // ?page=&limit= (default+cap in PR1)
    [FromServices] IProviderQuery providers, CancellationToken ct)
{
    ...
    var paged = await providers.GetProvidersPagedAsync(filter, page, ct);   // query returns (items, total)
    var data = paged.Items.Select(Map).ToList();
    return Results.Ok(PagedResult.Offset(data, page, paged.Total));         // meta always present
}
```
**Reference list** (ignores paging, one complete page):
```csharp
var items = await query.GetCountriesAsync(filter, ct);
return Results.Ok(PagedResult.Complete(items.Select(Map).ToList()));        // page 1/1, hasNextPage:false
```
Single-item endpoints are **not** wrapped — return the object directly.

**Tie-breaker (in the query):** every paginated `ORDER BY` must end on a unique column.
```csharp
// RequestDetailsQuery: ProjectId is NOT unique →
return query.OrderBy(r => r.ProjectId).ThenBy(r => r.Id).Select(...);
```

### T6 — Request enums as string names (Request aggregate only)
```csharp
// response: int → name
internal sealed record Response(..., string Type, string State, ...);
... new Response(..., r.Type.Name, r.State.Name, ...)
// filter input: ?state=Pending
internal sealed record Request(..., [FromQuery] string? State);
if (req.State is { } s && !RequestState.TryParseName(s, out var state))
    return Problems.Validation("state", "request.invalid_state");
// CreateRequest body: accept the name
if (!RequestType.TryParseName(request.RequestType, out var type))
    return Problems.Validation("requestType", "request.invalid_type");
```

### T7 — Docs: declare problem responses
```csharp
// before
.Produces(StatusCodes.Status409Conflict)
// after
.ProducesProblem(StatusCodes.Status409Conflict)
.ProducesValidationProblem()
// list endpoints
.Produces<PagedResult<Response>>(StatusCodes.Status200OK)
```
Document enum-name meanings and "use `code`, do not parse the message" in `.WithDescription`.

### T8 — Caller uniformity + date binding (where applicable)
```csharp
// before (GetUsers / GetUserPreferences)
var callerId = Caller.ResolveIdpId(httpContext);
if (callerId == default) return Results.Problem("Invalid token", statusCode: 401);
// after — take Caller as a parameter (binding yields the standard challenge)
internal static async Task<IResult> HandleAsync(..., Caller caller, ...)

// before (GetProjects date filters)
[FromQuery] string? StartDate   // then DateTimeOffset.TryParse
// after
[FromQuery] DateTimeOffset? StartDate   // framework parses ISO 8601; document inclusive/exclusive bounds
```

### T9 — Primitive 200 body → object response
A `200` success body is a JSON **object or array**, never a bare scalar (`string` / `number` /
`boolean`). Exceptions: `204 No Content` and a binary download. Other 2xx statuses (e.g. a `201`
create response) are outside this transform's scope.
```csharp
// before
return Results.Content(version, "text/plain");
// ...
.Produces<string>(StatusCodes.Status200OK)
// after
return Results.Ok(new Response(version));
// ...
.Produces<Response>(StatusCodes.Status200OK)
```
A **bare array stays legal** — it is the list-body shape the 2026-06-16 amendment mandates
(see [`GetRequestTypesEndpoint.cs:21,40`](../../src/Modules/Core/Features/Enums/GetRequestTypes/GetRequestTypesEndpoint.cs)).
T9 forbids scalars only.

T9 is its own transform because T1 covers bare strings on the *error* path only and T5 covers
*list* bodies only: both scalar-body defects in issue #277 (`GetVersion`, `GetUserPreferences`)
were conformant under T1/T5/T7 alone. Swept by the
[sweep guards](#sweep-guards-run-at-the-end-of-fan-out) below; asserted by
`NoPrimitive200BodyCharacterizationTests` (`Tests/Core/Application.Tests/`).

---

## Fan-out PRs (one aggregate per PR)

Ordered so the richest error-shape aggregate goes first (exercises every `Problems.*` helper, surfacing any
foundation gap before the smaller PRs), then the one aggregate with enum work in isolation, then the rest.

### PR 3 — Provider (incl. Location writes + ResourceAssignment) — 12 endpoints
Richest variety: every `Forbid`, the bare-string `Conflict`, inline 400 `Problem`s.

| Endpoint | Transforms |
|---|---|
| `GetProvider` | T2, T7 |
| `GetProviders` (growable) | T5-growable, T7 |
| `CreateProvider` | T1, T3, T7 |
| `UpdateProvider` | T2, T3, T7 |
| `DeactivateProvider` / `ReactivateProvider` | T2, T3, T7 |
| `AddLocation` / `UpdateLocation` / `RemoveLocation` | T2, T3, T7 |
| `AssignResource` / `UpdateAssignment` / `RemoveAssignment` | T2, T3, T4 (400→`provider.*`), T7 |

### PR 4 — Request — 7 endpoints (the only enum work)
Isolates T6 + the security-critical 404-hiding rider + the `GetRequests` tie-breaker fix.

| Endpoint | Transforms | Notes |
|---|---|---|
| `GetRequests` (growable) | T5-growable, **T6**, T7 | **add `.ThenBy(r => r.Id)`** tie-breaker |
| `GetSentRequests` (growable) | T5-growable, T6, T7 | same tie-breaker check |
| `CreateRequest` | T2, T4, **T6** (body name), T7 | |
| `AcceptRequest` / `RejectRequest` | T2 **(rider!)**, T4, T7 | missing ≡ scope-hidden identical |
| `GetPublicRequestInfo` | T2, T7 | **`AllowAnonymous` — no `Caller`**; public-by-id, not scope-hidden |
| `HandleActionLink` | T2, T4, T7 | **`AllowAnonymous`**, action-link token flow |

### PR 5 — User / UserInfo — 5 endpoints
| Endpoint | Transforms |
|---|---|
| `GetUsers` (growable, Admin) | T5-growable, **T8** (inline 401), T7 |
| `GetUserPreferences` | **T8** (inline 401), T7 |
| `UpdateUserPreferences` | T2, T4 (400), T7 |
| `GetUserInfo` / `GetExtendedUserInfo` (single) | T2, T7 |

### PR 6 — Resource, Location, ProjectTag — read-mostly — 6 endpoints
| Endpoint | Transforms |
|---|---|
| `GetResources` / `GetLocations` / `GetProjectTags` (growable) | T5-growable, T7 |
| `GetResource` / `GetLocation` / `GetProjectTag` (single) | T2, T7 |

### PR 7 — Reference/enum lists + misc singles — envelope-wrap only
`GetCountries`, `GetRequestStates`, `GetRequestTypes`, `GetOperationModes`, `GetLocationTypes`,
`GetProjectDomainTypes`, `GetValidRoles`, `GetProjectTemplates` → **T5-reference** (`PagedResult.Complete`),
T7. `GetStatistics`, `GetVersion` → no list change; T7 only.

---

## Tests (every PR)

Reuse the contract-assertion helpers built in the Project pilot; add one case per endpoint:

- **Error shape:** each failure path returns `ProblemDetails` with the expected `extensions["code"]` and
  status. Assert there are **no** bare-string / empty error bodies left in the aggregate.
- **404-hiding (Request, Provider reads):** a request for a genuinely-missing id and a request for an
  existing-but-out-of-scope id return **byte-identical** bodies. This is the highest-value test in the fan-out.
- **Pagination:** `meta` present on every list; growable list respects `limit`, caps at max, `hasNextPage`
  correct across a 2-page fixture; reference list returns `page:1,totalPages:1,hasNextPage:false`.
- **Stable order:** paging a fixture with duplicate sort-key rows (e.g. many requests on one project) returns
  no dupes/gaps across pages — guards the tie-breaker.
- **Enum round-trip (Request):** `?state=Pending` filters correctly; response emits the name; unknown name →
  `request.invalid_state` validation problem.

Layering per [CLAUDE.md](../../CLAUDE.md): domain logic (VO `Name`/`Parse`) is unit-tested directly; endpoint
contract behavior is integration-tested, mocking at the `IXxxQuery` / repository ports.

---

## Definition of done (per fan-out PR)

1. Every transform in the aggregate's table applied; **zero** `Results.Conflict("…")`, `Results.NotFound()`,
   `Results.Forbid()`, or inline `Results.Problem(...)` remain in the aggregate's `Features/<Aggregate>/`.
2. Every list endpoint returns `PagedResult<T>`; growable ones bind `PageRequest`; queries have a unique
   tie-breaker.
3. Docs use `.ProducesProblem` / `.ProducesValidationProblem`; list `Produces<PagedResult<…>>`.
4. Contract tests above pass; `dotnet format --verify-no-changes` and `dotnet build` (analyzers) clean.
5. No domain/data-access signature changes beyond the agreed paged query variants and the Request VO names.

### Sweep guards (run at the end of fan-out)
```bash
# should return nothing under src/Modules/Core/Features after all PRs
rg "Results\.(Conflict\(\"|NotFound\(\)|Forbid\(\))" src/Modules/Core/Features
rg "Results\.Problem\(" src/Modules/Core/Features
rg "\.Produces\(StatusCodes\.Status(4|5)" src/Modules/Core/Features   # should be ProducesProblem
rg "Results\.(Content|Text)\(" src/Modules/Core/Features               # T9 — no primitive 200 body
rg "Produces<string>" src/Modules/Core/Features                        # T9 — no primitive 200 body
```
These `rg` lines are a fast pre-check, not the guard. A scalar returned through `Results.Ok(someString)`
is not reliably matchable by regex; `NoPrimitive200BodyCharacterizationTests` reads the generated OpenAPI
documents and catches it however the endpoint spells it. The test is the authority for T9.

---

## Risks & gotchas

- **Anonymous public flows** (`GetPublicRequestInfo`, `HandleActionLink`) must **not** gain a `Caller`
  parameter and are **not** subject to scope-hiding — they're reachable by id/token by design. Standardize
  their error *shape* only.
- **Output cache / ETags:** paged list responses are cache-keyed by query — confirm `page`/`limit` are part
  of the cache key (`Core/Shared/OutputCache`) so page 2 isn't served from page 1's entry.
- **Read-scope tiers (ADR-0018):** pagination composes *after* the tier filter. `totalItems` must reflect the
  scoped set, not the global table — keep `Count` inside the same filtered `IQueryable`.
- **The enum rename touches inputs:** existing v2 callers (none in prod — v2 unreleased) and any internal
  test fixtures that POST `RequestType` as an int must switch to the name; grep tests before PR 4.
- **`GetUsers` is Admin-grouped** (`.WithGroupName(ApiAudience.Admin)`) — preserve that tag through the edit.
