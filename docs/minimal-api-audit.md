# Minimal API Contract Audit

**Scope:** Refactored **v2 Core** endpoints under `src/Modules/Core/Features/**` (~51 endpoints, all mapped under `MapGroup("api/v2")`), plus shared infrastructure — `src/Core/API/Program.cs`, `src/Modules/Core/Shared/DomainErrorResults.cs`, `src/Modules/Core/Shared/Auth/*`, `src/Core/API/GlobalExceptionHandler.cs`.
**Assumed API type:** **Internal** — the Gateway fronts Core, enforces RBAC, and issues the internal JWT; the consumers are the org's own frontend + services. Severity is calibrated accordingly (versioning is already handled, so breaking-change risk is low; consumer-ergonomics issues still count).
**Target convention:** RFC 9457 `ProblemDetails`.

## Summary

The v2 surface is well-structured and **far healthier than typical** — one vertical slice per use-case, `TypedResults`-style status codes used correctly (409/422/404/201/204 all appear in the right places), versioning already done (`api/v2` with legacy specs preserved), and documentation present on every endpoint via `MapDocumentation()`.

The dominant root cause of the findings is a single one: **errors and lists were built endpoint-by-endpoint without a shared response contract.** Domain errors carry stable machine codes (`project.*`, `provider.*`, `request.*`) but the shared mapper throws them away before they reach the client; error bodies come in four different shapes (ProblemDetails / bare string / empty / inline `Problem`); and every list endpoint returns a bare unbounded array with no pagination, sort, or cap. None of these corrupt data, so there are no Criticals — but they force consumers to string-match error text and hand-roll a different adapter per list.

| Severity | Count |
|---|---|
| Critical | 0 |
| High | 2 |
| Medium | 5 |
| Low | 2 |

## Findings

### [HIGH] Errors carry no stable machine-readable code — Check 2: Error messages written for nobody
**Where:** `src/Modules/Core/Shared/DomainErrorResults.cs:50` (systemic — every write endpoint routes domain failures through `.ToProblem()`)
**What the consumer must guess:** *which* failure happened. The status code is shared across many causes (e.g. 409 covers `ReadOnly`, `UserAlreadyAssigned`, `DuplicateProjectOwner`, `NameAlreadyExists`, `InvalidStateTransition`…), so to branch on the cause a client must string-match the human-readable `detail` — which breaks on copy-edits and i18n.
**Evidence:**
```csharp
// DomainErrorResults.cs — the code is used to pick a status, then discarded
var status = error.Code switch { /* project.* / provider.* / request.* → 4xx */ };
return Results.Problem(detail: error.Description, statusCode: status);  // error.Code never surfaces
```
The stable code already exists (`error.Code`, e.g. `ProjectErrorCodes.UserAlreadyAssigned`) and is even the switch key — it's simply not emitted.
**Fix:** surface the code (and any field) in `extensions` so clients branch on a stable token, not prose:
```csharp
return Results.Problem(
    detail: error.Description,
    statusCode: status,
    extensions: new Dictionary<string, object?> { ["code"] = error.Code });
```
One-line change in the shared mapper fixes it for every aggregate at once.

---

### [HIGH] List endpoints return bare, unbounded arrays — Check 4: Pagination as an afterthought
**Where:** `GetProjectsEndpoint.cs:87`, `GetProvidersEndpoint.cs:53`, `GetRequestsEndpoint.cs:52`, `GetUsersEndpoint.cs:55` (and the other `GetXxxs` list endpoints — systemic)
**What the consumer must guess:** how many results exist, whether there are more, and what order they arrive in. The UI can't render paging controls; a large tenant can pull the whole table in one call; and because there's no sort/tie-breaker, order is whatever the DB returns.
**Evidence:**
```csharp
// GetProjectsEndpoint.cs — no total, no page/limit, no cap, no ORDER BY tie-breaker
var summaries = await projects.GetProjectsAsync(filter, ct);
var response = summaries.Select(p => new Response(...)).ToList();
return Results.Ok(response);   // bare List<Response>
```
A repo-wide search for `Skip(`/`Take(`/`page`/`OrderBy` across `src/Modules/Core` returns **zero** hits in the read path.
**Fix:** one shared pagination contract applied to every list, with a max page size and a stable tie-breaker:
```csharp
public record PagedResult<T>(IReadOnlyList<T> Data, PageMeta Meta);
public record PageMeta(int Page, int Limit, int TotalItems, int TotalPages, bool HasNextPage, bool HasPreviousPage);

var query = baseQuery.OrderByDescending(p => p.StartDate).ThenBy(p => p.ShortName); // tie-breaker
var total = await query.CountAsync(ct);
var items = await query.Skip((page - 1) * limit).Take(Math.Min(limit, MaxPageSize)).ToListAsync(ct);
return TypedResults.Ok(new PagedResult<Response>(items.Select(Map).ToList(), new PageMeta(...)));
```
Defaults can keep current behavior (e.g. `limit=50`) so the change is additive for existing callers.

