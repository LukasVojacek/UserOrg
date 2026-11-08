# ADR 0020 - v2 HTTP contract standards: uniform ProblemDetails, paged-list envelope, and string-named enums

- Date: 2026-06-15
- Status: accepted

## Decision

All refactored **v2** endpoints (`src/Modules/Core/Features/**`, mapped under `MapGroup("api/v2")`) follow one
HTTP contract, so a consumer learns the shape **once** rather than per endpoint. Because v2 is not yet
released, these are introduced as the canonical shape with no deprecation window.

### 1. Errors are always RFC 9457 ProblemDetails carrying a stable `code`

- **Every** failure response is a `ProblemDetails`. No bare strings (`Results.Conflict("…")`), no empty
  bodies (`Results.NotFound()` / `Results.Forbid()` with no payload), no ad-hoc shapes.
- Each ProblemDetails carries a **stable, machine-readable `code`** in `extensions["code"]`. Clients branch
  on `code`, never on the human-readable `detail`/`title`.
- **Codes use the existing `aggregate.snake_case` convention** already established in the domain
  (`project.user_already_assigned`, `provider.name_already_exists`, `request.invalid_state_transition`).
  Domain failures reuse `error.Code` verbatim; the shared `DomainErrorResults.ToProblem()` surfaces it into
  `extensions["code"]` (previously it derived only the status and discarded the code).
- **The endpoint owns its concrete problem.** A shared `Problems` helper in `Core/Shared` produces the
  ProblemDetails *shape*; it does not impose a central mandatory enum. Cross-cutting convenience codes
  (e.g. `validation.failed`, `common.not_found`, `auth.forbidden`, `auth.unauthenticated`,
  `upstream.role_service_unavailable`) exist as helpers, but each endpoint chooses the code/title/detail that
  fits its case.

  > **Note 2026-10-01.** `common.not_found`, `auth.forbidden`, `auth.unauthenticated` and `internal_error` were
  > removed from `CommonErrorCodes`: no endpoint used them. The same strings in `GlobalExceptionHandler` and the
  > Gateway are separate literals and stay.
- **Unexpected (500) failures** return a generic code and a `requestId`, never the raw exception. See ADR's
  amendment to the global handler below.

### 2. Operational context: `requestId` echoed into every error body

`AddProblemDetails` is configured with `CustomizeProblemDetails` to copy the active correlation id
(`Activity.Current?.Id`, the same id `TracingHelper.EnsureResponseTraceHeaders` already puts on the response
**header**) into `extensions["requestId"]`. Body and header therefore agree, giving a client→log path on
failure. The shared `GlobalExceptionHandler` stops writing raw `exception.Message` to the client on the
unexpected/500 path (it logs the detail server-side and returns a generic problem + `requestId`); the safe
field-level detail for `ValidationException` is retained. This fix is applied to the shared handler, so it
protects legacy endpoints too.

### 3. Lists use one uniform, cursor-ready paged envelope

- Every **list** endpoint returns one shape: `PagedResult<T> { data, meta }`. `meta` is **always present**.
  Single-item endpoints (`GetProject`, `GetUserInfo`, …) are unchanged — the envelope is for collections only.
- **Scheme is offset-based now, shaped to allow cursor later.** `meta` always carries `limit` and
  `hasNextPage`; offset lists additionally carry `page`, `totalItems`, `totalPages`, `hasPreviousPage`. A
  future cursor-based list can populate `nextCursor` without changing the envelope or breaking offset lists.
- **Growable vs reference lists differ only in whether `?page=`/`?limit=` are honored — not in shape.**
  - *Growable* collections (`GetProjects`, `GetProviders`, `GetRequests`, `GetSentRequests`, `GetUsers`,
    `GetResources`, `GetProjectTags`, `GetLocations`, `GetProjectUsers`) honor `page`/`limit` with a server
    **max page-size cap** and a default limit.
  - *Bounded reference* lists (`GetCountries`, the `Get*Enums`, `GetValidRoles`, `GetProjectTemplates`)
    ignore paging params and report a single complete page (`page: 1, totalPages: 1, hasNextPage: false`).
    This is honest, not fake: the list *is* complete in one page. A client reads `meta.hasNextPage`
    identically for a 6-row enum and a 10 000-row project list.
