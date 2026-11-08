# ProjectResource — Developer Guide

How to **create**, **allocate**, and **edit** a project resource in the modular Core.

This feature lives across three projects (`Core` → `Core.DataAccess` → `Core.Domain`). The design is
recorded in [ADR-0043](../../../../../docs/architecture/adr/0043-provider-factory-create-and-allocate-resource.md)
(create-and-allocate via a domain service) and
[ADR-0044](../../../../../docs/architecture/adr/0044-projectresource-natural-key-and-resource-composite-seam.md)
(natural key + surrogate encapsulation). Read those for the *why*; this README is the *how*.

---

## 1. Concepts

| Term | What it is | Identity |
|---|---|---|
| **`ProjectResource`** | Aggregate root. A compute resource owned by **one** provider. | Natural key `ProjectResourceKey(ProviderName, ResourceName)`. Surrogate `ProjectResourceId` (DB `Guid`) is storage-only. |
| **`Location`** | Child entity of the `Provider` aggregate. A provider's compute endpoint (cluster/farm). | `(ProviderName, LocationName)` |
| **`AssignedLocation`** | Child of `Location`. The binding "resource X is allocated to location Y". | GUID-free `Slug` (ADR-0024); no natural key |

Two facts drive everything below:

1. **`ResourceName` is not globally unique** — the same name may exist under different providers
   ([ADR-0026](../../../../../docs/architecture/adr/0026-projectresource-per-provider-identity.md)). The provider qualifies it, so the natural key
   is the **pair** `(ProviderName, ResourceName)` — the value object `ProjectResourceKey`.
2. **The surrogate `ProjectResourceId` never leaves storage** (ADR-0044 Level 2). It is not a domain
   property, not a query-contract field, not an aggregate-method parameter, and not a repository key.
   Aggregates reference a resource by the resolved **entity** (EF navigation) or by `ProjectResourceKey`.

```
Core.Domain/Provider/
  ProjectResource.cs                 aggregate root
  ProjectResourceKey.cs              (ProviderName, ResourceName) natural key
  Provider.cs                        owns Locations + AssignResource/UpdateAssignment/RemoveAssignment
  AssignedLocation.cs                allocation; references the resource via private `_resource` navigation
  ResourceProvisioningService.cs     pure domain service: create + allocate
  IResourceProvisioningService.cs
  NewResourceSpec.cs                 input bundle: the resource's own data
  ResourceAllocationSpec.cs          input bundle: the allocation's data
```

---

## 2. The operations and their endpoints

