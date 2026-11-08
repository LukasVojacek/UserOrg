# Cache invalidation is a mandatory part of the importer contract, enforced by the base class

Fixes issue #210. Amends [ADR-0081](0081-kill-switch-rbac-mappings-ship-via-manual-import-on-main.md),
whose rollout step exists only because of the defect this ADR removes.

## Context

The Gateway authorizes every request against a snapshot `EndpointPermissionService` caches
under `EndpointPermissionService:Snapshot` (`EndpointPermissionService.cs:24`, read at
`:72-76`). Three write paths evict it — `SetEndpointPermissionAsync` (`:271`),
`CreateEndpointMappingAsync` (`:321`), `DeleteEndpointMappingAsync` (`:357`). The **import**
path does not: `EndpointMappingImportExport` injects only `IDbContextFactory` + `ILogger`
(`:24-31`) and, after `SaveChangesAsync` (`:299`), merely logs and returns. A successful
import therefore leaves the Gateway serving the stale snapshot, and newly-mapped routes keep
returning fail-closed 403 until the process restarts or the 30-minute `LocalCacheExpiration`
lapses. Reported in #210; hit live during the #254 rollout (ADR-0081).

Facts established before deciding:

- **The admin-UI import runs in the Gateway process.** The Role Management page is Blazor
  Server *inside* the Gateway (`RoleManagementPage.razor:1`); `ImportExport.razor:182` calls
  `RoleManagementService.ImportRolesAsync` as an in-process C# call over the SignalR circuit
  (`RoleManagementService.cs:453` invokes the endpoint-mappings importer). Nothing is proxied
  to Core. So an eviction inside the importer lands in the same process that serves the
  snapshot — the `ResetAllCaches` trap (ADR-0081 §1) does **not** apply here.
