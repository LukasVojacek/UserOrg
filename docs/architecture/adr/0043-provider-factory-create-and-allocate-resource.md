# ADR 0043 — Create-and-allocate ProjectResource via a domain service (single atomic transaction)

- Date: 2026-06-29
- Status: Accepted (accepted 2026-09-08 by the repository owner; the code has implemented it in full since before that date)
- Reaffirms: **ADR-0026 / ADR-0030 / ADR-0031** — `ProjectResource` stays an aggregate root (the demotion-to-Provider-child option is rejected again here). `ProjectResource.Id` stays a **DB-generated EF shadow property** (ADR-0030 §1 unchanged).
- Depends on: **ADR-0044 Level 2** — the allocation is wired by an EF **navigation** to the resolved `ProjectResource` (not by passing a surrogate id), which is why no client-generated id is needed here.
- Relates to: ADR-0016 (resource→location allocation writes through Provider), ADR-0029 (natural keys at boundaries; surrogate stays internal), ADR-0024 (GUID-free allocation slug).

## Context

A new v2 use case: **create a `ProjectResource` under a provider and, in the same call, allocate it to a
named `Location`** — the location addressed by `LocationName` on the input. Today this is two separate
operations: `CreateResourceV2` (`POST Providers/{providerName}/Resources`) creates the resource, and
`AssignResourceV2` (`POST Providers/{providerName}/Locations/{locationName}/Assignments`) allocates an
*existing* resource to a location (ADR-0016).

The domain owner reconsidered whether `ProjectResource` should be its own aggregate root at all, or a child
of the `Provider` aggregate (so resource creation would go "through `Provider`"). Two things had to be
settled:

1. **Modelling.** Keep `ProjectResource` an aggregate root, or fold it into `Provider`?
2. **Mechanics.** The allocation (`AssignedLocation`) references the resolved `ProjectResource` by an EF
   **navigation** (ADR-0044 Level 2 — `private ProjectResource? _resource` + shadow FK). Because the link is an
   object reference, EF fixes up the FK from the navigation during `SaveChanges`, so the resource's
   DB-generated id need not be known before save — an atomic create-and-allocate in a single `UnitOfWork`
   works with the id left as a shadow property.

## Decision

1. **`ProjectResource` stays an aggregate root.** The demotion-to-`Provider`-child option is rejected: F5
   links `Project ↔ ProjectResource` (`AssignedProjectResourceToProject`), so a child model would make
   `Project` (a different aggregate) reference an entity *inside* `Provider` — a cross-aggregate reference to
   a **non-root**, the classic anti-pattern. ADR-0026/0030/0031 stand.

2. **Coordination lives in a Domain Service, not on `Provider`.** Creating a `ProjectResource` (one aggregate)
   and allocating it to a `Location` (a child of the `Provider` aggregate) spans **two aggregates**, so neither
   owns the operation. A `Provider.CreateResource(...)` factory was rejected — an aggregate must not manufacture
   **and return** another aggregate root (that re-imports the "child" thinking we discarded). The operation is
   the textbook use for a **Domain Service**: stateless, named in the ubiquitous language, coordinating creation
   across aggregates.

   `IResourceProvisioningService` (interface + impl in `Core.Domain`) — **pure, no I/O; operates on the
   already-resolved `Provider`**:
   - `Result<ProjectResource, Error> Provision(Provider provider, LocationName locationName, NewResourceSpec resource, ResourceAllocationSpec allocation)`;
   - creates the resource via `ProjectResource.Create(resource.Name, provider, …)` (provider is the FK
     principal, ADR-0031);
   - allocates it via `provider.AssignResource(locationName, resource, …)` — which takes the resolved
     **entity** (ADR-0044 Level 2), creates the `AssignedLocation` via its `_resource` navigation, raises
     `ResourceAssignmentChangedEvent`, and enforces **invariant #5** in-aggregate
     (`resource.ProviderName == provider.Name`);
   - returns `Result<ProjectResource, Error>`.

   The application service resolves the `Provider` (404) and passes the entity; the service therefore returns
   **only domain errors** (location-not-found, invariant, read-only) — infrastructure concerns (404, the
   transaction) stay in the application. Because the service always does **both** steps, no code path produces a
   resource without its allocation (no half-built state). The returned resource is a separate aggregate the
   application persists (resource repo keyed by `ProjectResourceKey`, ADR-0044).

   **Natural keys are honoured where the principle applies** (ADR-0029): the *HTTP contract* and the
   service's cross-aggregate *identifiers* (`LocationName`, `ResourceName`) are natural keys, and the surrogate
   `ProjectResourceId` never appears. The `Provider` parameter is the *resolved aggregate the service operates
   on* — not an identifier — so passing it is "operate on a resolved aggregate" (ADR-0029 §4), not a surrogate
   leak. Keeping the service pure (vs. taking `ProviderName` + resolving via a port) keeps I/O ownership in the
   application, gives a tighter domain-only error contract, lets a batch caller load a provider once and provision
   many resources without re-resolving, and makes the service unit-testable against real aggregates.

