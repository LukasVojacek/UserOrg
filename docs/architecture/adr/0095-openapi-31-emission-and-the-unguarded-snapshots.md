# ADR 0095 - Build-time OpenAPI is emitted as 3.1; the committed snapshots stay unguarded

- Date: 2026-08-13
- Status: accepted

## Decision

1. **The build emits OpenAPI 3.1.** `src/Core/API/API.csproj` passes
   `--openapi-version OpenApi3_1`; all six root snapshots (`API_user.json`,
   `API_administration.json`, `API_service.json`, `API_v2-user.json`, `API_v2-provideradmin.json`,
   `API_v2-admin.json`; `API_service.json` since 2026-09-30, ADR-0021 amendment) are committed as `"openapi": "3.1.1"` documents.
2. **`Microsoft.OpenApi` stays at `[2.11.0]`.** The nullability drift is fixed from the *other* end —
   by asking the writer for the version whose semantics it already computes — not by pinning the
   security update back.
3. **The `NuGetAuditSuppress` for `GHSA-v5pm-xwqc-g5wc` is deleted**, together with its rationale
   comment: it had been unnecessary since the July pin bump, and the comment was wrong about the
   build besides.
4. **No parity check between the committed snapshots and a fresh build was added.** That gap is
   still open, and this record states it as an open risk rather than a solved problem.
   `Microsoft.AspNetCore.OpenApi` is pinned as a **floor**, so the output can move again.
5. **`ProviderIdentityResolver` no longer holds a `DbContext`** — each `HybridCache` factory delegate
   creates its own from `IDbContextFactory`. The race this closes is **not proved by test**, only made
   impossible by an invariant with a stated boundary, and **a second instance of the same shape is
   left in place** (`ProjectService.cs:622`). All three caveats are in the G1 section below.
6. **The other two audit items (G2, G3) closed with no code change** — one clean as claimed, one
   overstated. Recorded because "we looked and found nothing" is the finding.

### The bug: a 3.0 document written by a 3.1 object model

`Microsoft.OpenApi` 2.x represents nullability the JSON-Schema (3.1) way — a null union — regardless
of the output version it is told to write. Asked for 3.0 it down-translates that only where the
schema offers a slot:

- a `$ref`-typed property has a `oneOf` array, so the writer adds a null branch
  (`oneOf: [{"enum":[null]}, {"$ref": …}]`) — legal, if unidiomatic, under a 3.0-declared document;
- a bare scalar in these documents carries **no `type` keyword at all** — an `int?` renders as
  `pattern` + `format: int32` only — so there is nothing to attach a null marker to, and no fallback
  to the 3.0 `nullable` keyword either. The nullability was **silently dropped**.

The document's own `openapi` field stayed `3.0.4` throughout, which is why this surfaced as data loss
rather than as an obvious version mismatch. It appeared when `Microsoft.OpenApi` moved
`[2.7.5]` → `[2.11.0]` (commit `3b3809f7`, 2026-08-12) without anyone rebuilding the snapshots: every
subsequent build dirtied all five files, and the version reported by the working-tree diff was the
*correct*, pre-drift one. Four contributors hit that dirty worktree over two days, each of them
looking at a diff whose committed side was the truthful one — "just regenerate and commit" would have
published a spec that under-declares nullability for real clients.
Bisect, mechanism, and the contributor timeline above: `state/research/openapi-nullable-drift.md`,
`state/research/openapi-drift-bisect.md`.

### Why 3.1 rather than pinning the package back

**Because it removes the mismatch instead of working around it: stop asking a 3.1-shaped writer to emit
3.0.** Under 3.1 the same nullability is expressed as a type union the writer computes natively, so the
scalar-drops-`nullable` case cannot arise at all. The two alternatives — pinning `Microsoft.OpenApi`
back to `[2.7.5]` (restores byte-identical snapshots, verified in the bisect) and post-processing the
generator's output to re-insert `nullable: true` — are recorded with their costs under **Rejected
alternatives** below.

It also removes an inconsistency that predates the drift: the runtime document served by
`MapOpenApi` has been 3.1 by .NET 10 default all along — the very csproj comment deleted by this
change said so ("The runtime document (served via MapOpenApi) stays at the default 3.1"). The
committed artifact now matches what the API actually serves.