- **Every paginated query must have a unique sort tie-breaker.** Offset paging over a non-unique `ORDER BY`
  shuffles/duplicates rows across pages. `GetProjects` is already stable (orders by the unique `ShortName`);
  `GetRequests` ordered by `ProjectId` alone is **not** and gains `.ThenBy(r => r.Id)`. Pagination is pushed
  to SQL via a paged variant of the `DataQuery` wrapper (`Count` + `Skip/Take`), since the read queries are
  already `IQueryable`-based. Client-controlled `?sort=` is deferred (YAGNI) — order is fixed per endpoint.

### 4. Enums cross the wire as stable string names, in and out

- `RequestState` and `RequestType` (value objects over a legacy-DB-aligned `int`) are exposed and accepted as
  **stable PascalCase names** (`Pending`, `PendingForPIAccept`, `SelfOnboarding`, …), not raw integers.
  Responses emit the name; the `?state=` filter and `CreateRequest` accept the name; a name↔int map keeps the
  persisted/legacy integer purely internal. An unknown name is a `validation.failed` ProblemDetails.
- `EOperationMode` is already a real C# enum serialized as its name (via the global
  `JsonStringEnumConverter`); no change.
- No lookup id on the wire. Every lookup type has a list endpoint serving the value used in its place (the
  name, or a code such as a country's `Code`), and inputs and outputs carry that value, never the lookup's id.

## Context

A Minimal API contract audit of the v2 surface (see `docs/minimal-api-audit.md`) found the surface
otherwise healthy (honest status codes, versioning, idempotency via natural keys, per-endpoint docs) but
with one dominant root cause: **errors and lists were built endpoint-by-endpoint without a shared response
contract.** Concretely: domain error *codes* existed but were dropped before reaching the client; error
bodies came in four shapes (ProblemDetails / bare string / empty / inline `Problem`); every list returned a
bare unbounded array with no metadata, cap, or stable order; `RequestState`/`RequestType` leaked as magic
integers; and no `requestId` reached error bodies. v2 has no released consumers, so the target contract can
be adopted directly.

## Considered options

- **Uniform envelope, `meta` always present (chosen).** One client helper for every list and every error.
  Cost: a shared `Problems`/`PagedResult` layer plus a mechanical sweep of ~51 endpoints.
- **Domain-error codes only; leave validation/not-found/forbidden code-less.** Rejected: leaves clients
  branching on status alone for the majority of failures — the audit's core complaint.
- **Omit `meta` on reference lists to signal "not paginated".** Rejected: reintroduces a presence-check
  branch, recreating two shapes — the exact thing uniformity removes.
- **Cursor pagination now.** Rejected: overkill for hundreds-to-thousands of rows and loses the page-number
  UX a management console wants. The envelope is kept cursor-*ready* instead.
- **Keep enums as documented integers.** Rejected: clients still branch on magic numbers; only partially
  addresses the finding. String names make the int a pure internal/legacy detail.

## Consequences

- A shared presentation layer appears in `Core/Shared`: a `Problems` factory, optional `CommonErrorCodes`,
  `PagedResult<T>`/`PageMeta`, and a paged `DataQuery` wrapper. `DomainErrorResults.ToProblem()` now emits the
  code.
- **Rollout is incremental** (matching the F-series migration cadence): a foundation change ships the shared
  infra with no endpoint behavior change; a pilot slice (Project) applies the full standard end-to-end with
  integration tests and becomes the reference pattern; the remaining aggregates fan out one PR at a time.
- New contract tests assert the cross-cutting invariants: ProblemDetails shape + `code` on each failure path,
  `meta` presence/values, enum name round-trips, and the 404-hiding equivalence (see ADR-0018 amendment).
- `RequestState`/`RequestType` gain a stable name + parse. Adding presentation-facing names to domain VOs is a
  mild concession, accepted because the names are stable domain vocabulary, not transient formatting.
- The legacy `int`-valued request state/type remain in the database and in legacy endpoints unchanged; only
  the v2 wire contract speaks names.

## Amendment (2026-06-15) — validation error shape & honest implementation status

**Validation error shape (refines §1 & §2).** A validation failure carries exactly **one** human-readable
message and a machine **code** (the error *type*) — no per-field message duplication:

- *Single field* (the large majority of sites): `detail` = the human message, `extensions["code"]` = the
  specific code (e.g. `request.invalid_type`).
- *Multiple fields*: `detail` = one summary message, `extensions["code"]` = `validation.failed`, and
  `extensions["fieldErrors"]` = `[{ field, code }]` — **codes only**, no per-field messages.

Built by a `Problems.Validation(...)` helper. Codes are centralised: cross-cutting ones
(`validation.failed`, `validation.required`, `validation.invalid_format`, …) in a new `CommonErrorCodes`
class in `Core/Shared`; aggregate-specific ones in the existing `XxxErrorCodes`. This supersedes the earlier
implicit use of `Results.ValidationProblem`, which put the code (or worse, free-text) inside the `errors`
dictionary value where no client could branch on it.

**Implementation status (update 2026-06-15).** The parts of §2 that a contract review found unbuilt are now
**implemented**: `requestId` is echoed into every ProblemDetails body — `Program.cs` registers
`AddProblemDetails(o => o.CustomizeProblemDetails = …)` copying `Activity.Current?.Id`, and
`GlobalExceptionHandler` sets the same id manually (it builds its `ProblemDetails` outside the
`IProblemDetailsService` path); the `GlobalExceptionHandler` 500 path no longer returns `exception.Message`
(generic `internal_error` code + `requestId`, message logged server-side only); and the `Problems.Validation`
helper above exists, with every `Results.ValidationProblem` call site migrated to it. The earlier
"decided but not yet implemented" note (and the `v2-contract-standards-fanout-plan.md` "PR 1 configured
requestId" line, which was aspirational at the time) no longer apply. Verified by unit + EF-in-memory tests;
the real-SQL behaviour of the paged/natural-key reads still warrants the DB integration suite before release.

## Amendment (2026-06-15) — uniform create response

§1 fixed the *success-vs-error* and *list* shapes but left **create** responses inconsistent (a review found
three variants: `201` + body, `201` + null body, and `204`). The rule is now uniform across every create:

**A successful create returns `201 Created`, a `Location` header pointing at the new resource's canonical
URL, and a small body echoing the resource's identifying field(s)** (e.g. `{ shortName }`, `{ name }`,
`{ requestId }`, the assignment `{ id }`). The body spares the client a follow-up GET to learn the identity;
`Location` is the canonical address (a natural-key URL where one exists — ADR-0022 — otherwise the
surrogate-id URL, e.g. a Request or an assignment).

This standardised `CreateProject`/`CreateRequest` (which returned a null body), `CreateProjectTag` (which
returned `204`), and fixed a latent bug: `AssignResource` used `CreatedAtRoute("GetResourceByIdV2", …)`, a
route the ADR-0022 rename removed — since `CreatedAtRoute` resolves the name only at response-execution time,
every successful assignment threw `InvalidOperationException` (a **500**) that handler-level unit tests could
not see. It now uses `Results.Created(<assignment URL>, …)` and an explicit `Location` assertion guards the
regression. Idempotent creates (e.g. re-assigning an existing tag) still return `201` for one uniform shape.
Other state-changing commands remain `204 No Content`.

## Amendment (2026-06-16) — pagination moves from the body envelope to HTTP headers

§3 shipped pagination as a body envelope `PagedResult<T> { data, meta }`. We are **reverting the envelope**:
a list response body is now the **bare array** of items, and the pagination metadata is carried in **HTTP
response headers** following the established `Link`/`X-Total-Count` convention (RFC 8288 for `Link`).

- **Growable lists** emit `X-Total-Count: <n>` plus a `Link` header with `rel="first" | "prev" | "next" |
  "last"` URLs (only the rels that exist for the current page). The link URLs **preserve the other query
  parameters** (filters, `?state=`, etc.) and differ only in `page`/`limit`.
- **Bounded reference lists** (`GetCountries`, the `Get*Enums`, `GetValidRoles`, `GetProjectTemplates`) emit
  **only `X-Total-Count`** and no `Link` (a single complete page has no next/prev). A client reads
  `X-Total-Count` identically for a 6-row enum and a 10 000-row collection — the "learn the shape once"
  property of §3 is preserved, it just moves to a header.
- Single-item endpoints are unaffected (their body was already the bare object).
- **Cursor-readiness is retained and improved**: a future cursor list populates `Link rel="next"` with a
  cursor URL — no body-shape change, exactly as `Link` was designed for.

**Why reverse §3.** The team chose to follow the widely-implemented header-pagination convention (so generic
client libraries and HTTP tooling read paging without per-API knowledge), and confirmed that the original
motivation for a body envelope — distinguishing "deliberately empty" from "an error returning the same
shape" — is **already carried by the status code** (`200` + `[]` vs a non-2xx `ProblemDetails`; an uncaught
error is forced to `500` by `GlobalExceptionHandler`, never `200 []`). The envelope added a level of nesting
without adding information, so it is dropped. Partial/degraded reads (e.g. `GetProjectTemplates`) are modelled
as a **per-item domain field** (`IsAvailable`), not a response-level envelope concern, so the envelope was not
earning its keep there either.