---

### [MEDIUM] Error response *shape* is inconsistent across endpoints — Check 1: Inconsistent response shapes
**Where:** `CreateProjectEndpoint.cs:95`, `CreateProviderEndpoint.cs:73` (bare string); `Results.NotFound()` in ~30 handlers (empty); `DomainErrorResults.ToProblem` / `Results.ValidationProblem` (ProblemDetails); `CreateRequestEndpoint.cs:42,62,71` (inline `Results.Problem`)
**What the consumer must guess:** what an error body looks like — it depends on the endpoint. A 409 from `CreateProject` is a JSON **string**; a 409 from a domain error is a **ProblemDetails**; a 404 is an **empty body**. One error-handling helper can't parse all three.
**Evidence:**
```csharp
// CreateProjectEndpoint.cs:95 — bare string body
return Results.Conflict($"A project with short name '{shortName.Value}' already exists.");
// vs DomainErrorResults.cs:50 — ProblemDetails body
return Results.Problem(detail: error.Description, statusCode: status);
// vs AcceptRequestEndpoint.cs:46 — empty body
return Results.NotFound();
```
**Fix:** route *all* failures through `ProblemDetails`. Replace `Results.Conflict("…")` with `Results.Problem(detail, statusCode: 409, extensions: {["code"]=…})`, and give `NotFound()` a problem body with a code (`PROJECT_NOT_FOUND` etc.). Consider a small `Problems` helper so endpoints can't drift again.

---

### [MEDIUM] Enums crossing the wire as undocumented magic integers — Check 1/8
**Where:** `GetRequestsEndpoint.cs:18-25` (`Type`/`State` as `int`), `CreateRequestEndpoint.cs:24` (`RequestType` as `int`), `GetRequestsEndpoint.cs:16` (`int? State` filter)
**What the consumer must guess:** what `"state": 3` *means*. The request `Type`/`State` are serialized as raw integers, and only `CreateRequest` documents that `RequestType=1` = SelfOnboarding; `GetRequests` documents none of the `State` values.
**Evidence:**
```csharp
internal sealed record Response(
    Email? ContactEmail,   // value object leaking as response field
    int Type,              // 1? 2? meaning undocumented
    int State,             // pending/accepted/rejected/deleted? unknown to the caller
    ...);
```
Note also `ContactEmail` is the domain `Email` value object used directly as a response field — its JSON shape is an implementation detail, not a contract.
**Fix:** serialize enums as named strings (the app already registers `JsonStringEnumConverter` in `Program.cs:222` — these `int` fields opt out of it by being `int`), or document each integer's meaning in `WithDescription`. Project a `string Email` into the response rather than the value object.

---