### The recorded Spectral blocker was false

`API.csproj` justified the 3.0 downgrade with:

> Emit OpenAPI 3.0 at build time: Spectral's oas ruleset/parser does not fully support 3.1 and
> crashes on it.

That was untrue when it was written (2026-06-23) and it blocked the switch for nearly two months.
Spectral gained OpenAPI 3.1 support in **v6.0.0** (2021-07-14, per the upstream changelog);
`lint_openapi` pins
`stoplight/spectral:6.15` (`.gitlab/stages/.security_stage.yml:59`) — five majors/minors past it.
The claim was disproved by execution, not by argument: the pinned CLI (6.15.1) was run with this
repo's own `.spectral.yml` against the real 3.1 output this project emits, and reported **289 problems
(241 errors, 48 warnings)** — no crash, no parser error, and identical in count to the 3.0 control
run of the same command. Those figures are reported from that run
(`state/research/spectral-oasdiff-31.md`); they were not re-executed while writing this record. The
comment was deleted rather than softened; a blocker that survives only because nobody re-ran it is not
a blocker.

### What the regeneration actually changed — measured against the diff, not assumed

Verified by walking the old and new documents structurally (`be0a947b` vs this branch), not by
counting diff lines:

- `nullable: true` appears **549** times in the 3.0 snapshots. In the 3.1 output **543** schemas
  carry a `type` array containing `"null"`, and the remaining **6** — the `$ref`-typed properties —
  become `oneOf: [{"type":"null"}, {"$ref": …}]`.
- **181 schema nodes gain a `type` keyword that the 3.0 output omitted entirely**, and **117 of them
  are not nullable at all**. So this is not purely a nullability re-encoding: a large fraction of the
  diff is type information that was simply missing from the published 3.0 documents. An `int?`
  property now reads `"type": ["null", "integer", "string"]` alongside its existing `pattern` and
  `format: int32`.
- `LocationModel.Provider` moves from `{"type":"object","oneOf":[{"$ref": …}],"nullable":true}` to
  `{"oneOf":[{"type":"null"},{"$ref": …}]}` — the `"type":"object"` disappears, which reads as a loss
  until you notice the `$ref` it wrapped is unchanged.
- No other schema keyword changes anywhere in the five files: the only keys added or removed across
  the whole regeneration are `type`, `oneOf`, and `nullable`. `description`, `required`,
  `deprecated`, `readOnly`/`writeOnly`, `example`/`examples` are untouched, and no 3.1-only keyword
  (`$schema`, `const`, `prefixItems`, `webhooks`) appears.

One test asserted the 3.0 shape and had to move with it:
`NoPrimitive200BodyCharacterizationTests` classified a body as composite by comparing `type` as a
JSON **string**. Under 3.1 `type` may be a union array, so the guard now treats a schema as composite
only when **every non-null member** is `object` or `array` — a union such as `["string","object"]`
can still legally arrive as the bare scalar the characterization exists to forbid.

### The oasdiff cutover caveat — one commit only

`openapi_diff` compares the committed `API_user.json` at `HEAD~1` against the build artifact at
`HEAD`. Across the 3.0 → 3.1 flip that comparison reports **~65 "breaking change" errors** produced
by the version-format transition itself; a 3.1-vs-3.1 diff of the same document is clean. That count,
like the Spectral figures above, is reported from `state/research/spectral-oasdiff-31.md` and was not
re-executed here. The job is `allow_failure: true` (`.gitlab/stages/.security_stage.yml:79-103`), the
noise is confined to the flip commit, and it self-resolves on the next one. It must not be read as a contract break, and no attempt
was made to silence it.

### The suppression was unnecessary before this task touched it

