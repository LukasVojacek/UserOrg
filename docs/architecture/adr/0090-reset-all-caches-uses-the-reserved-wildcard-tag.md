# Reset-all-caches uses the reserved wildcard tag, not per-producer enumeration

Amends [ADR-0081](0081-kill-switch-rbac-mappings-ship-via-manual-import-on-main.md) §1 and
[ADR-0084](0084-cache-invalidation-is-a-mandatory-part-of-the-importer-contract.md), whose
description of the reset evicting named keys no longer holds. Their shared finding — the
endpoint is not a Gateway-cache substitute — stands unchanged.

Both ADRs call the endpoint `POST /Administration/ResetAllCaches`; no such route exists. The
route is **`DELETE /api/Administration/Cache`** (`AdministrationEndpoint.cs:251`), and this ADR is
the correct name of record.

## Context

`ResetAllCachesAsync` (`AdministrationService.cs:154`) claimed to remove "all known cache
entries" but only reached a subset: it listed users and projects, then issued one
`RemoveByTagAsync` per user/project tag, plus two hardcoded key removals. Every entry whose
tag it did not know how to construct was invisible to it — `Heappe_Clusters_{url}` (the tag
needs a HEAppE instance URL nothing in Core enumerates), `RoleList`/`RoleNames`,
`CmdTemplatePerm_Alloc_*`, `UserInfoHydration_{sub}`, ProviderIdResolver entries (written with
`tags: null`, removable only by exact key). The HEAppE cluster gap was the operationally
significant one: clearing the composed `TemplatesInfo` entry alone still left the next request
rebuilding it from the same stale cluster snapshots.

An earlier answer to this problem was "add a global tag to every Core cache producer" — make
every `GetOrCreateAsync` call also carry a well-known tag, so the reset endpoint has one tag
that always works. That requires touching every current producer and remembering to tag every
future one; a forgotten tag reproduces the exact gap this endpoint exists to close.

`HybridCache.RemoveByTagAsync("*")` was investigated instead by decompiling the package as
pinned on `main` at the time of the research (`state/research/hybridcache-wildcard.md` —
`Microsoft.Extensions.Caching.Hybrid 10.7.0`). **This branch pins 10.9.0**
(`Directory.Packages.props:61`); the re-verification the Consequences below require has not been
performed against 10.9.0:

- `DefaultHybridCache.InvalidateTagLocalCore(string tag, ...)` special-cases `tag == "*"`: it
  sets an internal `_globalInvalidateTimestamp` instead of storing the timestamp under that tag
  key.
- `DefaultHybridCache.IsValid(CacheItem cacheItem)` checks `IsWildcardExpired` **first**,
  unconditionally, before it inspects the item's own tags — so the wildcard invalidates every
  cached entry, including entries carrying zero tags.
- `TagSet.Validate` rejects `"*"` as a user-supplied tag (`ArgumentException`), so no producer
  can accidentally write an entry that collides with, or opts out of, the wildcard.

Those three findings prove the **L1** (in-process `CacheItem`) path only. Core also registers an
L2 (`Composition/HostInfrastructureExtensions.cs:31`, `AddDistributedMemoryCache()` — `main` has
the same call at `Program.cs:130`) with no `DisableDistributedCache` flag, so
every entry is written to both tiers, and an L1-only guarantee would let a pre-reset copy return
from L2. The **L2** path was verified separately during review: `HybridCachePayload.TryParse`
calls `IsWildcardExpired` on the creation timestamp it reads out of the payload, so the
deserialization path rejects a pre-reset L2 copy as well. The guarantee therefore holds on both
tiers.

This is proven behaviour of the shipped implementation, not documented contract: no XML-doc
summary described it before this ADR.

## Decision

**`ResetAllCachesAsync` becomes a single `await cache.RemoveByTagAsync("*", ct)` call.**

This supersedes the "global tag on every producer" answer: that answer's intent was full
coverage, and the wildcard delivers exactly that without touching any producer, present or
future. It also removes the `GetUsersAsync`/`GetProjectsAsync` round-trips and the per-user/
per-project loop that motivated them — the wildcard catches entries for a user or project that
no longer exists in the DB, which the enumerated loop structurally could not.

`DELETE /api/Administration/Cache`'s OpenAPI description states plainly that it clears every
**HybridCache** entry in the Core API process — not "every cache": in-process caches outside
HybridCache, notably the JwtBearer `ConfigurationManager`'s OIDC metadata and signing keys
(`AuthenticationExtensions.cs:45,72`), survive the reset. Nor does it claim to reach the
Gateway's separate HybridCache (see ADR-0081 §1 — both hosts use `AddDistributedMemoryCache()`,
so there is no shared L2 to cross).

## Consequences

- **A package upgrade past `10.7.0` must re-verify this behaviour before it ships — on both
  tiers.** The wildcard mechanism is undocumented IL, not a public contract; the changelog of
  `Microsoft.Extensions.Caching.Hybrid` is the thing to check, and a decompile re-check is
  warranted if it is silent on the topic. Re-verify the L1 check (`IsValid` →
  `IsWildcardExpired`) **and** the L2 check (`HybridCachePayload.TryParse` →
  `IsWildcardExpired`); either one going missing leaves stale entries readable.
- **No selective invalidation is possible through this endpoint** — it is all-or-nothing by
  design. A caller wanting to evict one project's or user's data still uses the specific tag
  producers already write.
- **The removed explicit `RemoveAsync(["EndpointPermissionService:Snapshot", "jwks:json"])` is
  redundant, not lost coverage.** The snapshot key is covered by the wildcard; `jwks:json` was
  never reachable from the Core process at all — see the amendment to
  [ADR-0081](0081-kill-switch-rbac-mappings-ship-via-manual-import-on-main.md).
- **Every gap listed in the Context is closed** by construction: the wildcard does not depend
  on Core being able to enumerate a tag, so a future cache producer needs no reset-side change
  to be covered.
