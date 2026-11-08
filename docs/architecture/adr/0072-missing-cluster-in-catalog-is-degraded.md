# A missing expected cluster in TemplatesInfo is degraded, not an empty result

## Context

TemplatesInfo is served through two HybridCache layers: the composed per-project response (`TemplatesInfo_{project}`, Core) and the HEAppE cluster catalog (adapter). One HEAppE instance hosts many clusters (e.g. Barbora + Karolina) reachable through a single instance URL.

A bug surfaced as: a call with `?noCache=true` returns a Location's nodes correctly, but a following `?noCache=false` returns the same Location with `IsAvailable=false, Nodes:[]` for ~5 minutes. Root cause:

1. The adapter cached the **whole catalog under one per-instance key** (`Heappe_Clusters_{url}`) and accepted it as long as *some* cluster had data. While a HEAppE instance is hydrating after wake-up (~2 min) it can return a partial catalog (Karolina present, Barbora not yet); that partial snapshot was cached per-instance for the full TTL and served to every project/cluster on the instance.
2. **Core** `ProcessUrlGroupAsync` reported "active link's cluster not in the catalog" as a **successful, non-degraded** build, so the empty `Nodes:[]` result was cached at the Core layer too.
3. `noCache=true` bypasses cache reads **and writes**, so a correct live fetch never displaced the poisoned entries — they only cleared on TTL expiry.

It was confirmed there is no legitimate steady state where a project's active link points at a cluster the instance does not list: absence is either transient hydration or invalid configuration (an error state).

## Decision

**Cache HEAppE clusters per cluster, not per instance.** Each cluster is cached under its own key `CacheTags.HeappeCluster(url, name)`, while still tagged with the per-instance `CacheTags.HeappeClusters(url)` tag so existing per-instance invalidations (command-template changes/sync) keep working unchanged via `RemoveByTagAsync`. The adapter receives the requested location names, fetches the whole catalog at most once per call (a shared `Lazy`, with cross-call de-duplication via HybridCache single-flight on each per-cluster key), caches each **present** cluster, and **does not cache** a requested cluster that is absent or has no usable node/project data — a missing cluster is a plain cache miss the next call refetches, never a stored empty snapshot.

A requested cluster the adapter could not return shows up in Core as an unmatched active link, which Core marks **degraded** (so the composed `TemplatesInfo` snapshot is not cached); the existing retry/snapshot machinery self-heals once the cluster reappears, and a total outage still surfaces as 503 after the retry budget. The degraded classification is gated on the cache-backed path (`!noCache`), so a `noCache=true` request stays a pure pass-through.

## Consequences

- A permanently misconfigured link (its cluster genuinely gone/renamed) surfaces as **503 after the retry budget**, not a silent empty `200`. This is intended: the invalid configuration becomes visible instead of being cached away.
- No cross-layer cache eviction: Core no longer reaches into the adapter's cache to `RemoveByTagAsync`. Because a missing cluster is never cached, there is nothing to evict, and a partial catalog can no longer poison co-located clusters/projects on the same instance.
- HybridCache single-flight is preserved per cluster: during a hydration window only callers needing the not-yet-present cluster refetch (and concurrent ones join one factory run), while already-present clusters on the same instance stay cache hits.
- The adapter fetches the whole catalog once per call with a missing cluster (cross-call coalescing is via single-flight, not a shared catalog cache), so a cluster that is permanently absent costs one catalog fetch per call referencing it until the link is fixed — bounded by the same retry budget.
- Self-healing is bounded by `TemplatesInfoRetryPolicy.RetryBudget` (default 1 min). Because the hydration window is ~2 min, a single request may still return an **uncached** empty snapshot; the next request heals once hydration completes. Raising the budget toward ~2–2.5 min would let a single request ride out hydration, at the cost of a longer wait before a genuine outage returns 503 — left as a separate tuning decision.

> **Superseded in part by ADR 0073.** The wait-and-rebuild loop and `TemplatesInfoRetryPolicy` described here were removed: a degraded build now returns immediately (partial → snapshot, total → 503) and never rides out hydration within a single call. The "missing cluster ⇒ degraded ⇒ not cached" classification this ADR established is unchanged; only the *intra-call retry* it relied on for self-healing is gone — healing is now strictly across calls/logins.