`GHSA-v5pm-xwqc-g5wc` affects `Microsoft.OpenApi` up to and including 2.7.4 — 2.7.5 is the patched
release, as this repo's own `Directory.Build.props` comment already stated. The pin moved to
`[2.7.5]` on 2026-07-14 (`55408816`) and to `[2.11.0]` on 2026-08-12 (`3b3809f7`), so NuGet audit has
been evaluating a patched resolved version for a month; the suppression was inert. Its comment was
also actively misleading: it described `Microsoft.OpenApi` as "pinned to 2.0.0 by
Microsoft.AspNetCore.OpenApi 10.0.10" and claimed the bundled source generator "fails to compile
against the patched 2.11.0+ line — confirmed by testing the bump directly", while the repo has been
building with 0 errors on 10.0.11 + 2.11.0. Removing the suppression is therefore independent of the
3.1 decision: it would have been correct on either side of it.

### The committed snapshots are still unguarded — an open risk, not a closed one

Nothing in CI asserts that a committed `API_*.json` equals what a build produces now:

- `lint_openapi` covers **2 of the 5** files (`API_user.json`, `API_administration.json`) and
  `openapi_diff` covers **1** (`API_user.json`); the three `API_v2-*.json` are never linted or diffed
  at all. The build job publishes only those same two as artifacts
  (`.gitlab/stages/.build_stage.yml:11-12`).
- `openapi_diff` compares the committed file at `HEAD~1` against the fresh artifact at `HEAD` — it
  detects breaking changes **between two commits**, never a mismatch between the committed file and a
  rebuild of the same commit.
- Both jobs are `allow_failure: true`.
- Both extend `.rules-mr-to-main-or-feature` (`.gitlab-ci.yml:139-145`), which runs on `feat|fix|hotfix`
  **pushes** and on MRs targeting the default branch only — so on this repo's actual integration path,
  MRs into `feat/refactor-core`, they do not run at MR time.

A parity check (rebuild, diff all five against the committed copies, fail loudly) was **deferred on
cost**, not because the drift stopped mattering. Three pipeline facts make it more than a one-line
job: the build job publishes 2 of 5 files, so three would need new artifacts; the build **overwrites**
the committed files in its own workspace, so the comparison has to capture them before it runs; and
`git` is unusable inside these jobs — the runner clones as one user while the job runs as the non-root
`app` user, so git rejects the checkout with `fatal: detected dubious ownership`
([ADR 0089 — *a partial dependency scan fails the job*](0089-a-partial-dependency-scan-fails-the-job.md),
§29-33, where the same constraint forced
`find` in place of `git ls-files`).

Until that check exists, these five files remain a manually maintained artifact that drifts silently.

**And the generator itself can still move: `Microsoft.AspNetCore.OpenApi` is pinned as a floor
(`10.0.11`), not as an exact version the way `Microsoft.OpenApi` is (`[2.11.0]`).** The next dependency
sweep can change the output again, and the same class of drift will land the same way — invisible until
someone runs a full build and reads the diff by hand. Switching to 3.1 removed the cause of *this*
drift, not the class of it.

## The `ProviderIdentityResolver` scoped-context capture (G1) — real, fixed, incompletely proved

`ProviderIdentityResolver` closed over its request-scoped `UserOrgDbContext` inside both
`HybridCache.GetOrCreateAsync` factory delegates (`state: (dbContext, providerId)` and
`state: (dbContext, idpUserId)`). The context is **pooled** (`AddPooledDbContextFactory<UserOrgDbContext>`,
`ForgeCoreExtensions.cs:96`), and the two cache keys — `providername:{providerId}`,
`assignedprovidername:{idpUserId}` — are keyed on the provider or the user, never on the request, so
concurrent requests collide on them as ordinary traffic.

Under `HybridCache` 10.9.0's stampede dedup — mechanism per
`state/research/hybridcache-cancellation-path.md`, reflected from the shipped assembly rather than
re-derived here — the **winning** caller's captured state is queued onto a detached `ThreadPool` work
item, and the shared operation is cancelled only when the **last** enlisted caller leaves. A winner that disconnects while another caller is still enlisted therefore has its
request scope disposed — returning that pooled context to the pool, where the pool may lease it to an
unrelated request — while the background factory is still querying through it.

**Fix:** the resolver's primary constructor now takes `IDbContextFactory<UserOrgDbContext>`, and each
factory delegate creates and `await using`s its own context per invocation. The class holds no
`DbContext` field at all, so no request-scoped context can reach a delegate.

