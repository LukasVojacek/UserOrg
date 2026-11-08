# Implementation plan — v2 pagination → headers, ETag rollout, optimistic concurrency

Implements ADR-0020 amendments (2026-06-16) and ADR-0023. Three independent workstreams; **A** and **B** can
ship in parallel, **C** is a separate later decision.

> **Status (2026-06-16): Workstreams A and B DONE.** All 17 list endpoints emit the bare array +
> `X-Total-Count`/`Link` headers; `PagedResponse` helper added (7 unit tests); `PagedResult<T>`/`PageMeta` and
> `PagedResultTests` removed; CORS `WithExposedHeaders("Link","X-Total-Count","ETag")` added. **B:** `.WithETag()`
> now ships on all six cached GETs (`GetUserInfo`, `GetExtendedUserInfo`, `GetProject`, `GetProjectTemplates`,
> `GetCountries`, `GetStatistics`); the previously-untested `ConditionalGetMiddleware` 304 mechanism is now
> covered by 8 unit tests. Core.UnitTests 462 passed / 8 skipped / 0 failed. **Still open:** the YARP Gateway
> header-forwarding check (A1) and the real-SQL integration suite for paged reads. **C** (optimistic
> concurrency) not started.

Scope anchors (verified 2026-06-16):
- **17 list endpoints** call `PagedResult.Offset/Complete` (9 growable, 8 bounded reference) and 17 use
  `.Produces<PagedResult<…>>`.
- Read-side ETag already exists: `ConditionalGetMiddleware` + `ETagMetadata`/`.WithETag()`, applied to 2
  endpoints only.
- CORS policy (`Program.cs:296`) is `AllowAnyOrigin/Header/Method` with **no** `WithExposedHeaders`.
- `UseCoreConditionalGet()` (`Program.cs:336`) is correctly ordered before `UseOutputCache()` (`:337`).

---

## Workstream A — pagination metadata to headers

**A1. CORS expose-headers (do first; unblocks everything browser-side).**
- `Program.cs:298` — add `.WithExposedHeaders("Link", "X-Total-Count", "ETag")` to the default policy.
- Verify the YARP Gateway forwards these response headers unchanged (no header allow-list stripping them).
- Verify: cross-origin GET shows `Link`/`X-Total-Count`/`ETag` readable in the browser; integration assert on
  `Access-Control-Expose-Headers`.

**A2. Shared header helper** in `Core/Shared/Paging/`.
- `PagedResponse.Offset<T>(HttpContext http, IReadOnlyList<T> data, int page, int limit, int totalItems)` —
  sets `X-Total-Count`; builds `Link` with `first/prev/next/last` rels (only those that exist), **preserving
  all other query params** from `http.Request.Query` and overriding only `page`/`limit`; returns
  `Results.Ok(data)` (bare array).
- `PagedResponse.Complete<T>(HttpContext http, IReadOnlyList<T> data)` — sets `X-Total-Count` only; returns
  `Results.Ok(data)`.
- Keep the offset math currently in `PagedResult.Offset` (totalPages/has-next derivation) — it now decides
  which `Link` rels to emit.
- Verify: unit tests for link building (page 1 has no `prev`; last page has no `next`; middle page has all;
  filters preserved; empty result → `X-Total-Count: 0`, no `Link`).

**A3. Pilot — `GetProjects` end-to-end** (reference pattern).
- Swap `Results.Ok(PagedResult.Offset(...))` → `PagedResponse.Offset(http, ...)`; add `HttpContext http` param.
- `.Produces<PagedResult<Response>>` → `.Produces<IReadOnlyList<Response>>`; document the two response headers.
- Update `GetProjectsEndpointTests` to assert bare-array body + `Link`/`X-Total-Count` headers.
- Verify: `dotnet test --filter-query "/*/*/*/*GetProjects*"` green.

**A4. Fan out** to the remaining 16 endpoints, one PR per aggregate (matching the F-series cadence):
- Growable (`PagedResponse.Offset`): `GetProviders`, `GetRequests`, `GetSentRequests`, `GetUsers`,
  `GetResources`, `GetProjectTags`, `GetLocations`, `GetProjectUsers`.
- Bounded reference (`PagedResponse.Complete`): `GetCountries`, `GetLocationTypes`, `GetOperationModes`,
  `GetProjectDomainTypes`, `GetRequestStates`, `GetRequestTypes`, `GetValidRoles`, `GetProjectTemplates`.
- Each: handler swap + `.Produces<>` swap + test update (body shape + headers).

**A5. Remove dead envelope types.**
- Delete `PagedResult<T>`/`PageMeta` (and `PagedResultTests`) once no call site remains. `PageRequest` stays
  (still binds `?page=`/`?limit=`). Verify: solution builds, `grep PagedResult` returns nothing in `Features`.

---

## Workstream B — roll out read-side ETag to all cached GETs — **DONE (2026-06-16)**

The mechanism exists; this was application + exposure only.
- ✅ Added `.WithETag()` to the four remaining cached GETs (`GetProject`, `GetProjectTemplates`, `GetCountries`,
  `GetStatistics`); `GetUserInfo`/`GetExtendedUserInfo` already had it. No combined `.CacheOutputWithETag(...)`
  helper was added — it would diverge from the existing two-call pattern for marginal benefit (6 call sites).
- ✅ `ETag` exposure handled by A1 (`WithExposedHeaders`).
- ✅ The `ConditionalGetMiddleware` 304 mechanism (previously untested) is now covered by
  `ConditionalGetMiddlewareTests` — strong-ETag emission + `Cache-Control: private, no-cache`, `If-None-Match`
  match → `304` empty body, no-match → `200` + body, weak-validator and `*` matching, and pass-through for
  non-GET / no-metadata / non-200.
- **Open (live):** end-to-end per-endpoint check through the running pipeline — first GET returns `200` +
  `ETag`; repeat with `If-None-Match: <etag>` returns `304` empty body; a mutation invalidates the hash so the
  next GET returns `200` + new `ETag`.

---

## Workstream C — optimistic concurrency (ADR-0023, opt-in, separate)

Only for `UpdateProject`, `ChangeProjectUserRole`, `UpdateProvider` initially.
- **C1.** Add a shadow rowversion to each guarded aggregate's EF configuration (`IsRowVersion()`); domain type
  unchanged. One migration per aggregate (overlay-migration acceptance per the modular-migrations rule:
  probe migration touches no aggregate tables it shouldn't).
- **C2.** The aggregate's single-GET emits a **strong ETag = rowversion** (distinct from the body-hash ETag).
- **C3.** The write endpoint reads `If-Match`; set it as the rowversion original value; translate
  `DbUpdateConcurrencyException` → `412` (`concurrency.precondition_failed`); missing `If-Match` → `428`.
- **C4.** Contract tests: stale token → `412`; missing token → `428`; matching token → success and the ETag
  advances.
- Verify: concurrent-edit integration test (load v1, two edits, second gets `412`).

---

## Sequencing & risk

1. **A1 → A2 → A3** first (CORS + helper + pilot establishes the pattern and proves CORS/Gateway).
2. **A4** fan-out and **B** rollout can interleave per aggregate (touch the same files once).
3. **A5** cleanup after the last A4 PR.
4. **C** independently, gated on ADR-0023, only for the three guarded aggregates.

Cross-cutting verification before release: real-SQL integration suite for the paged reads (the unit/EF-in-memory
tests don't exercise SQL paging order/tie-breakers), per the standing ADR-0020 note.
