# ADR 0050 — Multi-location resource provisioning with a HEAppE liveness gate

- Date: 2026-07-01
- Status: Accepted (accepted 2026-09-08 by the repository owner; the code has implemented it in full since before that date)
- Supersedes parts of **ADR-0043**: the single-location `Provision(Provider, LocationName, NewResourceSpec, ResourceAllocationSpec)`
  signature (§2), the location-nested endpoint shape (§5), and the "existing create-only `CreateResourceV2` … left
  unchanged" statement (§5). ADR-0043's aggregate/domain-service modelling decisions (§1–4, §6–7) stand unchanged.
- Relates to: ADR-0016 (resource→location allocation writes through Provider), ADR-0024 (allocation slug), ADR-0073
  (HEAppE liveness probe gates a data call).

## Context

The create-and-allocate v2 use case (ADR-0043) only ever assigned the new `ProjectResource` to **one** `Location`,
addressed by a route segment: `POST providers/{providerName}/locations/{locationName}/resources`. The requirement
changed: callers must be able to create a resource and assign it to **several** Locations in the same call, so
`LocationName` can no longer live on the route — it moves into the request body, one entry per Location, each
carrying its own allocation values (`AllocationUnitTypeId`, `AllocationUnitCount`, `AllocationDescription`,
`AggregationName`, `Specifications`).

Two things had to be settled in addition to the shape change:

1. Whether a partial batch (3 of 5 locations assigned, 2 failed) is allowed, given ADR-0043's "no half-built state"
   guarantee applied only to the single create+allocate pair.
2. HEAppE instances backing a Location can be down. Before this change a dead instance was only discovered later,
   asynchronously, by the command-template sync path. The caller now needs to know **before** anything is created.

There is also no remaining use case for a `ProjectResource` with zero Locations, so the separate create-only
`CreateResourceV2` (`POST Providers/{providerName}/Resources`, no allocation) is retired — the multi-location
endpoint takes over its route.

## Decision

1. **All-or-nothing across the whole batch, not just one pair.** `IResourceProvisioningService.Provision` takes an
   ordered list of `(LocationName, ResourceAllocationSpec)` and iterates it, calling `provider.AssignResource` per
   entry exactly as before. Any failure — an unknown location, a read-only location, the non-shared-location
   invariant — aborts the whole call; nothing is added to the aggregate and the endpoint persists nothing. This
   keeps ADR-0043's "no half-built state" guarantee intact by widening it from one allocation to N.

2. **The non-shared-location invariant is enforced across the batch itself, not just against the DB.** A resource
   may hold at most one non-shared Location (`Location.AssignResource`, existing rule). Because the resource is
   brand-new, a DB precheck (`IResourceAssignmentQuery.HasNonSharedLocationAsync`) always reports false — the only
   way two non-shared Locations in the *same* request can conflict is with each other. `Provision` tracks
   "has this resource already been assigned a non-shared Location in this call" as local state while iterating, and
   fails the second such entry with the existing `ResourceAlreadyHasNonSharedLocation` error. No new invariant, no
   new error code — the existing rule now also fires within one call.

3. **Duplicate-location detection is on `(LocationName, AggregationName)`, not `LocationName` alone.** The same
   Location may legitimately appear twice with different `AggregationName`/allocation values (e.g. two distinct
   aggregations on a shared cluster). Two entries with the same `LocationName` **and** the same `AggregationName`
   are rejected as a 400 before any domain call — this is a malformed request, not a domain conflict. (A duplicate
   targeting a *non-shared* Location is also naturally rejected by rule 2 regardless of `AggregationName`.)

4. **A HEAppE liveness gate runs for every resolved Location before any provisioning.** Each requested Location must
   exist (first missing name → 404, detail names it specifically) before its endpoint can be probed. Once every
   Location resolves, `IHeappeLivenessProbe.IsInstanceAlive(location.Endpoint, ct)` (existing port, ADR-0073) is
   called for **each** one. Unlike the location-existence check (fail on first miss), *all* unreachable instances
   are collected and reported together — the caller needs to know exactly which HEAppE instance(s) are the problem
   without retrying N times against a live external dependency for a typo they didn't make. If any instance is
   unreachable, the whole request fails **502 Bad Gateway** with `extensions.unreachableLocations = [{location,
   endpoint}, …]`, listing every dead one; nothing is created.

5. **`IHeappeLivenessProbe` moves from `Heappe.csproj` to `Heappe.Contracts.csproj`.** Core and the HEAppE
   integration are separate modules communicating through a Contracts (ports) project — the same shape
   `IHeappeConnectionService`/`IHeappeService` already use. The probe interface was defined next to its only
   consumer to date (the legacy `HeappeConnectionService`), which happened to live in the same project; it has to
   be a port to be reachable from `Core`, which references only `Heappe.Contracts`. The implementation
   (`HeappeVersionRemoteResolver`) and its DI registration are unchanged.

