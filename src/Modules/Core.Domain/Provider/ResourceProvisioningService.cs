using Lukdrasil.Error;
using Lukdrasil.Result;

namespace Core.Domain.Provider;

/// <summary>
/// Single, stateless implementation of <see cref="IResourceProvisioningService"/>. No constructor
/// dependencies — it is a pure domain service (no I/O), so it is registered as a singleton.
/// </summary>
public sealed class ResourceProvisioningService : IResourceProvisioningService
{
    public Result<ProjectResource, Error> Provision(
        Provider provider,
        NewResourceSpec resource,
        UserId createdBy,
        IReadOnlyList<LocationAssignmentSpec> assignments)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(assignments);

        // A resource with no location is not a valid outcome (ADR-0050) — enforced here, not just at the
        // endpoint's input validation, so no other caller of this service can bypass it.
        if (assignments.Count == 0)
        {
            return Result<ProjectResource, Error>.Failure(ProviderErrors.NoLocationAssignments(resource.Name));
        }

        var created = ProjectResource.Create(
            resource.Name,
            provider,
            createdBy,
            resource.StartDate,
            resource.EndDate,
            description: resource.Description,
            primaryInvestigatorEmail: resource.PrimaryInvestigatorEmail);

        var allocation = AllocateAll(provider, created, assignments);
        return allocation.IsSuccess
            ? Result<ProjectResource, Error>.Success(created)
            : Result<ProjectResource, Error>.Failure(allocation.Error);
    }

    /// <summary>
    /// Allocates the resource entity (ADR-0044 Level 2, ADR-0050) to every location in order, all-or-nothing.
    /// Provider.AssignResource enforces invariant #5 (resource.ProviderName == provider.Name) and wires
    /// AssignedLocation via the _resource navigation. Tracks the non-shared-location conflict locally across
    /// the batch (ADR-0050 §2) — the only conflict a batch onto one resource can create within itself. Returns
    /// Success if all allocations succeeded, or the first failing allocation error if any step failed.
    /// </summary>
    /// <param name="provider">The owning provider aggregate (invariant already enforced upstream).</param>
    /// <param name="resource">The newly created resource to allocate.</param>
    /// <param name="assignments">The ordered list of locations and their per-location allocation data.</param>
    /// <returns>Success if all allocations succeeded; Failure with the first error encountered otherwise.</returns>
    private static Result<Error> AllocateAll(
        Provider provider,
        ProjectResource resource,
        IReadOnlyList<LocationAssignmentSpec> assignments)
    {
        var resourceHasNonSharedLocation = false;

        foreach (var assignment in assignments)
        {
            var assign = provider.AssignResource(
                assignment.LocationName,
                resource,
                assignment.Allocation.AllocationUnitTypeId,
                assignment.Allocation.AllocationUnitCount,
                assignment.Allocation.Description,
                assignment.Allocation.AggregationName,
                assignment.Allocation.Specifications,
                resourceHasNonSharedLocation,
                slug: assignment.Allocation.Slug);

            if (!assign.IsSuccess)
            {
                return Result<Error>.Failure(assign.Error);
            }

            var location = provider.Locations.First(l => l.Name.Equals(assignment.LocationName));
            resourceHasNonSharedLocation = resourceHasNonSharedLocation || !location.IsShared;
        }

        return Result<Error>.Success();
    }
}