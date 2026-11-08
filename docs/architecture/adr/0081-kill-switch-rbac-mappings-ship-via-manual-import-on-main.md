# Kill-switch RBAC mappings ship to production via a manual import step on main

## Amendment (2026-07-16, same MR !310) — the Gateway restart is no longer required

Amended by [ADR-0084](0084-cache-invalidation-is-a-mandatory-part-of-the-importer-contract.md),
which fixes #210 — the defect that made the restart necessary. Both ship in !310, so this ADR is
edited in place rather than superseded.

**What changed:** the import now evicts the Gateway's endpoint-permission snapshot itself, so
**the mandatory Gateway restart is struck from the rollout step**. §1 below asserted the import
does not invalidate the cache and that the restart "is not optional". That was **true when
written** — established by running the rollout live against a real Gateway, not by reading code —
and it is what motivated fixing the shared import path. It is false as of ADR-0084.

**Why the fix landed here rather than as the follow-up §1 promised:** the kill-switch rollout
depends on the import path working, and shipping a feature on top of a known-broken rollout step
was judged worse than the extra surface (ADR-0084, Consequences).

**What is unchanged and still load-bearing:**

- The rollout still requires the **manual import with `overwriteExisting: true` on `main`** — the
  additive reconcile is only on `feat/refactor-core`. The whole Context below stands.
- The **verify-not-403 step stays.** It checks the outcome; the import summary alone still does not
  prove the mappings are live. ADR-0084 keeps it explicitly.
- §2's finding stands (`endpointName` is a unique NAME, not the route) — a separate, still-true
  finding. Its wording and its reading of the `Errors: 1` evidence are corrected below to match
  the code: a duplicate name aborts the whole endpoint-mappings import, not just one route.

**What the fix does NOT do:** eviction is process-local and no backplane exists, so a Gateway
**scale-out** would leave other replicas serving their own stale snapshot for up to the 30-minute
`LocalCacheExpiration`. ADR-0084 names this an accepted limitation (the three pre-existing evict
paths share it; the AppHost declares a single Gateway instance). The stale-snapshot failure mode is
narrowed, not made impossible.

## Context

The Gateway authorizes from DB-backed RBAC seeded from `roleManagmentRBAC.json`. An
unmapped route is **fail-closed 403**, so the four new `/api/Heappe/KillSwitch` endpoints
need `endpointMappings` entries (`perm:provider:admin` + `perm:platform:admin`) to work at
all.

The issue assumed the seed reconciles additively on startup, so new mappings "just appear."
That is true on `feat/refactor-core` (the `#242` / ADR-0060 change) but **not on `main`**:
`RbacSeedHostedService` imports only when the roles table is empty and with
`overwriteExisting: false` (it skips entirely once roles exist). Production `main` is
already seeded, so a fresh mapping added to the JSON is **never picked up on restart** — the
kill-switch endpoints would return 403 to everyone, i.e. be dead exactly during the incident
they exist for.

Porting the additive-reconcile behaviour onto `main` was considered and rejected for a
hotfix: larger blast radius on the Gateway's auth seeding, unrelated to the kill-switch.

## Decision

**Add the kill-switch mappings to `roleManagmentRBAC.json`, and make the one-time RBAC
import an explicit, documented rollout step: after deploy, an operator runs the RBAC import
with `overwriteExisting: true` (the admin-UI import) to load the new mappings, and then
verifies a newly-mapped route is not 403.** Do not rely on additive reconcile — it is not on
`main`.

> **Amended:** this decision originally ended "**and then restarts the Gateway**", and the
> paragraph below opened "The restart is not optional". Since ADR-0084 the import evicts the
> Gateway's snapshot in its own process, so **the restart instruction is withdrawn** — an
> operator following it wastes a restart on a cache that is already correct. The verification
> step replaces it in the sentence above; it was always required, and still is.

Two things were found by actually performing the import against a live Gateway:

