# TemplatesInfo v2 — fetch HEAppE catalog once per instance URL (implementation plan)

**Status:** ready for implementation
**Decision record:** [ADR-0038](../../../architecture/adr/0038-templatesinfo-v2-catalog-once-per-url.md)
**Glossary:** see *HEAppE project* in [CONTEXT.md](../../../../CONTEXT.md)
**Scope:** `Modules/Core` GetProjectTemplates endpoint + `Heappe` connection facade. No change to v1/legacy, no service-account switch, no external contract change.

---

## Goal

Optimise the number of requests the v2 `GetProjectTemplates` endpoint makes to HEAppE (and to the integration
DB) when a project has many resources spread across few HEAppE instances, **without changing the response**.

Target for the reference scenario (1 project, 10 resources, 7 distinct instance URLs, 3 URLs shared by 2
resources): **7 liveness probes, 7 catalog fetches, 1 allowed-template DB read, 0 catalog clones** — and the
deduplication must be **structural** (hold even with the per-URL catalog cache cold/disabled), not a side
effect of a warm cache.

---

## How it works today (and why it's wasteful)

The endpoint groups links by **(instance URL, resource)** and runs each group independently in
`ProcessUrlGroupAsync`:

| Per `(URL, resource)` group | For 10 resources / 7 URLs | Where it lives |
|---|---|---|
| `IsHostAliveAsync(url)` | **10** probes (URLs with 2 resources probed twice) | endpoint |
| `GetListAvailableClusters(resourceName)` | 10 calls; catalog HTTP deduped to **7** by per-URL `HybridCache` | integration |
| `GetActiveCommandTemplatesAsync(projectId)` (DB) | **10** identical reads (projectId is constant) | integration (inside the call above) |
| `CloneClusters` + `PruneDisallowedCommandTemplates` | **10** full JSON clones + prunes | integration |

Two facts that unlock the fix:

1. **The HEAppE call sends no resource filter.** `GET /heappe/ClusterInformation/ListAvailableClusters` is keyed
   by URL + caller token only and returns the whole catalog the token can see (all accounting strings /
   HEAppE projects). `resourceName` is used *only* locally, afterwards, by `PruneDisallowedCommandTemplates`.
2. **The catalog is already cached per URL** (`CacheTags.HeappeClusters(heappeUrl)`), and the prune runs on a
   *clone* of that shared cache entry. So the per-resource repetition wastes a probe, a DB read, and a clone —
   the catalog itself is shared.

Net: the only redundant HEAppE HTTP is the probe (10 → 7); the DB read (10 → 1) and clone (10 → 0) are pure
local waste; and today's catalog dedup is implicit (cache-dependent).

---

## Decisions (from the design review)

- **Group by instance URL only.** "Project" in the request = HEAppE project = accounting string = userorg
  `ResourceName`; a userorg project's several resources on one instance share one catalog.
- **Keep the caller token** (`ListAvailableClusters`). The service-account `Management/Clusters` branch is dead
  code in this path and stays out of scope.
- **Integration returns the unfiltered catalog; the use-case owns all filtering.** The HEAppE facade stays a
  pure fetch+cache boundary that knows nothing of userorg's `ProjectCommandTemplatePartition`.
- **Allowed-template set reaches the endpoint via a facade passthrough** (single integration dependency), read
  **once per request**.
- **Lock the call counts with tests** so the per-resource fan-out cannot silently return.

---

## Phase 1 — Integration contract (`Heappe.Contracts` + `Heappe`)

**`IHeappeConnectionService`** — [src/Modules/Integrations/Heappe.Contracts/IHeappeConnectionService.cs](../../../../src/Modules/Integrations/Heappe.Contracts/IHeappeConnectionService.cs)

- Replace
  `GetListAvailableClusters(string projectShortName, Guid projectId, string resourceName, Uri heappeUrl, string bearerToken, CancellationToken ct)`
  with
  `GetClusterCatalogAsync(string projectShortName, Uri heappeUrl, string bearerToken, CancellationToken ct)`
  returning the **unpruned** `Result<IList<ClusterInfoModel>, Error>`.
- Add `Task<HashSet<CommandTemplatePartitionResponse>> GetActiveCommandTemplatesAsync(Guid projectId, CancellationToken ct)`.
- `IsHostAliveAsync` unchanged.

**`HeappeConnectionService`** — [src/Modules/Integrations/Heappe/Features/Connection/HeappeConnectionService.cs](../../../../src/Modules/Integrations/Heappe/Features/Connection/HeappeConnectionService.cs)

