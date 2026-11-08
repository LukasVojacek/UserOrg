# ADR 0016 - Resource-assignment writes flow through the Provider aggregate; AssignedLocation is a keyless child with surrogate identity; HEAppE sync via a Core-owned port

- Date: 2026-06-09
- Status: accepted

## Decision

The legacy `ProjectResourceAssignmentService` write surface (assign / update / remove a resource allocation on a location) migrates into the modular Core as write endpoints with the following shape.

- **Provider is the aggregate root of the transaction.** A resource assignment is the `AssignedLocation` entity, a child of `Location`, which is a child of the `Provider` aggregate. Writes load the `Provider` aggregate (via `IProviderRepository`), mutate through `Location` methods, and commit through `IUnitOfWork` — the same path as the provider/location writes. The allocated **`ProjectResource` is referenced cross-aggregate by identity** (the typed `ProjectResourceId` value object, already mapped on the FK), never embedded. `AllocationUnitType` is a seeded lookup referenced by its int id.

- **The endpoints nest under the location, and the assignment is addressed by its surrogate Guid.** Routes are `Providers/{providerName}/Locations/{locationName}/Assignments` (POST) and `.../assignments/{id:guid}` (PUT/DELETE). `AssignedLocation` has **no natural key** — the schema's `(LocationId, ProjectResourceId)` index is **non-unique** (a resource may hold several allocations on the same shared location, distinguished by aggregation/specs), so no composite natural key exists either. Surrogate identity is therefore the only honest identity, and the read side already exposes that Guid (`resources/{id:guid}`, `ResourceAssignmentResponse.Id`).

- **Authorization reuses the provider-write model (ADR-0011).** `ProviderWriteAuthorization.Authorize` — Admin may write any provider, a provider service-key identity only its own (scoped by the `providerName` claim vs the route). The new routes are added to `roleManagmentRBAC.json` under `perm:provider:update`. Path-scoping under the named provider replaces the legacy "does this location belong to the provider?" body check.

- **The HEAppE synchronization side-effect runs via a domain event and a Core-owned port.** A successful create/update/delete raises a `ResourceAssignmentChanged` domain event carrying the `ProjectResourceId`; `IUnitOfWork` dispatches it **after commit** (the event-handler-by-scan infrastructure, see the core-modules plan). The handler resolves the projects using that resource and enqueues a sync per project through a **Core-side port** `IResourceSyncTrigger`. A thin adapter in the wiring layer bridges that port to the HEAppE module's background `Channel<WorkItem>`. Core depends on **its own port**, not on HEAppE internals or `Heappe.Contracts`.

- **Invariants are enforced hybridly.** The location-local invariant — *a non-shared location may not already have an assigned resource* — is enforced **in the aggregate** (`Location.AssignResource`, over the loaded `AssignedLocations`). The cross-aggregate invariant — *a resource may not already have a non-shared location* — and the read-only-mode guard on the referenced `ProjectResource` are checked in the **use-case via read queries** before the aggregate is mutated. Specifications (`AssignedLocationValue`) use **replace-all** semantics in the aggregate, with no uniqueness enforcement (matching legacy). Exposing the previously-shadow FKs (`ProjectResourceId`, `AllocationUnitTypeId`) and `Mode` is an **EF mapping change over existing columns — no migration**.

The legacy `ProjectResourceAssignment` endpoint is **left untouched**; the modular `v2` endpoints run alongside it, reachable through the Gateway catch-all forwarder. All mutating routes carry `.DisabledWhen("ReadOnlyMode")`.

## Context

- "Resource assignment" is an overloaded term. The legacy `ProjectResourceAssignmentService` operates on **`AssignedLocation`** — the allocation of a `ProjectResource` to a `Location` (with `AllocationUnitType`, count, specifications, aggregation). This is **not** `AssignedProjectResourceToProject` (the resource↔project link). The naming collision is the first thing this ADR disambiguates.
- The modular domain already models `Provider → Location → AssignedLocation` with partial write behaviour (`Location.AddAssignedLocation/Update/Remove`), but without the `ProjectResource`/`AllocationUnitType` binding, specifications writes, or the legacy invariants — so the question was how to complete it, not how to introduce it.
- Legacy gated these writes on `httpContext.TryGetProviderId` (a provider service-key) and skipped the location-ownership check when no key was present (an admin), which is exactly the actor model already formalized for provider/location writes in ADR-0011.
- The HEAppE sync in legacy is **not** a synchronous HEAppE API call — it enqueues a `WorkItem(projectShortName, resourceName)` onto a background channel, after `SaveAsync`, for each project assigned to the resource. The migration must preserve that ordering and fire-and-forget shape.

## Considered options