3. **`ProjectResource.Id` stays a DB-generated shadow property (no client-generated id).** Because the
   allocation links to the resource by **navigation** (ADR-0044 Level 2), EF derives the FK from the object
   reference on insert — the id is never read on the CLR surface, so it stays a shadow property exactly as
   ADR-0030 §1 had it. (The client-generated-id idea explored earlier is dropped — it was only needed to pass a
   surrogate id into `AssignResource`, which Level 2 replaces with the entity.) ADR-0029 is preserved: the
   surrogate never crosses a contract or an aggregate-method boundary.

4. **The application service owns I/O and one atomic transaction (no inconsistent write).** The endpoint/use-case:
   authorize (`ProviderWriteAuthorization`, name-based) → load `Provider` (`IProviderRepository.FindAsync`) →
   **404** if missing/deactivated → **409** if `(providerName, resourceName)` already exists
   (`IResourceQuery.IsResourceNameTakenAsync`; filtered unique index = concurrency backstop) → **422** if the
   `AllocationUnitTypeId` is unknown (`IResourceAssignmentQuery.AllocationUnitTypeExistsAsync`) →
   `provisioning.Provision(provider, locationName, …)` (pure, in-memory; maps location-not-found → 404,
   invariant/location-local breach → 409/422) → `resourceRepo.AddAsync(result.Value)` → **one**
   `unitOfWork.SaveChangesAsync()` committing the `ProjectResource` **and** the mutated `Provider`'s new
   `AssignedLocation` together (same scoped `DbContext`; EF orders the inserts so the FK is satisfied). Every
   check returns a `Result` **before** any persistence, so a failure writes nothing.

5. **Endpoint (location-nested).** `POST providers/{providerName}/locations/{locationName}/resources`. Body =
   resource fields (`Name`, `Description?`, `PrimaryInvestigatorEmail`, `StartDate`, `EndDate`) **plus** the
   allocation spec (`AllocationUnitTypeId` required, `AllocationUnitCount?`, `AllocationDescription?`,
   `AggregationName?`, `Specifications[]`). Success **201** `CreatedAtRoute("GetResourceByNameV2", …)`. Auth =
   `ProviderWriteAuthorization` (Admin or the owning provider identity), `ProviderAdmin` group,
   `DisabledWhen("ReadOnlyMode")`. The existing create-only `CreateResourceV2` and assign-only
   `AssignResourceV2` endpoints are **left unchanged**.

6. **Create-only semantics — existing resource ⇒ 409.** No resolve-or-reuse. Allocating an *already-existing*
   resource to a location remains `AssignResourceV2`'s job.