- `GetClusterCatalogAsync` = today's private `GetCachedAvailableClustersAsync`, made public. **Per-URL
  `HybridCache` stays unchanged** (it is now the cross-request dedup layer only).
- **Delete** `CloneClusters`, `PruneDisallowedCommandTemplates`, `RemoveDisallowedProjectsFromNodes`,
  `RemoveDisallowedTemplatesFromProject`, and the internal `GetActiveCommandTemplatesAsync` call.
- `GetActiveCommandTemplatesAsync` = one-line delegate to the injected `IHeappeService`.
- *(Optional cleanup — pre-existing dead code, default leave)* the never-reached
  `ServiceTokenHeappeConnectionInfo => GetClusters` catalog branch.

Single caller of the old method is the v2 endpoint, so this is a clean replace (the `ProjectService.cs` /
`Userorg.Adapter` callers are the legacy monolith's separate interface — untouched).

## Phase 2 — Endpoint restructure

[src/Modules/Core/Features/Project/GetProjectTemplates/GetProjectTemplatesEndpoint.cs](../../../../src/Modules/Core/Features/Project/GetProjectTemplates/GetProjectTemplatesEndpoint.cs)

`HandleAsync`:

```csharp
var allowed = await heappeConnectionService.GetActiveCommandTemplatesAsync(projectId, ct); // 1×/request

var groupResults = await Task.WhenAll(
    activeLinks
        .GroupBy(l => l.HeappeApi.ToNormalizeUrl().AbsoluteUri)        // URL only
        .Select(g => ProcessUrlAsync(
            heappeConnectionService, logger, shortName, new Uri(g.Key),
            bearerToken, g.ToList(), allowed, currentDate, ct)));
```

`ProcessUrlAsync(url, links, allowed, …)` — **independent per-URL task, no global barrier** (one dead
instance's probe timeout delays only its own resources):

```
if (!await conn.IsHostAliveAsync(url, ct))        → links ⇒ IsAvailable=false, Failed=true
catalog = await conn.GetClusterCatalogAsync(shortName, url, token, ct)
if null / !IsSuccess / TaskCanceled                → links ⇒ IsAvailable=false, Failed=true
foreach link in links: BuildTemplateResponses(link, catalog, allowed, currentDate)
```

- `ProcessUrlGroupAsync` (per resource) is replaced; the probe moves here (once per URL).
- `Failed` → `ResponseCacheControl.SuppressStorage` semantics unchanged.
- A link whose accounting string is absent from a **successfully fetched** catalog stays `IsAvailable=false`
  **without** `Failed` (do not suppress the cache for a normal "not provisioned here").

## Phase 3 — Fold the allowed-template gate into the projection

`BuildTemplateResponses` takes `IReadOnlySet<CommandTemplatePartitionResponse> allowed`; in the
command-template selection add:

```csharp
.Where(c => allowed.Contains(new CommandTemplatePartitionResponse(c.Name, c.Id)))
```

This merges the gate (today in the integration's `Prune`) into the single existing per-link pass. Filtering is
**read-only** over the shared cached catalog, so no clone is needed.

---

## Tests (acceptance)

- **Migrate** [Tests/Modules/Integrations/Heappe.Adapter.Tests/GetListAvailableClustersPruningTests.cs](../../../../Tests/Modules/Integrations/Heappe.Adapter.Tests/GetListAvailableClustersPruningTests.cs)
  → assertions on the allowed-template gate in `BuildTemplateResponses` (the integration prune is gone).
- **Extend** [Tests/Modules/Core/Core.UnitTests/ProjectTemplates/GetProjectTemplatesEndpointTests.cs](../../../../Tests/Modules/Core/Core.UnitTests/ProjectTemplates/GetProjectTemplatesEndpointTests.cs):
  - **Behavior-preservation** fixtures: 2 resources on 1 URL, dead URL, resource absent from catalog, empty
    allowed-set — response identical to current.
  - **Count-assertions** via a mocked `IHeappeConnectionService`: `IsHostAliveAsync` and `GetClusterCatalogAsync`
    each called exactly once per **distinct URL**; `GetActiveCommandTemplatesAsync` called **once** per request.

> Note: tests live under `Tests/` (capital). Add new files there and verify with `git ls-files` — see the
> mixed-case path caveat in the repo.

## Verification

```bash
dotnet build
dotnet test Tests/Modules/Core/Core.UnitTests/Core.UnitTests.csproj
dotnet test Tests/Modules/Integrations/Heappe.Adapter.Tests/Heappe.Adapter.Tests.csproj
```

Done when: build + analyzers clean, all tests green, count-assertions prove probe & catalog once per URL and
allowed-templates once per request, and the behavior-preservation fixtures show an unchanged response.