### [MEDIUM] No correlation id in error responses — Check 10: Hidden operational context
**Where:** `src/Core/API/Program.cs:309`
**What the consumer must guess:** how to tie a failed call to a server log line. A client that hits a 500/409/400 gets a ProblemDetails with no `requestId`/`traceId` to quote to support.
**Evidence:**
```csharp
builder.Services.AddProblemDetails();   // no CustomizeProblemDetails → no requestId/traceId echoed
```
(There is an `OtelTraceResponseMiddleware` and Serilog, so a trace id likely exists server-side — it's just not surfaced in the problem body.)
**Fix:**
```csharp
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["requestId"] =
        Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier);
```

---

### [MEDIUM] Unhandled-exception handler leaks `exception.Message` to clients — Check 2: leaking internals
**Where:** `src/Core/API/GlobalExceptionHandler.cs:97` (app-wide, so it covers v2 too)
**What the consumer must guess:** nothing — the problem is it tells them *too much*. On the `default` (unexpected) branch the status is 500 but the raw `exception.Message` is written into `extensions["errors"]`, potentially exposing internal detail, and no `requestId` is attached.
**Evidence:**
```csharp
default:
    httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
    break;
...
problem.Extensions.TryAdd("errors", exception.Message);   // raw message to the client on every path
await httpContext.Response.WriteAsJsonAsync(problem, ...);
```
**Fix:** for the unexpected/500 path return a generic `INTERNAL_SERVER_ERROR` + a `requestId`, and log the message server-side only. Keep field-level detail for the known `ValidationException` case where it's safe.

---

### [MEDIUM] `403 Forbidden` returned with an empty body — Check 6: Confusing auth failures
**Where:** `CreateProviderEndpoint.cs:43`, `UpdateProviderEndpoint.cs:40`, `DeactivateProviderEndpoint.cs:29`, `ReactivateProviderEndpoint.cs:30`, `AddLocationEndpoint.cs:42`, `RemoveLocationEndpoint.cs:33`, `UpdateLocationEndpoint.cs:43`, the three `ResourceAssignment` endpoints
**What the consumer must guess:** *why* they were forbidden — which permission/role is missing. `Results.Forbid()` emits an empty 403.
**What's already good:** 401 vs 403 is cleanly separated, and the deliberate **404-instead-of-403** (hiding out-of-scope resources, e.g. `AcceptRequestEndpoint.cs:55`) is **documented** in the endpoint description — exactly the right pattern.
**Fix:** return a problem body with a stable code on the 403 path:
```csharp
return Results.Problem(statusCode: 403, title: "Provider write not permitted for this identity.",
    extensions: new Dictionary<string, object?> { ["code"] = "PROVIDER_WRITE_FORBIDDEN" });
```
(Lower urgency because the Gateway is the real RBAC gate; these Core checks are defense-in-depth.)

---

### [LOW] Date-range filters loosely typed and undocumented — Check 5: Inconsistent filters & sorting
**Where:** `GetProjectsEndpoint.cs:11-15, 49-63`
**What the consumer must guess:** the timezone and inclusivity of `StartDate`/`EndDate`, and why these are `string?` (parsed with `DateTimeOffset.TryParse`) when sibling endpoints take typed `Guid?`/`int?` params directly.
**Evidence:**
```csharp
[FromQuery] string? StartDate, [FromQuery] string? EndDate
...
if (!DateTimeOffset.TryParse(request.StartDate, out var sd)) return Results.ValidationProblem(...);
```
**Fix:** bind as `DateTimeOffset?` directly (Minimal API parses ISO 8601 natively, removing the manual parse) and document the bounds, e.g. *"`startDate` inclusive, `endDate` exclusive; ISO 8601 UTC."* No sort param exists on any list — add one consistently if/when paging lands.

---

### [LOW] One-off inline 401 instead of the shared `Caller` binding — Check 1
**Where:** `GetUsersEndpoint.cs:27-29`, `GetUserPreferencesEndpoint.cs:17`
**What the consumer must guess:** nothing serious — it's an internal inconsistency. Most endpoints take `Caller` as a bound parameter (which yields a framework `400` when `sub` is absent — see `Caller.BindAsync`); these two instead resolve the id manually and return `Results.Problem("Invalid token", 401)`.
**Fix:** take `Caller` as a parameter like the rest, or centralize the "no sub" response so authn-failure shape is uniform.

## Passed checks

- **Check 3 — Status codes:** Correct and varied. `201 Created` with `Location` (`CreateProvider` uses `CreatedAtRoute`), `204 NoContent` for settles, `409` for duplicates/conflicts, `422` for unsupported request types, `502 BadGateway` when the role service is down. No `200`-on-failure observed.
- **Check 7 — Breaking changes / versioning:** Already versioned — refactored routes live under `api/v2`, legacy specs are preserved verbatim, and the two are kept apart by the `v2/` prefix (`Program.cs:231-292`). Appropriate for an internal API.
- **Check 9 — Idempotency:** Acceptably handled without explicit keys: creates are guarded by natural-key uniqueness → `409` (`CreateProject`, `CreateProvider`), self-onboarding by a pending-request dedupe check (`CreateRequest.cs:70`), and settles by domain state-transition guards (`InvalidStateTransition` → `409`). Retries don't double-create or double-email.
- **Check 8 — Docs (partial):** Strong. Every endpoint has `WithName/WithSummary/WithDescription/WithTags/Produces`, and descriptions enumerate the failure codes and conditions (even documenting the intentional 404-for-out-of-scope). Gaps: error responses are declared with `.Produces(409)` rather than `.ProducesProblem(409)`, and enum-integer meanings are under-documented (see Medium finding).

## Pre-ship checklist

- ✅ **1. Consistent success shape** — lists/objects returned directly, no per-endpoint success envelopes.
- ⚠️ **2. Error messages machine-readable** — ProblemDetails used in most paths but the **stable code is dropped** (`DomainErrorResults.cs:50`) and 500s leak `exception.Message`.
- ✅ **3. Honest status codes** — correct, varied, no 200-on-failure.
- ❌ **4. Pagination** — every list returns a bare unbounded array; no metadata, cap, or sort.
- ⚠️ **5. Filters & sorting** — flat filter params are consistent; **no sorting** anywhere; date ranges loosely typed/undocumented.
- ⚠️ **6. Auth failures** — 401/403 cleanly split and intentional-404 documented, but `Forbid()` bodies are empty with no code.
- ✅ **7. Breaking changes** — versioned (`api/v2`), legacy preserved.
- ⚠️ **8. Docs beyond happy path** — rich descriptions, but `.Produces` not `.ProducesProblem`, and enum meanings thin.
- ✅ **9. Idempotency** — natural keys + state guards cover the high-risk writes.
- ❌ **10. Operational context** — no `requestId` in any problem body (`Program.cs:309`).