7. **No FlowModule.** ~~The operation is a single atomic transaction with no long-running / gated / externally
   retryable step, so the `WorkflowEngine` (which would make it eventually-consistent and be its first
   production consumer) is **not** used here. Reserved for a future provisioning flow.~~

   > **Superseded 2026-09-08 by [`0101-workflow-step-payloads-carry-natural-keys-and-request-dtos.md`](0101-workflow-step-payloads-carry-natural-keys-and-request-dtos.md).**
   > §7 is no longer true of the code. The premise it rests on — "no long-running / gated / externally
   > retryable step" — was invalidated by
   > [`0050-multi-location-resource-provisioning.md`](0050-multi-location-resource-provisioning.md), which
   > added a **durable HEAppE liveness gate**: an external call that can be slow, can fail transiently and
   > must be retryable without re-running the caller's HTTP request. That is precisely the case the
   > WorkflowEngine exists for. The operation therefore runs as the `create-project-resource` strategy on
   > the WorkflowEngine (`src/Modules/Core/Features/Resource/Workflow/CreateStrategy.cs`,
   > [`0056-workflow-engine-platform-integration.md`](0056-workflow-engine-platform-integration.md)) and
   > the endpoint returns `202 Accepted`
   > ([`0057-async-resource-creation-202-accepted-and-status-endpoint.md`](0057-async-resource-creation-202-accepted-and-status-endpoint.md)).
   >
   > What survives from §7 is the **atomicity** requirement, and it survives in a different place: the
   > create-and-allocate is still one `IUnitOfWork.SaveChangesAsync`, but it now happens inside
   > `InitializeProjectResourceHandler`'s own scope rather than inside the HTTP request's. The `Provider`
   > aggregate is **re-resolved per step from its natural key** and never travels in the step payload —
   > see [`0101-workflow-step-payloads-carry-natural-keys-and-request-dtos.md`](0101-workflow-step-payloads-carry-natural-keys-and-request-dtos.md).
   > §1–§4 and §6 of this ADR are unaffected.

## Considered options

- **`Provider.CreateResource(...)` factory method on the aggregate (rejected).** An aggregate that *creates and
  returns another aggregate root* (for the use-case to persist via a different repo) blurs the boundary and is a
  leftover from the rejected "ProjectResource is a Provider child" model. Once `ProjectResource` is a root, the
  cross-aggregate creation belongs in a Domain Service, not on `Provider`.
- **Application-service orchestration only, no domain service (viable; not chosen).** The endpoint sequences
  `ProjectResource.Create` + `provider.AssignResource` inline. Leanest and matches the codebase's "app resolves,
  aggregate methods are sync" style. Rejected as the default because it leaves the create+allocate cohesion
  un-encapsulated — a future caller could `Create` without allocating and persist a dangling resource. The
  domain service makes the two steps inseparable (safety) and names "provisioning" in the ubiquitous language.
- **Name-only service that resolves the `Provider` via `IProviderRepository` (considered; not chosen).** Gives a
  fully name-based service facade, but makes the service async/I/O-bound, splits I/O ownership between the
  service and the application, widens its error contract to include 404, and re-resolves the provider on every
  call (a batch caller pays N lookups). The pure entity form keeps I/O in the application, gives a domain-only
  error contract, and composes for batch reuse — and the natural-key principle is already satisfied at the HTTP
  contract, so passing the resolved `Provider` is not a surrogate leak (ADR-0029 §4).
- **Demote `ProjectResource` to a `Provider` child (rejected).** Breaks the F5 `Project→Resource` reference
  (would target a non-root), reworks F4/F5 surfaces, supersedes three accepted ADRs — large blast radius for no
  domain gain.
- **Client-generated id + pass the surrogate into `AssignResource` (rejected).** Kept the surrogate on the
  domain surface; the navigation/entity approach (ADR-0044 Level 2) is atomic *and* fully encapsulates it.
- **Orchestrate via FlowModule / WorkflowEngine (rejected for now).** See Decision §7 — this rejection was
  reversed on 2026-09-08; the flow does run on the WorkflowEngine.

## Consequences

- **No inconsistent state is writable:** the domain service guarantees create+allocate are inseparable
  (no half-built resource), and the single `UnitOfWork.SaveChangesAsync` makes the two inserts atomic (both or
  neither). All validation returns a `Result` before persistence.
- `IResourceProvisioningService` is **pure** (operates on the resolved `Provider`; cross-aggregate identifiers
  are natural keys `LocationName`/`ResourceName`, no surrogate) → unit-testable against real aggregates, no
  mocks. Reusable by future callers (the composite feature, sync paths) without duplicating the create+allocate
  rule, and a batch caller loads a provider once and provisions many resources.
- Atomic create-and-allocate in one transaction; exactly one `ResourceAssignmentChangedEvent` → existing
  HEAppE sync path (ADR-0016) fires unchanged.
- The surrogate `ProjectResourceId` stays storage-only (shadow); `ProjectResource.Id` is **not** promoted to the
  CLR surface. This ADR therefore leans on **ADR-0044 Level 2** (entity-param `AssignResource`, `_resource`
  navigation, repo by `ProjectResourceKey`). F4/F5 suites must stay green.
- No schema change; the migrations-overlay freeze is respected.