- **The snapshot has exactly one consumer, and it is the Gateway**
  (`AuthorizationHelpers.cs:29`, `InternalTokenTransformExtensions.cs:58,108`). Core registers
  `EndpointPermissionService` via `AddForgeCore()` (`Program.cs:145`) but never reads it —
  which sharpens ADR-0081's finding: Core's cache reset evicted a key its own process never
  populates. *(Amended by [ADR-0090](0090-reset-all-caches-uses-the-reserved-wildcard-tag.md):
  the reset — `DELETE /api/Administration/Cache` — no longer names that key, or any key; it
  wildcards Core's whole HybridCache. The point survives: the wipe is still Core-process-only,
  so it still cannot refresh the Gateway's snapshot.)*
- **No backplane exists.** Both hosts use `AddDistributedMemoryCache()` +
  `LocalCacheExpiration = 30 min`; zero Redis references in the repo. Eviction cannot cross a
  process.
- **`RoleImportExport` carries the identical defect**: `RoleService` caches role lists
  (`RoleService.cs:19-20`, 5-min) and evicts `CacheTags.RoleList/RoleNames` on every
  mutation; the importer never does.
- **So does `PermissionImportExport`** — established late, by the independent review of the block
  that was supposed to declare it exempt. "Permissions are not cached" is true of
  `PermissionService` and false of the system: `Role.RolePermissions` holds the `Permission`
  **entities**, and `ToPermissionInfo()` reads `permission.Description` into the `RoleInfo` graph
  that `RoleService.ListRoles` caches. An `OverwriteExisting` permission import rewrites
  descriptions and invalidates nothing, so role lists serve stale text for up to 5 minutes. Milder
  than the other two (no authorization decision changes — `PermissionKey` is the match key and is
  never rewritten; only display text goes stale), but the same defect class.
- **The base class cannot simply wrap the import.** `TypedForgeImportExport.ImportAsync(JsonElement)`
  is `virtual` (`:64`) and **all three importers override it** to hand-parse their payloads
  (`EndpointMappingImportExport.cs:33`, `RoleImportExport.cs`, `PermissionImportExport.cs`) — so a
  hook placed in the base's body would today be skipped by **every** importer, not just the one
  this is about. The base's typed deserialize path (`:64-94` → `ImportAsync(TPayload)`) is
  effectively dead for all current implementations.
- **No tests exist** for `EndpointMappingImportExport` or `EndpointPermissionService`.

## Decision

**Invalidation is part of the import contract, not a courtesy an importer may forget. The
base class owns the public entry point and calls a mandatory invalidation hook on every
import that could have written; each cache's owner exposes the invalidation, so the cache key
never leaves the type that defines it.**

1. **Sealed template method.** `TypedForgeImportExport` seals the public
   `ImportAsync(JsonElement)`; it calls `protected abstract ImportCoreAsync(JsonElement, …)`
   and then `protected abstract InvalidateAffectedCachesAsync(…)`. All three importers move
   their existing override body from `ImportAsync(JsonElement)` to `ImportCoreAsync` — a
   mechanical rename, but it touches every one of them, which is why each gets its own block.
   The base's typed deserialize path is retained for a future importer that wants it, not
   because anything uses it today.

   Both members are **abstract, not virtual**. A new importer cannot silently omit
   invalidation — it does not compile until it answers the question. This is the same discipline
   as ADR-0077's non-nullable `Enabled`: make the omission impossible rather than remembered.

   **The mechanism earned its keep during its own implementation.** `PermissionImportExport` was
   planned as the "nothing cached" case — the explicit no-op that justifies the abstract hook. Forced
   to answer, it answered *wrong*, and the review caught it: permission descriptions are cached, one
   aggregate away, inside the role lists. A `virtual` hook defaulting to no-op would have let that
   sail through silently, which is the entire argument for `abstract` — the compiler cannot check the
   answer, but it can force one to be given where a reviewer will see it.

2. **Invalidate on every import that could have written — NOT only on `Success`.**
   `ImportResult.Success` is `errors.Count == 0`, but rows and errors are not mutually
   exclusive: the per-item `catch` (`EndpointMappingImportExport.cs:289-296`) records an error
   and continues the loop, and the single `SaveChangesAsync` (`:299`) then commits every item
   that did survive. So a result reporting failure can carry committed writes. Gating eviction
   on `Success` would leave the cache stale exactly there. A redundant eviction costs one
   snapshot rebuild; a skipped one is this defect.

   **Correction to an earlier draft of this ADR** (and a caveat on ADR-0081's wording): the
   `Updated: 79` + `Errors: 1` seen during the #254 rollout is **not** an instance of one
   importer writing rows while reporting an error. `RoleManagementService.ImportRolesAsync`
   **aggregates** counts across all three features (`totalCreated/totalUpdated/totalErrors +=`
   per feature, `:439/:453/:467`), so that summary was permissions+roles contributing the 79
   updates while the endpoint-mappings import failed *whole* — its duplicate-key
   `DbUpdateException` hit the outer `catch` (`:312`), which returns
   `new ImportResult(false, Errors: [ex.Message])` with **no counts** and nothing committed.
   The decision above is unchanged, but it rests on the per-item path and on the
   future-importer argument, not on that observation. Do not re-cite the 79 as evidence.

   **A real instance does exist, and it is now pinned by a test**: `RoleImportExport` reaches the
   partial-write path straight from JSON — an item referencing a nonexistent permission commits the
   role while recording an error, so the result reports failure over committed rows. That case is
   the honest evidence for this decision; it was pre-existing and is covered by
   `RoleImportExportTests`.

3. **The cache's owner exposes its invalidation.** `IEndpointPermissionService` gains
   `InvalidateSnapshotAsync`; `IRoleService` gains the equivalent for its list tags. Importers
   call those; the keys stay `private const` in the service that defines them. Verified: no DI
   cycle — both services take only `IDbContextFactory`, `HybridCache`, `ILogger`, and
   `IForgeImportExport` is consumed only by `ImportExportService`.
   Narrowed by ADR-0117: `ImportExportService` is deleted; `IForgeImportExport` is consumed only by the Gateway `RoleManagementService`, which no Forge.Core service depends on, so there is still no DI cycle.

4. **A failed eviction is an import error.** If invalidation throws, the DB write already
   happened, so append to `ImportResult.Errors` rather than swallow or rethrow: the admin UI
   then shows the import as problematic and the operator knows to restart the Gateway.
   Swallowing it would reproduce this very defect — a successful-looking import over dead
   authorization — just more rarely.

## Consequences

- **ADR-0081's mandatory Gateway restart goes away** once this ships: the import evicts its
  own process's snapshot. ADR-0081 §1 must be amended — it currently states the restart "is
  not optional", which will be false. **The verify-not-403 step stays**: it checks the outcome,
  and the import summary alone still does not prove the mappings are live.
- **Scale-out is a named, accepted limitation.** Eviction is process-local and no backplane
  exists, so a second Gateway replica would keep serving its own stale snapshot for up to 30
  minutes. This fix is not worse than today — the three existing evict paths have the identical
  limitation; it merely raises import to their level. The AppHost declares a single Gateway
  instance (`Program.cs:102`, no `WithReplicas`). Whoever introduces scale-out must solve this
  for all four paths at once, not just for import.
- **Core's copy is deliberately not addressed.** Nothing in Core reads the snapshot, so there
  is nothing to go stale. Do not "fix" this by adding cross-process invalidation.
- **`RbacSeedHostedService` (`:43`) also imports**, at Gateway startup — the eviction there is
  a no-op against an empty cache. Harmless, and it keeps the contract uniform.
- **This widens a hotfix.** #210 is folded into !310 (kill-switch) by explicit decision,
  against ADR-0081's own "out of scope for this hotfix" note. The tradeoff accepted: the
  kill-switch's rollout depends on the import path working, and shipping the feature on top of
  a known-broken rollout step is worse than the extra surface. The base class and all three
  importers are touched, so the blast radius is every Forge.Core import — reviewed accordingly.
- **A failed invalidation on the `ImportCoreAsync`-throws path is silent.** The base runs
  invalidation best-effort there and rethrows the original exception unmodified; an invalidation
  failure must not mask the real cause, so it is swallowed rather than reported. Accepted: on that
  path the caller already sees an exception. On the normal path a failed invalidation still lands
  in `Errors` per §4.
- **`IForgeImportExport<TPayload>` is left orphaned but is NOT inert.** It derives from
  `IForgeImportExport`, so an importer implementing it *directly* still satisfies the DI
  registration while bypassing this ADR's contract entirely — and the comment at
  `IForgeImportExport.cs:34` actively recommends that path (naming a `TypedForgeImportExportBase<TPayload>`
  that does not exist). This decision makes that code dead, so deleting it is part of shipping the
  decision, not a follow-up: an escape hatch left open defeats the "cannot be omitted" guarantee.
- **No schema change, no migration.**