**Non-negotiable consequence — CORS.** A browser client cannot read `Link`, `X-Total-Count` (or `ETag`, see
the next amendment) unless the server sends `Access-Control-Expose-Headers`. The default CORS policy uses
`AllowAnyHeader()`, which governs **request** headers only and does **not** expose response headers; it must
add `WithExposedHeaders("Link", "X-Total-Count", "ETag")`. The YARP Gateway in front of Core must forward
these headers unchanged.

**OpenAPI.** List endpoints change their success body from `PagedResult<Response>` to `IReadOnlyList<Response>`
and document the `Link`/`X-Total-Count` response headers. The `PagedResult<T>`/`PageMeta` body types are
removed; the offset computation (`page`/`limit`/`totalItems`) is retained and feeds the header builder.

## Amendment (2026-06-16) — read-side ETag revalidation is the v2 standard for cached reads

Client-side conditional GET (a strong `ETag` over the response body + `If-None-Match` → `304 Not Modified`) is
the standard for **read** endpoints. It is implemented by `ConditionalGetMiddleware` (registered immediately
before `UseOutputCache`, so it revalidates output-cached responses too) and opted into per endpoint with
`.WithETag()`; responses are tagged `Cache-Control: private, no-cache` because the data is per-caller and
behind a bearer token. The body hash is taken over whatever the endpoint returns — now the bare list array,
unaffected by the envelope removal above. The standard is to apply `.WithETag()` to every cached GET read;
`ETag` must be added to the CORS `WithExposedHeaders` list so browser clients can read it.