- **Provider aggregate root, keyless child by surrogate id (chosen).** Completes the existing `Provider → Location → AssignedLocation` model, reuses the shipped repository / auth / error-mapping, and is the only addressing that survives the non-unique composite. Cost: the assignment Guid appears in the URL — accepted because the entity has no natural key and the read side already exposes it.
- **A `ProjectResource` (or `AssignedLocation`) aggregate root with its own repository.** Closest to legacy's independent repositories, but it would move ownership away from the `Location` that already models these children, fragment the Provider aggregate, and duplicate the write plumbing.
- **Address the assignment by the `(location, resource)` composite.** Rejected: the schema's composite index is non-unique, so a resource can have multiple allocations on one shared location — the composite does not identify a row.
- **HEAppE sync via a direct port call in the endpoint, or via a `Heappe.Contracts` port.** The direct call was rejected for putting integration orchestration and post-commit ordering in the endpoint; depending on `Heappe.Contracts` was rejected to keep Core coupled only to its own port (HEAppE stays a self-contained module behind the ports it exposes). The domain-event + Core-owned-port path reproduces legacy ordering and reuses the existing post-commit dispatch.
- **Enforce all invariants in the use-case (thin domain).** Rejected for the location-local invariant, which the aggregate can enforce over already-loaded state; only the genuinely cross-aggregate checks live in the use-case.

## Consequences

- **The natural-key philosophy is preserved where it applies, not everywhere.** Provider and Location keep their name keys; `AssignedLocation`, a keyless join/allocation entity, uses surrogate identity. A future reader seeing a Guid in `.../assignments/{id}` next to name-based sibling routes should read this as deliberate, not an oversight.
- **Cross-aggregate references stay typed.** `ProjectResourceId` (a value object) is the reference, never a raw `Guid` — consistent with the codebase's typed-id rule.
- **HEAppE remains a self-contained module.** Core gains an `IResourceSyncTrigger` port; the binding to the background channel lives in the composition root. Swapping the HEAppE transport touches only the adapter.
- **No schema change.** Surfacing the shadow FKs and `Mode` is EF mapping only; the migrations-overlay freeze is respected.
- **Eventual consistency for sync, as in legacy.** The sync is enqueued post-commit and processed asynchronously; a failure to enqueue does not roll back the committed assignment.
- **Reference pattern.** This is the first modular write that combines an aggregate mutation with a cross-module integration side-effect; the domain-event → Core-owned-port → composition-adapter shape is the template for future ones.

## Amendment (2026-06-10, during S4)

The per-entity `ProjectResource.Mode == ReadOnly` guard is now enforced on **all three** modifying
endpoints (assign / update / remove), a deliberate tightening over legacy, which checked the resource's
mode only on create. Rationale: `ProjectResource.Mode` is the operationally meaningful read-only signal
(`Location.Mode`/`AssignedLocation.Mode` are DB-defaulted on insert and effectively inert), so freezing
a resource must block edits and removals of its allocations too, not just new assignments. Because
`ProjectResource` is a separate aggregate, the check lives in the use-case (endpoint) from
`ResourceAssignmentTarget.Mode`, not in `Provider`. On update/delete it runs **after** the in-memory
mutation and **before** `SaveChangesAsync`, so a read-only resource leaves nothing persisted (and no
domain event is dispatched). A **soft-deleted/missing** resource (`GetResourceForAssignmentAsync` →
`null`) **never blocks** — only an existing, read-only resource does — so a dangling assignment to a
removed resource stays cleanable. The endpoint surface also completed `ProviderErrorMapping`:
`AssignedLocationNotFound → 404` and `AssignedLocationAlreadyDeleted → 409` (these S1 error codes were
first exercised here; previously they would have fallen through to `400`).

## Amendment (2026-06-10, during S2)

Invariant #1 (a resource may not already hold a non-shared location) is now evaluated by
`IResourceAssignmentQuery.GetResourceForAssignmentAsync`, which **deliberately filters to active
allocations** (`!AssignedLocation.IsDeleted`, plus `Location`'s own `!IsDeleted` query filter). Legacy's
check omitted the `IsDeleted` predicate, so a soft-deleted allocation kept the non-shared location
reserved forever — almost certainly a latent bug. The modular query corrects this; a removed allocation
frees the location. The write-validation reads live in a **separate `IResourceAssignmentQuery`** rather
than the caller-scoped `IResourceQuery`, because they are authority-scoped pre-write lookups (Admin /
ProviderAdmin), not the visible-to-the-caller read model.

## Amendment (2026-06-10, during S1)

The "coexistence with the auto-created initial `AssignedLocation`" wrinkle assumed by this ADR turned
out to be void: `Provider.AddLocation` unconditionally created an initial entry whose
`ProjectResourceId`/`AllocationUnitTypeId` are **NOT NULL FK columns**, so every such insert would have
failed with an FK violation against the real database (the location-write verification ran build+unit
tests only, never a live insert). Legacy `InsertLocationAsync` creates no such entry either. The
auto-creation was therefore **removed**: a new location starts with no allocations, and
`Location.AssignResource` — which requires a real `ProjectResourceId` — is the only way an
`AssignedLocation` comes into existence. The `AddLocation` endpoint dropped its three dead
allocation-seeding body fields. The assignment's surrogate `Id` is client-generated (`Guid.CreateVersion7()`)
so the entity is addressable before save; the column default remains as a fallback.