6. **The create-only `CreateResourceV2` endpoint is deleted; the multi-location endpoint takes over its route.**
   There is no use case for a `ProjectResource` with zero Locations, so a separate create-without-allocation
   operation no longer makes sense. The multi-location endpoint (class/folder name unchanged —
   `CreateResourceOnLocation`/`CreateResourceOnLocationEndpoint`) is remapped to
   `POST Providers/{providerName}/Resources` and reuses the route name `CreateResourceV2`. The RBAC entry for that
   path/method (`perm:resource:create`) is kept as-is, only its description updated — path, method, and permission
   are unchanged, so no gateway re-seed is needed.

7. **Response returns one allocation result per Location.** `Response(string Name, IReadOnlyList<LocationAllocationResult>
   Allocations)` where `LocationAllocationResult(string LocationName, string AllocationSlug)` — each Location gets
   its own slug from a separate `AllocationSlug.New(name)` call, exactly as the single-location path minted one.

   > **Note 2026-10-01.** `LocationAllocationResult` was deleted: it has been unused since the endpoint moved to
   > the 202 response of [ADR-0057](0057-async-resource-creation-202-accepted-and-status-endpoint.md).

## Considered options

- **Partial success (create the resource, assign what succeeds, report per-location failures) — rejected.** Would
  need a new response vocabulary for a half-assigned resource and abandons ADR-0043's core "no half-built state"
  guarantee for no clear caller benefit; the caller almost always wants every requested Location assigned or none.
- **Reject mixed non-shared Locations at basic input validation (client-side rule) — rejected.** Whether a named
  Location is shared or not is only known after resolving it from the DB; enforcing it earlier would require a
  second, redundant lookup ahead of the one `Provision` already does.
- **List all missing/unknown Location names, mirroring the unreachable-instance list — rejected (over-engineered).**
  `LocationName`s are chosen by the caller from an existing listing endpoint, so a miss is a rare client bug, not an
  operational condition the caller needs a batch report on. A single 404 naming the specific missing Location is
  enough.
- **Reference `Heappe.csproj` directly from `Core` instead of relocating the interface — rejected.** Breaks the
  module boundary this codebase already enforces via the Contracts-project pattern.

## Consequences

- `IResourceProvisioningService.Provision` signature changes (single `LocationName` + `ResourceAllocationSpec` →
  ordered list of pairs); its unit tests need the batch/non-shared-across-batch cases added.
- `CreateResourceEndpoint.cs` and `CreateResourceEndpointTests.cs` are deleted; `EndpointsExtensions.cs` loses its
  `MapCreateResourceEndpoint()` line.
- `IHeappeLivenessProbe` becomes a public port on `Heappe.Contracts`; any existing internal reference in
  `Heappe.csproj` updates its `using`.
- New 502 response shape (`unreachableLocations` list) joins the existing `Problems.Upstream` helper as an
  overload — no change to already-shipped upstream-unavailable responses elsewhere.
- No schema change.

> **Amendment 2026-09-08 ([`0101-workflow-step-payloads-carry-natural-keys-and-request-dtos.md`](0101-workflow-step-payloads-carry-natural-keys-and-request-dtos.md)).**
> The HEAppE liveness gate this ADR introduces is what made the operation long-running, so the flow now runs
> as the `create-project-resource` strategy on the WorkflowEngine rather than inside one HTTP request
> (this reverses §7 of
> [`0043-provider-factory-create-and-allocate-resource.md`](0043-provider-factory-create-and-allocate-resource.md),
> now marked superseded there).
>
> The consequence for `IResourceProvisioningService.Provision`: it is still pure and still takes the
> **resolved** `Provider` aggregate, but that aggregate is now resolved **inside the handler's own unit of
> work** (`InitializeProjectResourceHandler`, via `IProviderRepository.FindAsync` on the `ProviderName` the
> step payload carries), not in the HTTP scope and handed onward. The step payload carries `ProviderName`
> and `ResolvedLocationRef(LocationName, Endpoint)` — never the aggregate, never a `Location`, never a
> `ValueTuple`. A provider that has disappeared by the time the worker runs is a
> `PermanentException("PROVIDER_NOT_FOUND")`.
>
> The ordered list of locations this ADR added is what makes the point concrete: it is the *specification*
> that is ordered and carried, not the resolved `Location` entities. Passing the resolved aggregate remains
> correct **within** one scope (ADR-0029 §4) and is wrong **across** one.