**Implemented (2026-06-16).** `.WithETag()` now ships on all six cached GETs — `GetUserInfo`,
`GetExtendedUserInfo`, `GetProject`, `GetProjectTemplates`, `GetCountries`, `GetStatistics`. `ETag` is in the
CORS `WithExposedHeaders` list (added in Workstream A). The `ConditionalGetMiddleware` 304 mechanism is covered
by `ConditionalGetMiddlewareTests` (8 tests: strong-ETag emission, `If-None-Match` match → `304`, no-match →
`200`, weak-validator/`*` matching, and pass-through for non-GET / no-metadata / non-200).

This body-hash ETag answers `If-None-Match` (bandwidth/`304`) but is **not** a write precondition — see
ADR-0023 for write-side optimistic concurrency, which uses a version token, not a body hash.

## Amendment (2026-07-20) — `BadHttpRequestException` mapping, the rejected `ArgumentException` case, and a known error-shape divergence

Issue [#260](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/work_items/260) found that malformed
route parameters on 18 v2 endpoints returned **500** instead of **400**: `GlobalExceptionHandler`'s switch had
no case for the framework's own route-binding failure, so it fell to `default`. Fixing it surfaced two more
decisions worth recording against §1.

**`BadHttpRequestException` gets its own branch, carrying the `code` §1 requires.** The framework throws
`BadHttpRequestException` when it cannot bind a route/query parameter — a caller error, not a server fault.
The branch honours the framework's own `.StatusCode` (400 today; a future 413/415 maps correctly too, rather
than a hard-coded 400), and sets `Title` = `"Validation failed"`, a **fixed, generic** `Detail`
(`"One or more request parameters have an invalid format."` — `GlobalExceptionHandler.cs:56`; "request",
not "route", because this exception type also covers `413`/`415` and malformed JSON bodies), and
`extensions["code"] =
"validation.invalid_format"`. The raw binding message (`"Failed to bind parameter …"`) is **never** echoed to
the client — it is logged server-side only, preserving §1's "never the raw exception" rule for this path too.

