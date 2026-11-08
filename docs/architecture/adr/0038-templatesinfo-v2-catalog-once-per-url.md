# ADR 0038 - TemplatesInfo v2 fetches the HEAppE catalog once per instance URL; resource/template filtering is a use-case projection

- Date: 2026-06-28
- Status: accepted (2026-06-28)
- Relates to: ADR-0018 (read-scope tiers), ADR-0026 (per-provider ProjectResource identity). Constrains the `Modules/Core` GetProjectTemplates endpoint and the `Heappe` connection facade.

## Context

The v2 `GetProjectTemplates` endpoint groups a project's HEAppE links by **(instance URL, resource)** — where
*resource* is the userorg `ResourceName`, which is the HEAppE project's accounting string (see CONTEXT.md
*HEAppE project*). An instance hosting N of a project's resources therefore produces N groups, and each group
independently: runs a liveness probe, calls `GetListAvailableClusters(resourceName)`, re-reads the project's
active-command-template set from the integration DB, and JSON-clones the cluster catalog before pruning it to
that one resource.

Two facts make this redundant rather than necessary:

1. The HEAppE call sends **no resource filter** — `GET /heappe/ClusterInformation/ListAvailableClusters` is
   keyed by URL + caller token only and returns the whole catalog the token can see (all accounting strings,
   clusters, node types, command templates). `resourceName` is used *only* afterwards, locally, to prune.
2. The catalog HTTP is already deduplicated by a per-URL `HybridCache`. So the per-resource repetition wastes a
   probe, a DB read, and a full clone — and the dedup that *does* exist is **implicit**: it holds only while
   the cache is warm, not as a structural property of the request.

Pushing a resource filter down into the integration was considered and rejected: HEAppE cannot filter by
accounting string server-side anyway, and per-resource scoping is what forces the N-fold repetition.

## Decision

The instance catalog is fetched **once per instance URL**, and all project-specific filtering is a pure
projection in the use-case.

1. **Integration exposes the unfiltered catalog.** `IHeappeConnectionService.GetListAvailableClusters(...,
   resourceName, ...)` is replaced by `GetClusterCatalogAsync(projectShortName, heappeUrl, bearerToken, ct)`,
   returning the per-URL-cached catalog. It never resource-scopes, never reads the userorg
   `ProjectCommandTemplatePartition` table, and never clones. The active-command-template set is exposed
   through a separate facade passthrough, `GetActiveCommandTemplatesAsync(projectId, ct)`.
2. **The use-case owns all filtering.** The endpoint groups links by **instance URL only**, probes and fetches
   the catalog once per URL (inside an independent per-URL task — no global barrier, so one dead instance's
   probe timeout delays only its own resources), reads the active-command-template set **once per request**,
   and filters the shared catalog per link by resource/location/date and the allowed-template gate as a
   read-only projection.
3. **Dead surface retired.** The resource-scoped overload, the internal clone+prune, and the never-reached
   `ServiceTokenHeappeConnectionInfo => GetClusters` catalog branch go away.

## Consequences

- For one project across D distinct instance URLs: liveness probe and catalog fetch drop to **once per URL**;
  the allowed-template DB read drops to **once per request**; the catalog clone is **gone** (filtering is
  read-only over the shared cached list, so no mutation to defend against).
- Dedup is now **structural** — it holds even with the per-URL catalog cache cold or disabled. That cache
  remains, but only as the cross-request layer.
- The integration deliberately returns **more data than any single resource needs**. The use-case is the only
  place that knows which accounting strings and command templates a userorg project may surface; the HEAppE
  facade stays a pure fetch+cache boundary that knows nothing of userorg's `ProjectCommandTemplatePartition`.
- HEAppE returns identical data and the external response is unchanged. The reduced call counts (probe and
  catalog once per URL, allowed-templates once per request) are locked by tests so a later edit cannot
  silently re-introduce the per-resource fan-out.