Three limits of the proof, recorded deliberately:

- **The race itself is not proved by test** — only the invariant that makes it impossible. Proving it
  red-before-green needs a real `DefaultHybridCache` plus an interleaving gate (an EF
  `DbCommandInterceptor` and a `TaskCompletionSource`, roughly a hundred lines that do not exist in
  this repo). It was considered and rejected: a bespoke interleaving harness is itself a flakiness
  source, and a flaky guard on a concurrency invariant is worse than a stated gap.
- **The reflection test's invariant has a boundary.**
  `Should_HoldNoDbContextField_SoCacheFactoriesCannotBeGivenARequestScopedContext` proves no
  `DbContext`-typed **field** exists on the resolver. A context arriving as a **method parameter** and
  entering a state tuple from there would pass that test unchanged. No such path exists today; a
  future one would need its own guard.
- **The verdict was reversed twice.** The hazard was first closed as "not a hazard"
  (`state/research/hybridcache-scoped-capture.md`), then reopened, then settled as real by reflecting
  the shipped `Microsoft.Extensions.Caching.Hybrid` 10.9.0 assembly and cross-checking upstream
  `dotnet/extensions` at tag `v10.9.0` (the same note cited above). The first
  analysis was not sloppy — it was a chain of individually true statements (the resolver is Scoped,
  both calls are awaited inline, the two call sites are mutually exclusive per request) that never
  reached the dedup path, which is precisely the case where "awaited inline" stops holding. Recorded
  because the shape of that error is more reusable than its conclusion.

**A second instance of the same shape exists and was left alone.**
`ProjectService.ProjectTemplateInfo` (`src/Core/Application/UserOrg/Services/ProjectService.cs:622`)
captures `this` — and therefore its scoped `IUserOrgUnitOfWork` — inside a `HybridCache` factory
delegate. It is milder: that unit of work sits on a plain `AddDbContext` registration, not a pooled
one, so a disposed-scope hit surfaces as an `ObjectDisposedException` rather than as one request
silently reading through another's leased context. Out of scope for this task, recorded here so it is
found deliberately rather than rediscovered.

## What was checked and found clean (G2, G3) — no code changed

**The wider `AddIdentity` claim audit is clean.** Exactly **two** `AddIdentity` call sites exist in
the whole solution, both in the gateway's `OnTokenValidated`
(`UserOrg.Gateway/Startup/ServiceExtensions.cs:397`, `:443`). Beyond `am` and `provider_id` they
append `sub`, `preferred_username`, `email` and `role` — each either a copy of the already-validated
IdP identity's own value or dead for authorization (`role` on the gateway principal is overwritten
from the DB permissions cache before use). No claim satisfies all three of: gateway-appended,
authorized on, and absent from
[ADR 0094 — *interactive ProviderAdmin identity*](0094-interactive-provider-admin-identity-and-the-write-asymmetry.md)'s strip list. The
strip needs no widening.

**The "unstampable fixture roles" item was overstated and needs no fix.** Test `Caller` fixtures pair
`IsServiceKeyIdentity: true` with role strings the API-key handler can never stamp — 19 occurrences
across 12 files. All are harmless. `Caller.Role` is *authorized on* in exactly seven places, all of
them a case-insensitive equality against the configured admin role name
(`IOptions<WellKnownRoles>.Value.DefaultAdminRoleName`, `"Admin"` under the default binding;
`ProviderWriteAuthorization.cs:17`, `ReadScope.cs:41`, and five endpoint admin guards), and none of
the fixture strings equals `"Admin"`. It is *read* in three further places that gate nothing —
`UserInfoLoader.cs:54` and `:73` project it into the UserInfo response body, and
`UserInfoCacheHelpers.cs:21` folds it into a cache-key hash — and none of the twelve files exercises
UserInfo, so no test outcome depends on them; outcomes are driven by `IsServiceKeyIdentity` and
`ProviderName`, and the `Caller` doc comment forbids deriving `IsServiceKeyIdentity` from `Role` at
all. The original framing — that the roles are unproducible — was wrong about the roles and right
only about the *pairing*: `"User"` and
`"ProviderAdmin"` are ordinary interactive roles, impossible only in combination with a service-key
identity.