**The `code` is written as a string literal, not as `CommonErrorCodes.InvalidFormat`.** That constant is
`internal` to the **Core module** assembly while `GlobalExceptionHandler` lives in the **API** assembly, and
Core's only `InternalsVisibleTo` grants are `Core.UnitTests` and `DynamicProxyGenAssembly2` (the Castle/Moq
proxy assembly) — neither reaches the API assembly, so referencing the constant from the handler is a
**CS0122**, not a style preference. Two ways of removing the duplication were considered and rejected: adding
`InternalsVisibleTo("API")` permanently widens Core's *entire* internal surface to the whole API assembly in
order to share one string; making `CommonErrorCodes` public violates Core's frozen public surface
(`docs/architecture/adr/0079-core-visibility-internal-by-default-with-documented-exceptions.md`, issue #249)
and fails its enforcing architecture test
`PublicSurfaceTests` (`Tests/Modules/Core/Core.ArchitectureTests/PublicSurfaceTests.cs`). Inlining also
matches the precedent already in this file, which hard-codes `"internal_error"` the same way. The accepted
cost is one duplicated literal, and that cost is acceptable because both copies are now pinned by tests: the
`CommonErrorCodes.InvalidFormat` constant by
`Tests/Modules/Core/Core.UnitTests/Shared/CommonErrorCodesTests.cs`, method
`Should_MatchGlobalExceptionHandlerLiteral_When_InvalidFormatIsDuplicated` (cited by name, not line: it is
the file's only test, so a line number would add fragility without precision), which asserts the constant
against the same literal the handler hard-codes, and the handler-side literal by
`Tests/Core/Application.Tests/GlobalExceptionHandlerTests.cs:74`, which asserts
`extensions["code"] == "validation.invalid_format"` (with `:73` pinning the `Detail` string). Drift between
the two copies is therefore detected. Note this is a **build-time** guard: `InvalidFormat` is a `const`, so its
value is inlined into `Core.UnitTests` at that assembly's compile time — a `dotnet test --no-build` run over a
test assembly built before the constant was edited compares two copies of the old value and still passes.

**`ArgumentException` is deliberately *not* given a global case.** The issue as filed proposed adding both
`BadHttpRequestException` and `ArgumentException`. The latter is rejected. `ArgumentNullException` derives
from `ArgumentException`, and `src/` is saturated with `ArgumentException`-family guards. Measured on
`feat/refactor-core`, 2026-07-20, by `grep -r --include=*.cs` over `src/`: **483**
`ArgumentNullException.ThrowIfNull(`, **43** `ThrowIfNullOrWhiteSpace(`, **23** `ThrowIfNullOrEmpty(` —
roughly **550** in total. These are *programming-error* guards, not client input validation.

Treat that total as an **order of magnitude, not a fixed integer**: it moves with every commit, and a later
grep returning 500 or 620 confirms this reasoning rather than refuting it. The argument holds for any count in
the hundreds. A bare `case ArgumentException` in the shared handler would reclassify all ~550 of those guards
as a client `400`, permanently erasing them from 5xx alerting, to serve **5** endpoints that reached a
throwing value-object factory on caller-controlled input. Two take it from a **route** parameter
(`GetProviderEndpoint`, `PromoteToProviderAdminEndpoint`); three take it from a **query** parameter, which an
earlier sweep of this ADR missed by searching route bindings only — `GetProvidersEndpoint` (`?name=`) and
`GetLocationsEndpoint` (`?providerName=`, which threw inside `LocationDetailsQuery`) via `ProviderName`, and
`GetUsersEndpoint` (`?idpUserId=`) via `IdpId`. Each was guarded by `IsNullOrWhiteSpace` alone while the
factory also enforced a length bound, so an over-long (or, for `IdpId`, an under-length) filter returned
**500**.

All 5 are instead fixed at the call site, returning `400` through the `Problems.cs` helper like every other v2
validation failure. `ProviderName` already offered a non-throwing `TryCreate` alongside the throwing `From()`
and `IdpId` a `TryParse`, so the fix was simply to switch the endpoints over to them. `LocationFilter` in
addition now carries a `ProviderName?` rather than a `string?` — mirroring the sibling `ProviderFilter` — so
the query object cannot receive an unvalidated name at all. No domain code was written for *these 5 fixes* —
`ProviderName.TryCreate` and `IdpId.TryParse` both already existed, so the change is confined to call sites.
That is not true of the issue as a whole: the onboarding-slug exception below did add one `TryCreate` to
`src/Modules/Core.Domain/`, so `git diff feat/refactor-core...HEAD -- src/Modules/Core.Domain/` is **not**
empty. **If this decision is reopened, re-run the grep above first** — the sheer scale of the guard
population, not convenience, is why the global case does not exist.

**Known, accepted divergence between the two v2 error paths.** The 16 endpoints on the
`GlobalExceptionHandler` path and the 4 provider-name endpoints on the `Problems.cs` path agree on `status`
(`400`) and `title` (`"Validation failed"`) for this failure — and on nothing else. `extensions.code` is
present on both but deliberately **different**, and `detail` differs by construction:

| field | handler path (16 endpoints) | `Problems.cs` path (4 endpoints) |
|---|---|---|
| `status` | `400` | `400` — **equal** |
| `title` | `"Validation failed"` | `"Validation failed"` — **equal** |
| `extensions.code` | `"validation.invalid_format"` (`GlobalExceptionHandler.cs:58`) | `"provider.invalid_name"` (`ProviderErrorCodes.cs:33`) — present on both, **never equal** |
| `detail` | fixed generic string (`GlobalExceptionHandler.cs:56`) | the domain-specific message from `Problems.Validation` — **never equal** |
| Content-Type | `application/json` (`GlobalExceptionHandler.cs:139`) | `application/problem+json` |
| `type` member | unset | RFC-9110 default |
| `requestId` | set manually (`GlobalExceptionHandler.cs:140`) | set manually (`Problems.cs`, closed 2026-07-20 — see amendment below) |

The `code` and `detail` rows are *permanent* divergences, not gaps awaiting a fix. The handler cannot know
which parameter failed, so it emits one generic code and message for every binding failure; the `Problems.cs`
path knows it was a provider name and says so. `RouteParamValidationTests.cs` encodes exactly this: it asserts
equality on `status` and `title` (`:126-127`), asserts both codes are non-empty, and asserts
`Assert.NotEqual` on the two codes (`:133`). It deliberately does **not** assert `detail` equality (`:101-102`).

The table covers the four provider-name endpoints only. The fifth fixed endpoint, `GetUsersEndpoint`, is the
one case where the two paths *do* agree on `code`: its `?idpUserId=` filter emits `CommonErrorCodes.InvalidFormat`
(`"validation.invalid_format"`) through `Problems.cs`, matching the code its `?projectShortName=` filter already
used and matching the handler's generic code. That is deliberate — an identity-provider id has no
aggregate-specific error code — and it is why the `NotEqual` assertion above is pinned to the provider-name
endpoints rather than asserted globally.

`requestId` was absent on the `Problems.cs` path because `Results.Problem` returns a `ProblemHttpResult`, which
bypasses `IProblemDetailsService` entirely — so the `CustomizeProblemDetails` hook registered at
`HostInfrastructureExtensions.cs:108-111` (the mechanism §2 relies on) never fires for it. Closing this gap
would mean touching `Problems.cs` or the `ProblemDetails` registration itself, which lands on **every** v2
endpoint using that helper, not just these 21 — far beyond issue #260's blast radius. It was therefore filed as
a separate issue (#263) rather than folded into this fix. **Closed 2026-07-20** — see the amendment below.

**Deliberate exception — the onboarding Accept/Reject endpoints answer a malformed slug with `404`, never
`400`.** A third `ArgumentException`-to-`500` site was found after the 18 above: `OnboardingRequestQuery`
called the throwing `OnboardingRequestSlug.From()` on the raw route slug (in `FindIdBySlugAsync` and
`FindSettlementBySlugAsync`), so a slug over `OnboardingRequestSlug.MaxLength` (**120**) reached the handler's
`default` arm as a `500`. It was **not** fixed the way the other endpoints were. `PUT
projects/{projectShortName}/onboarding-requests/{slug}/accept` and `…/reject` are engineered so that a genuine
miss, an out-of-scope caller, and a slug belonging to a different project all return one byte-identical
`onboarding_request.not_found` problem (the ADR-0018 rider plus the ADR-0048 wrong-project rider). Returning
`400` for a structurally invalid slug would hand an enumerator a distinguisher the other three arms are
specifically designed to deny: it separates "this slug is malformed" from "this slug does not exist / is not
yours", which is precisely the leak those endpoints exist to prevent.

The fix therefore adds `OnboardingRequestSlug.TryCreate` (mirroring the `TagName`/`ProviderName` convention)
and has the **query** treat an unparseable slug as a **miss** — it returns `null`, exactly as it does for a
slug that is absent. No new status, no new `code`, no new branch in either endpoint: the invalid slug reaches
the endpoints' existing `settlement is null` arm, so the response is identical to a genuine miss not merely by
value but by *code path*. This is safe because an over-length slug cannot match any stored row anyway — slugs
are generated by `OnboardingRequestSlug.New` and validated on write — so answering "not found" is also simply
true. Ordering is unchanged; nothing was hoisted above the scope check.

**Do not "harmonize" these two endpoints to the `400` pattern.** Doing so silently reintroduces the
enumeration leak. The guard is `OnboardingRequestQueryTests.Should_AnswerAsAMiss_When_SlugIsStructurallyInvalid`,
which asserts the invalid-slug result *equals* the absent-slug result rather than pinning a hard-coded shape,
alongside `AcceptOnboardingRequestEndpointTests.Should_ReturnIdenticalNotFound_When_MissingOutOfScopeOrWrongProject`.
Consequently these endpoints carry **no** `.ProducesProblem(400)` — their documented contract is `204` / `404` /
`409`, and that omission is correct, not an oversight.

## Amendment (2026-07-20) — `requestId` closed on the `Problems.cs` path (#263)

The gap noted above — `requestId` absent on any failure body built via `Problems.cs`'s `Results.Problem` calls,
because that `ProblemHttpResult` path bypasses the `IProblemDetailsService`/`CustomizeProblemDetails` hook §2
relies on — is now closed centrally inside `Problems.cs` itself, not by threading `HttpContext` through the
~270 call sites across ~62 endpoint files that use it.

`Problems.cs` adds a private `RequestId` accessor (`Activity.Current?.Id`) and stamps
`extensions["requestId"]` from it in every one of the file's 10 public helpers: the 7 that funnel through the
private `Of(...)` builder (`NotFound`, `ProjectNotFound`, `CallerNotProvisioned`, `Conflict`, `Forbidden`,
`Unprocessable`, `Upstream(code, detail)`), plus the 3 that bypass it
(`Validation(code, message)`, `Validation(detail, fieldErrors)`, `Upstream(code, detail, unreachable)`).

This uses only the ambient `Activity.Current` (`AsyncLocal`, populated by ASP.NET Core's hosting layer for
the duration of every inbound request), the same primary source `GlobalExceptionHandler.cs:140` and
`HostInfrastructureExtensions.cs:110-111` already rely on. Unlike those two sites, `Problems.cs` does not fall
back to `httpContext.TraceIdentifier` — none of its helpers receive `HttpContext`, and threading it in for a
fallback that is only reached if `Activity.Current` were ever null (which it is not, for any in-flight HTTP
request) is not worth the ~270-call-site churn this amendment exists to avoid.

Verified by `Tests/Modules/Core/Core.UnitTests/Shared/ProblemsValidationTests.cs`: with an `Activity` started,
`Problems.Validation` (both overloads), `Problems.Upstream` (both overloads), and `Problems.NotFound` (an
`Of`-routed helper) each assert `problem.ProblemDetails.Extensions["requestId"]` equals the started activity's
`Id`. The "verified by tests" gap this ADR previously left open for the `Problems.cs` path is closed.

## Amendment (2026-09-23): `AddLocationV2` requires `LocationType` (T-195-01)

`POST v2/Providers/{providerName}/Locations` gains a required `LocationType` body field, a string-named enum
per §4 (`"HPC"`, `"CLOUD"`, ...). A missing field or `"Unspecified"`, an undefined number such as `999`, or a
type with no `LocationType` row returns 400 `provider.invalid_location_type`, checked after the provider 404.
An unknown name such as `"Foo"` fails binding and returns 400 `validation.invalid_format`. Before this change the
endpoint inserted `LocationTypeId` 0 and answered 500 on the foreign key.

This is a breaking change for the portal, which must send `LocationType`: a coordinated cutover.

## Amendment (2026-09-23): location types cross the wire as names (T-195-02)

Every Core v2 contract that exposed a location type as `LocationTypeId`, or as the `LocationTypeId` +
`LocationTypeName` pair, now carries one `LocationType` field, the domain enum `Core.Domain.Shared.ELocationType`
serialized as its name per §4 (`"HPC"`). The six contracts:

- `GET v2/Locations`: each item; the `?locationTypeId=` filter becomes `?locationType=<name>`.
- `GET v2/Providers/{providerName}/Locations/{locationName}`.
- `GET v2/Providers/{providerName}/Resources`: each `Assignments[]` item; the `?locationTypeId=` filter becomes
  `?locationType=<name>`.
- `GET v2/Providers/{providerName}/Resources/{resourceName}`: each `Assignments[]` item.
- `GET v2/Projects/{projectShortName}/Extended`: each `AssignedResources[]` item.
- `GET v2/LocationTypes`: returns `[{ "Name": "HPC" }, ...]`, without `Unspecified` and without `Id`.

An unknown name in either filter fails binding and returns 400 `validation.invalid_format`.
`?locationType=Unspecified` binds and returns an empty list. Together with the required `LocationType` on
`POST v2/Providers/{providerName}/Locations` (the amendment above), no v2 location contract carries the
integer id any more.

The Context premise that "v2 has no released consumers" no longer holds: the portal consumes these contracts.
This is a deliberate breaking change. It reaches `main` together with the T-195-01 amendment above, as one
coordinated cutover with the portal and without a deprecation window that keeps `LocationTypeId`. §4 already
decided names over integers, so the change applies that rule rather than a new one. New location types are added over time, so a consumer of a v2 wire enum must
tolerate a name it does not know.

## Amendment (2026-09-23): allocation unit types as enum names, removed filters rejected (T-206)

Four v2 contracts still carried the allocation unit type as the `AllocationUnitType` table's `Name` column
(`"node-hours"`), while `GET v2/AllocationUnitTypes` and the assignment writes use the enum name
(`"NodeHours"`), so a client could not send back the value it read. Each now carries
`UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType` serialized as its name per §4:

- `GET v2/Projects/{projectShortName}/Extended`: `AssignedResources[].AllocationUnitTypeName` becomes
  `AllocationUnitType`.
- `GET v2/Service/ProjectsInfo`: `Assignments[].AllocationUnitTypeName` becomes `AllocationUnitType`.
- `GET v2/UserInfo/Extended`: `Clusters[].AllocationUnit` keeps its name and carries the enum name.
- `GET v2/Service/SyncDetails`: `Locations[].AllocationUnitType` keeps its name and carries the enum name.

The two renamed fields drop the `Name` suffix that described the lookup label. The two kept names are pinned by
the v1/v2 shape parity of the user-info contract. The location type fields of these contracts already carry
the enum name, since the `LocationType` table names equal it.

The T-195 cutover replaced four id query filters, and ASP.NET ignores a query parameter nothing binds, so a
client still sending the old one got the unfiltered list with 200. A request carrying a removed parameter now
returns 400 `validation.removed_parameter`, whose detail names the replacement:

- `GET v2/Locations` and `GET v2/Providers/{providerName}/Resources`: `locationTypeId` (use `locationType`).
- `GET v2/Providers`: `countryId` (use `countryCode`).
- `GET v2/Projects`: `domainId` (use `domainTypeName`).

The parameter name matches case-insensitively and the check ignores the value. Both changes belong to the same
coordinated cutover with the portal and the DDI consumers as T-195.

## Amendment (2026-09-24): every v2 route segment is PascalCase (T-242)

Every literal segment of every v2 route is PascalCase: `Projects/{projectShortName}/OnboardingRequests`,
`ProviderAdmin/Locations`, `Public/Requests/ByLink/{actionLink}`. A kebab segment joins into PascalCase, a
single word is capitalised, and a closed compound takes its domain word boundaries (`UserInfo`, which is also
the v1 spelling). The `api/v2` prefix and the route parameter names (`{projectShortName}`, `{slug}`) stay as
they are. The DDI routes (`DataStaging/*`, `Service/*`) already followed the rule.

This reverses the lower-case and kebab-case paths the Core v2 routes shipped with. Route matching ignores case,
so a caller that differs only in case still matches, but the 29 routes that lost a hyphen answer 404 on the old
spelling. v2 has no release yet, so the old spellings are dropped with no aliases. The portal and every other
v2 caller move to the new paths using `docs/guides/v2-route-casing-map.md`, which lists all 79 changed
operations. `V2RoutePascalCaseGuardTests` fails on any v2 literal segment in the `API_v2-*.json` snapshots
that is not PascalCase.

The gateway RBAC seed keeps every `endpointName` and changes only its `pathTemplate`s. `RbacSeedHostedService`
(ADR-0060) inserts by name and never updates, so the develop RBAC store is wiped and reimported after the merge.