All routes are under `api/v2`, group `ProviderAdmin`, and `DisabledWhen("ReadOnlyMode")`.
Auth = `ProviderWriteAuthorization` (Admin, or the owning provider's service-key identity — ADR-0011).

| Operation | Verb + route | Success | Endpoint |
|---|---|---|---|
| **Create only** | `POST Providers/{providerName}/Resources` | 201 | `CreateResource/` |
| **Create + allocate (atomic)** | `POST Providers/{providerName}/Locations/{locationName}/Resources` | 201 | `CreateResourceOnLocation/` |
| **Edit metadata** | `PUT Providers/{providerName}/Resources/{resourceName}` | 204 | `UpdateResource/` |
| **Soft-delete** | `DELETE Providers/{providerName}/Resources/{resourceName}` | 204 | `DeleteResource/` |
| **Allocate existing resource** | `POST Providers/{providerName}/Locations/{locationName}/Assignments` | 201 | `../Provider/ResourceAssignment/` |
| **Edit allocation** | `PUT Providers/{providerName}/Locations/{locationName}/Assignments/{slug}` | 204 | `../Provider/ResourceAssignment/` |
| **Remove allocation** | `DELETE Providers/{providerName}/Locations/{locationName}/Assignments/{slug}` | 204 | `../Provider/ResourceAssignment/` |
| **Assign resource → project** | `PUT Projects/{projectShortName}/Providers/{providerName}/Resources/{resourceName}` | 204 | `../Project/AssignProjectResource/` |

> **Create-only semantics.** Create returns **409** if `(providerName, resourceName)` already exists — there is
> no resolve-or-reuse. Allocating an *already-existing* resource to a location is the separate
> `AssignResourceV2` job.

---

## 3. Creating a resource

### 3a. Create **and** allocate in one call (the new flow — ADR-0043)

This is the recommended path when the resource should land on a location immediately. The handler resolves
the provider and owns the single transaction; the pure `IResourceProvisioningService` does the in-memory
create+allocate so no half-built resource (a resource with no allocation) can ever be persisted.

Request body (`CreateResourceOnLocationEndpoint.Request`):

```jsonc
{
  "name": "cluster-a",
  "description": "optional, ≤150 chars",
  "primaryInvestigatorEmail": "pi@example.org",   // required, valid, ≤100 chars
  "startDate": "2026-07-01T00:00:00Z",
  "endDate":   "2027-07-01T00:00:00Z",            // must be > startDate
  "allocationUnitTypeId": 3,                       // must exist
  "allocationUnitCount": 1000,                     // optional
  "allocationDescription": "optional",
  "aggregationName": "optional",
  "specifications": [ { "name": "max_memory", "value": "64GB" } ]
}
```

Response `201`: `{ "name": "cluster-a", "allocationSlug": "<slug>" }`, with a `Location` header to
`GetResourceByNameV2`.

The handler ([`CreateResourceOnLocationEndpoint.cs`](CreateResourceOnLocation/CreateResourceOnLocationEndpoint.cs))
runs every check **before** any persistence, so a failure writes nothing:

```
validate input (400)
  → ProviderWriteAuthorization (403)
  → providerRepository.FindAsync(provider)              → 404 if missing/deactivated
  → resources.IsResourceNameTakenAsync(provider, name)  → 409 (filtered unique index = backstop)
  → assignments.AllocationUnitTypeExistsAsync((int)AllocationUnitType) → 422
  → slug = AllocationSlug.New(name)                     // GUID-free public id (ADR-0024)
  → provisioning.Provision(provider, location, NewResourceSpec, ResourceAllocationSpec)  → domain errors
  → resourceRepository.AddAsync(result.Value)
  → providerRepository.MarkAssignedLocationAsAdded(newAllocation)   // REQUIRED — see below
  → unitOfWork.SaveChangesAsync()                        // ONE transaction: resource + AssignedLocation
```

> **`MarkAssignedLocationAsAdded` is not optional** (ADR
> `docs/architecture/adr/0101-workflow-step-payloads-carry-natural-keys-and-request-dtos.md`). `Provision`
> hangs a new `AssignedLocation` off an already-tracked `Location`, and because `AssignedLocation.Id` is a
> client-generated CLR key, EF's `NavigationFixer` attaches that node as `Modified`, not `Added`. The save
> then emits an `UPDATE` against a row that does not exist and throws `DbUpdateConcurrencyException`. The two
> rows persist together only once the new allocation has been explicitly `Add`ed. `AssignResourceEndpoint.cs`
> and `InitializeProjectResourceHandler.cs` both do this.

The domain service is the whole point of the pattern
([`ResourceProvisioningService.cs`](../../../../Core.Domain/Provider/ResourceProvisioningService.cs)):

```csharp
public Result<ProjectResource, Error> Provision(
    Provider provider, LocationName locationName,
    NewResourceSpec resource, ResourceAllocationSpec allocation)
{
    var created = ProjectResource.Create(
        resource.Name, provider, resource.StartDate, resource.EndDate,
        description: resource.Description,
        primaryInvestigatorEmail: resource.PrimaryInvestigatorEmail);

    // Pass the ENTITY (ADR-0044 L2). provider.AssignResource enforces invariant #5
    // (resource.ProviderName == provider.Name) and wires AssignedLocation via the _resource navigation.
    var assign = provider.AssignResource(
        locationName, created,
        allocation.AllocationUnitTypeId, allocation.AllocationUnitCount,
        allocation.Description, allocation.AggregationName, allocation.Specifications,
        resourceHasNonSharedLocation: false, slug: allocation.Slug);

    return assign.IsSuccess
        ? Result<ProjectResource, Error>.Success(created)
        : Result<ProjectResource, Error>.Failure(assign.Error);
}
```

Why a **domain service** and not `Provider.CreateResource(...)`: the operation spans two aggregates
(`ProjectResource` is a root, `Location` lives in `Provider`), so neither owns it, and an aggregate must not
manufacture **and return** another aggregate root. The service is pure (no I/O, no async) — the application
resolves the `Provider` and owns the transaction. Because `Provision` always does **both** steps, the
create+allocate cohesion is un-bypassable. See ADR-0043 §2.

The atomicity works because `AssignedLocation` links to the resource by **navigation**, not by id: EF fixes up
the FK from the object reference during `SaveChanges`, so the resource's DB-generated id need not be known
before the save. One `SaveChangesAsync` commits the new `ProjectResource` and the mutated provider's new
`AssignedLocation` together — **provided the new `AssignedLocation` was marked `Added` first**, as the code
block above shows. Navigation fix-up settles the FK; it does not settle the entity state of a client-keyed
child hanging off a tracked parent.

### 3b. Create only

`POST Providers/{providerName}/Resources` ([`CreateResourceEndpoint.cs`](CreateResource/CreateResourceEndpoint.cs))
creates a resource with no allocation. Same validation/auth as above minus the allocation parts; it loads the
owning provider, calls `ProjectResource.Create(...)`, `repository.AddAsync`, one `SaveChangesAsync`. Use this
only when you genuinely want a resource without a location (allocate later via `AssignResourceV2`).

---

## 4. Editing a resource

### 4a. Update metadata — `PUT Providers/{providerName}/Resources/{resourceName}`

[`UpdateResourceEndpoint.cs`](UpdateResource/UpdateResourceEndpoint.cs). **Name and owning provider are
identity and immutable** — rename is disabled (ADR-0030). Body: `{ Description?, PrimaryInvestigatorEmail,
StartDate, EndDate }`.

```csharp
var resource = await repository.FindAsync(new ProjectResourceKey(provider, name), ct);
if (resource is null) return Problems.NotFound("resource.not_found", "Resource not found.");

// operationMode / externalId are NOT in the contract — preserve loaded values so a metadata
// update never wipes fields the client didn't send.
resource.UpdateDetails(request.StartDate, request.EndDate, request.Description,
                       resource.OperationMode, email, resource.ExternalId);
await unitOfWork.SaveChangesAsync(ct);
```

Missing / wrong-provider / soft-deleted all resolve to the **same 404** — existence under another provider
never leaks. The surrogate stays inside the repository (`FindAsync` resolves `ProviderName → ProviderId`
internally, ADR-0044 L2.4).

### 4b. Soft-delete — `DELETE Providers/{providerName}/Resources/{resourceName}`

[`DeleteResourceEndpoint.cs`](DeleteResource/DeleteResourceEndpoint.cs). Loads by `ProjectResourceKey`, calls
`resource.Delete()` (sets `IsDeleted`), one `SaveChangesAsync`. Same uniform 404.

### 4c. Edit the **allocation** (not the resource) — `PUT …/assignments/{slug}`

[`UpdateAssignmentEndpoint.cs`](../Provider/ResourceAssignment/UpdateAssignmentEndpoint.cs). The allocation is
addressed by its GUID-free `slug`; the allocated resource and location never change. Specifications are
**replace-all**. The provider is loaded with `FindWithAllocatedResourcesAsync` so the read-only guard can read
`ResourceOperationMode` off the existing `AssignedLocation._resource` navigation:

```csharp
var provider = await repository.FindWithAllocatedResourcesAsync(name, ct);   // resources materialised
var result = provider.UpdateAssignment(location, slug, /* details + specs */);
if (!result.IsSuccess) return result.Error.ToProblem();

// Cross-aggregate read-only guard — checked AFTER mutation, BEFORE commit, so a rejected request persists nothing.
if (result.Value.ResourceOperationMode == EOperationMode.ReadOnly)
    return Problems.Unprocessable(ProviderErrorCodes.ProviderReadOnly, "...read-only...");
await unitOfWork.SaveChangesAsync(ct);
```

---

## 5. Invariants & error map

Domain methods return `Result<T, Error>` for expected failures; they **throw** only for programming errors
(`ArgumentNullException`, `EndDate < StartDate`). Endpoints map each `Error`/check to a status code:

| Check | Where | Status |
|---|---|---|
| Input shape / dates / email | endpoint | 400 |
| Not Admin and not owning provider | endpoint (`ProviderWriteAuthorization`) | 403 |
| Provider / location / resource not found | endpoint (uniform, no existence leak) | 404 |
| Resource name already taken for provider | endpoint (`IsResourceNameTakenAsync`) + filtered unique index | 409 |
| `AllocationUnitType` name not in `EAllocationUnitType` | request binding | 400 `validation.invalid_format` |
| `AllocationUnitType` with no catalog row (`None`) | endpoint (`AllocationUnitTypeExistsAsync`) | 422 |
| **Invariant #5**: `resource.ProviderName != provider.Name` | `Provider.AssignResource` (in-aggregate, ADR-0044 L2.3) | domain error |
| Location is read-only | `Location.AssignResource` | domain error |
| Non-shared location already has an active assignment | `Location.AssignResource` | domain error |
| Resource already has a non-shared location | endpoint computes the flag, aggregate enforces | domain error |
| Allocated resource is read-only | endpoint, post-mutation pre-commit | 422 |

---

## 6. Adding a new resource endpoint

1. **Folder**: `Features/Resource/<Verb><Thing>/<Verb><Thing>Endpoint.cs`.
2. **DTOs**: `internal sealed record Request(...)` / `Response(...)` inside the endpoint class.
3. **Handler**: `internal static async Task<IResult> HandleAsync(...)`. Resolve by natural key —
   `repository.FindAsync(new ProjectResourceKey(provider, name), ct)`. **Never** accept or surface
   `ProjectResourceId`.
4. **Validate → authorize → load → mutate the aggregate → one `unitOfWork.SaveChangesAsync()`.** Every
   `Result` check returns before persistence.
5. **Map** via the C# 14 extension-member pattern (`MapXxxEndpoint` + `MapDocumentation`); add
   `.DisabledWhen("ReadOnlyMode")` and `.WithGroupName(ApiAudience.ProviderAdmin)`.
6. **Register** the `MapXxxEndpoint()` call in
   [`EndpointsExtensions.MapCoreEndpoints`](../../Composition/EndpointsExtensions.cs).
7. **DI**: reuse what exists — `IRepository<ProjectResource, ProjectResourceKey>`, `IProviderRepository`,
   `IResourceQuery`, `IResourceAssignmentQuery`, `IResourceProvisioningService` (singleton), `IUnitOfWork` are
   already registered in
   [`ServiceCollectionExtensions`](../../Composition/ServiceCollectionExtensions.cs). Domain-event handlers are
   auto-wired; no new registration needed for a standard mutation.

---

## 7. Persistence notes (ADR-0044 Level 2)

- **Repository key is the natural key**: `IRepository<ProjectResource, ProjectResourceKey>`. `FindAsync`
  resolves `ProviderName → ProviderId` internally and `.Include("_provider")` so `ProjectResource.ProviderName`
  is whole after load.
- **`ProjectResourceConfiguration`**: surrogate `Id` is a DB-generated shadow PK (`NewId()`); owning provider
  is a shadow FK `ProviderId` mapped to the private `_provider` navigation; filtered unique index
  `IX_ProjectResource_ProviderId_Name` on `[IsDeleted] = 0 AND [ProviderId] IS NOT NULL`; `HasQueryFilter(pr =>
  !pr.IsDeleted)`; `Ignore(pr => pr.Key)` (the key is projected in memory, not stored).
- **`AssignedLocationConfiguration`**: references the resource via shadow FK `ProjectResourceId` mapped to the
  private `_resource` navigation; `ResourceKey` / `ResourceOperationMode` are `Ignore`d projections; `Slug` has a
  filtered unique index. **No schema migration** — the column/FK/indexes are unchanged from ADR-0016.
- **Event**: a successful assign/update/remove raises one `ResourceAssignmentChangedEvent(ProjectResourceKey)`,
  dispatched after `SaveChanges`, driving the HEAppE sync path (ADR-0016) unchanged.
</content>
</invoke>