1. **The import does not invalidate the Gateway's endpoint-permission cache.** *(No longer true
   — fixed by ADR-0084; see the note closing this finding. Retained as the record of why the
   restart step existed.)*
   `EndpointPermissionService` resolves every request against a snapshot cached under
   `EndpointPermissionService:Snapshot` (HybridCache). `CreateEndpointMappingAsync` /
   `DeleteEndpointMappingAsync` / `SetEndpointPermissionAsync` each evict it — but the
   *import* path (`EndpointMappingImportExport`) does not. So a successful import leaves the
   Gateway serving the stale snapshot, and the new routes keep returning fail-closed 403.
   Restarting the Gateway rebuilds the snapshot. (Pre-existing Forge.Core behaviour, not
   introduced here. #254 is simply the first feature that dies silently because of it. This ADR
   originally deferred the shared-import-path fix as "a follow-up, out of scope for this hotfix";
   that deferral was **reversed** — the fix is #210 / ADR-0084 and ships in this same MR.)

   **`DELETE /api/Administration/Cache` is NOT a substitute.** *(This ADR originally named it
   `POST /Administration/ResetAllCaches`, a route that has never existed; corrected by
   ADR-0090.)* It evicted the same snapshot key by name (it no longer does — see the ADR-0090
   amendment below), and it runs in the **Core API** process against Core's HybridCache; the
   Gateway's cache is process-local (`ServiceExtensions.cs`, `AddDistributedMemoryCache()`), so
   Core cannot reach it. Anyone reaching for that endpoint will see it "not work" and
   misdiagnose. ADR-0084 sharpens this: the snapshot's **only read path** is the Gateway's
   `AuthorizationHelpers.cs:29` (`InternalTokenTransformExtensions.cs:69,120` merely *resolve*
   `IEndpointPermissionService` and hand it to `AuthorizationHelpers`; they do not read the
   snapshot themselves) — nothing in Core ever reads it, so Core's reset evicted a key its own
   process never populates. It is not merely unable to reach the
   Gateway; it is a no-op against a cache Core never fills. Left alone, the stale snapshot expires
   on its own after the Gateway's 30-minute `LocalCacheExpiration` — which is worse than a clean
   failure: the rollout looks broken, then fixes itself half an hour later, and nobody learns why.

   **Amendment (ADR-0084): this finding no longer holds.** `EndpointMappingImportExport` now
   evicts the snapshot through `IEndpointPermissionService.InvalidateSnapshotAsync`, called by a
   mandatory hook the importer base class cannot skip. Because the admin-UI import is Blazor
   Server running **inside the Gateway process**, that eviction lands in the same process that
   serves the snapshot — which is exactly why the `ResetAllCaches` trap above does not apply to
   it. **No Gateway restart is needed after an import.** Caveat: eviction is process-local, so a
   Gateway scale-out would still strand other replicas' snapshots for up to 30 minutes — an
   accepted limitation, not a solved problem.

   **Amendment (ADR-0090): `ResetAllCaches` no longer targets the snapshot key, or any key, by
   name.** It now issues one `HybridCache.RemoveByTagAsync("*")`, wiping every entry in the
   Core process (in both cache tiers) regardless of tag — every entry held by Core's
   `HybridCache`, that is; in-process caches outside it, such as the authentication middleware's
   OIDC metadata and signing keys, survive. The trap this paragraph describes is unchanged in
   substance: the endpoint still only ever reaches Core's own HybridCache, never the Gateway's,
   so reaching for it as a Gateway-cache substitute is still a mistake. The explicit
   `jwks:json` removal it used to attempt is gone too — that key was never reachable from the
   Core process either, so nothing is lost.

2. **`endpointMappings[].endpointName` is a unique NAME, not the route.** It carries a unique
   index (`IX_EndpointMappings_EndpointName_Unique`); the route lives in `pathTemplate`, and
   `httpMethod` separates verbs on the same path — see the existing `/api/ProjectTag` entries
   ("Create Project Tag" / "List Project Tags" / "Update Project Tags"). Naming a mapping after
   its path collides as soon as two verbs share a path, and the collision does not surface until
   the single `SaveChangesAsync` outside the item loop (`EndpointMappingImportExport.cs:309`):
   the duplicate-key violation throws there, is caught by the outer `catch` (`:322-325`), and
   aborts the **entire endpoint-mappings section** — no mapping in that section commits, not
   just the colliding route. The file's permissions and roles sections are separate imports
   (`RoleManagementService.cs:439/453/467` calls each feature's `ImportAsync` sequentially,
   each against its own `IDbContextFactory` context and its own `SaveChangesAsync`, no shared
   transaction) and are unaffected — they are the `Updated: 79` the admin UI reports alongside
   `Errors: 1`. `RbacSeedFileTests` now pins the uniqueness so this fails in CI instead.

   **Read that summary correctly** (clarified per ADR-0084 §2 — the observation stands, its
   interpretation is the trap): those numbers are the **admin UI's aggregate**.
   `RoleManagementService.ImportRolesAsync` sums counts across all three features, so the 79
   updates were permissions+roles, while the endpoint-mappings import failed **whole** — its
   duplicate-key `DbUpdateException` hit the outer catch, committing nothing and returning no
   counts. It is *not* an instance of one importer writing rows while reporting an error, and
   must not be cited as one.

   **This re-reads the live #254 rollout as two sequential defects, not one.** The first import
   attempt (`Errors: 1`) wrote no endpoint mappings at all — the naming collision aborted that
   section whole before any mapping was applied — while its permissions and roles sections still
   committed independently (the `Updated: 79` above). So the 403s seen at that moment were a
   **missing-mapping-data** problem, not a stale-cache one: the kill-switch routes specifically had
   nothing in the DB to authorize against, regardless of what the Gateway's snapshot cached. The
   stale snapshot in §1 was the *second* import's problem: the one that fixed the collision, wrote
   the endpoint mappings successfully, and — before ADR-0084 — still needed the Gateway restart to
   take effect. A reader debugging a 403 against this ADR needs to tell which of the two they are
   looking at: an import whose mappings failed to write (§2), or one whose mappings wrote but the
   Gateway hadn't seen them yet (§1, now fixed).

**Verifying the rollout worked is therefore part of the step**, not an afterthought: after the
import, call one of the new endpoints through the Gateway and confirm it is not 403. The import
summary alone does not prove the mappings are live — as §2 shows, a summary can report updates
while the mapping you care about never landed. This step is unaffected by ADR-0084 and remains
mandatory; only the restart that used to precede it is gone.

## Rollout step (`main`)

1. Deploy.
2. Run the RBAC import from the admin UI with `overwriteExisting: true`.
3. Call one of the new `/api/Heappe/KillSwitch` endpoints through the Gateway and confirm it is
   **not 403**.

No Gateway restart (removed by ADR-0084). If step 3 still 403s, do **not** reach for
`DELETE /api/Administration/Cache` — see §1. Check the import summary for `Errors` and the
`endpointName` collision in §2 first; a restart remains a valid last-resort escape hatch, not a
routine step.

## Consequences

- **The PR's rollout notes must state this loudly**, and the task is not "done" until the
  import step is documented. A silent deploy ships fail-closed endpoints.
- **No code change to the seeding service** — smallest possible surface for a hotfix. *(Amended:
  still true of the seeding service; ADR-0084 does change the shared importer path in this MR.)*
- **OpenAPI snapshots** (`API_user.json` / `API_administration.json` at repo root; the
  kill-switch routes live in `API_service.json` since 2026-09-30) are build
  regenerated when the endpoints are added; their diff is part of the PR.
- When this feature ports to `feat/refactor-core`, the additive reconcile there makes the
  manual step unnecessary — the rollout note is `main`-specific.