> **Note 2026-10-01.** The seven admin checks are now one: `ProviderWriteAuthorization` and the five
> endpoint guards call `caller.IsAdmin(roles)` (`CallerReadScope.IsAdmin` in `Shared/Auth/ReadScope.cs`),
> which holds the only case-insensitive comparison against `DefaultAdminRoleName`.

The residual worth naming: there is **no shared `Caller` fixture builder**. Every test file that needs
one hand-rolls it, which is why a nonsense pairing could spread across twelve of them without a single
review catching it. Recorded, not fixed here.

## Context

- The six root `API_*.json` are generated at build time by `Microsoft.Extensions.ApiDescription.Server`
  from one project (`src/Core/API/API.csproj`, `OpenApiDocumentsDirectory=../../..`), which registers
  all six documents (`V1CoreExtensions.cs`, `V2CoreExtensions.cs`), and they are committed
  so the CI OpenAPI jobs have something to read.
- The drift and the three audit items above all arrived as wave-2 follow-ups on
  [ADR 0094 — *interactive ProviderAdmin identity*](0094-interactive-provider-admin-identity-and-the-write-asymmetry.md); two of the three
  audits closed with no code change, and saying so explicitly is the point of recording them.

## Consequences

- **The published contract now declares nullability accurately, and declares types it previously
  omitted.** A client generator reading these documents sees `type` on 181 schemas that had none, and
  a null union wherever the C# model is nullable. This is a client-visible change to the documents,
  not to any endpoint's behaviour.
- **Anything that consumes these files must accept 3.1.** The two CI consumers do
  (Spectral ≥ 6.0.0, oasdiff); a repo-wide search found no client generator (`nswag`, `kiota`,
  `openapi-generator`) reading them.
- **`docs/ci-cutover.md` no longer claims the files are 3.0.** It was corrected in the same change,
  along with the `Directory.Build.props` pin comment.
- **The drift can recur.** Nothing compares the committed snapshots against a fresh build; the next
  generator bump can move the output again, and the first symptom will again be an unexplained dirty
  worktree.
- **`ProviderIdentityResolver` no longer holds a `DbContext`.** Its constructor signature changed, and
  any future call site or test must supply `IDbContextFactory<UserOrgDbContext>`. Each cache miss now
  creates and disposes one context — the cost the fix trades for the invariant. What is guarded is the
  invariant (no `DbContext` **field**), not the race: no test reproduces the race, and a context
  arriving as a method parameter would still pass the guard.
- **The same capture shape is still live at `ProjectService.cs:622`** — knowingly left, milder because
  the context there is not pooled, and unguarded by any test. Anyone touching `HybridCache` factories
  should treat it as the next instance, not as a hypothetical.

## Rejected alternatives

- **Pin `Microsoft.OpenApi` back to `[2.7.5]` (rejected).** Restores the snapshots byte-identically
  and was verified to do so, but re-opens `GHSA-v5pm-xwqc-g5wc` and preserves the underlying mismatch —
  a 3.1-shaped writer emitting 3.0 — for the next sweep to trip over.
- **Regenerate and commit under 3.0 (rejected).** The generated 3.0 output is *wrong*: it drops
  `nullable: true` from scalars whose C# models are genuinely nullable. Committing it would have made
  the published spec less accurate than the file it replaced.
- **Post-process the generated 3.0 documents to re-insert `nullable: true` (rejected).** Keeps the
  security update and the declared version, at the price of a permanent hand-maintained correction step
  on an artifact nothing verifies.
- **Silence the one-time oasdiff cutover noise (rejected).** The job is `allow_failure`, the noise is
  confined to one commit, and any suppression added for it would outlive the reason for it.
- **Prove the `HybridCache` race with an interleaving harness (considered, declined).** A
  `DbCommandInterceptor` + `TaskCompletionSource` gate around a real `DefaultHybridCache` would
  demonstrate the race, at the cost of a bespoke concurrency harness in the test suite. The invariant
  test plus this record was judged the better trade; see the G1 caveats above.
