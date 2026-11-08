using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Queries;

/// <summary>
/// Authority-scoped write-validation reads for the resource-assignment endpoints (see
/// <see cref="IResourceAssignmentQuery"/>). Like <see cref="ResourceDetailsQuery"/> the object graph
/// spans several entity sets joined over shadow FKs, so each question is answered by a focused query
/// rather than one navigation-based load. The resource is addressed by its natural key
/// <c>(ProviderName, ResourceName)</c>; each method resolves the surrogate internally (ADR-0044 L2.6).
/// </summary>
internal sealed class ResourceAssignmentQuery(
    IDataQuery<ProjectResource> resources,
    IDataQuery<AssignedLocation> assignedLocations,
    IDataQuery<AllocationUnitType> allocationUnitTypes,
    IDataQuery<AssignedProjectResourceToProject> resourceToProject) : IResourceAssignmentQuery
{
    public async Task<bool> HasNonSharedLocationAsync(ProjectResourceKey key, CancellationToken ct)
    {
        var id = await ResolveSurrogateAsync(key, ct);
        return id is { } resolved && await HasNonSharedLocationAsync(resolved, ct);
    }

    // Cross-aggregate invariant #1: does an ACTIVE assignment of this resource already sit on a non-shared
    // location? Deliberately filters !AssignedLocation.IsDeleted (legacy omits this — a soft-deleted
    // allocation must not keep reserving the location); Location's own query filter drops soft-deleted ones.
    private async Task<bool> HasNonSharedLocationAsync(ProjectResourceId id, CancellationToken ct) =>
        await assignedLocations.FirstOrDefaultAsync<Location, bool?>(
            (assigned, locations) =>
                // Query syntax: two sets joined over shadow FKs with the !al.IsDeleted + !loc.IsShared guard(s)
                // interleaved between the joins. The compiler's transparent identifiers keep every earlier range
                // variable in scope; the same chain as .Join() needs a hand-written anonymous carrier per step. Same
                // expression tree either way (module-conventions.md § LINQ syntax in read queries).
                from al in assigned
                where EF.Property<ProjectResourceId>(al, "ProjectResourceId") == id && !al.IsDeleted
                join loc in locations on EF.Property<Guid>(al, "LocationId") equals EF.Property<Guid>(loc, "Id")
                where !loc.IsShared
                select (bool?)true,
            "GetResourceForAssignment.NonSharedLocation",
            ct) ?? false;

    public async Task<bool> AllocationUnitTypeExistsAsync(int allocationUnitTypeId, CancellationToken ct)
    {
        var found = await allocationUnitTypes.FirstOrDefaultAsync(
            query => query
                .Where(a => a.Id == allocationUnitTypeId)
                .Select(a => (int?)a.Id),
            "AllocationUnitTypeExists",
            ct);

        return found.HasValue;
    }

    public async Task<IReadOnlyList<ResourceProjectRef>> GetProjectsUsingResourceAsync(ProjectResourceKey key, CancellationToken ct)
    {
        var id = await ResolveSurrogateAsync(key, ct);
        if (id is not { } resolved)
        {
            return [];
        }

        return await resourceToProject.ToListAsync<Domain.Project.Project, ProjectResource, ResourceProjectRef>(
            (assignedProjects, projects, projectResources) =>
                // Query syntax: three sets joined over shadow FKs with the !ap.IsDeleted guard(s) interleaved between
                // the joins. The compiler's transparent identifiers keep every earlier range variable in scope; the
                // same chain as .Join() needs a hand-written anonymous carrier per step. Same expression tree either
                // way (module-conventions.md § LINQ syntax in read queries).
                from ap in assignedProjects
                where EF.Property<ProjectResourceId>(ap, "ProjectResourceId") == resolved && !ap.IsDeleted
                join p in projects on EF.Property<ProjectId>(ap, "ProjectId") equals EF.Property<ProjectId>(p, "Id")
                join pr in projectResources on EF.Property<ProjectResourceId>(ap, "ProjectResourceId") equals EF.Property<ProjectResourceId>(pr, "Id")
                select new ResourceProjectRef(p.ShortName, pr.ResourceName),
            "GetProjectsUsingResource",
            ct);
    }

    public async Task<Guid?> GetResourceSurrogateAsync(ProjectResourceKey key, CancellationToken ct)
    {
        var id = await ResolveSurrogateAsync(key, ct);
        return id?.Value;
    }

    // Resolve the surrogate ProjectResourceId from the natural key (ADR-0044 L2.6). The resource matches only
    // when an active provider named key.Provider OWNS it — an EXISTS (Any) gate over the provider set. A name
    // under another provider, or a NULL-ProviderId legacy row, never matches → null. The !IsDeleted query
    // filter excludes soft-deleted rows; the filtered unique IX_ProjectResource_ProviderId_Name index
    // guarantees at most one active match.
    private async Task<ProjectResourceId?> ResolveSurrogateAsync(ProjectResourceKey key, CancellationToken ct)
    {
        var row = await resources.FirstOrDefaultAsync<Domain.Provider.Provider, ProjectResourceId?>(
            (res, providers) => res
                .Where(pr => pr.ResourceName == key.Name
                    && providers.Any(prov =>
                        prov.Name == key.Provider
                        && EF.Property<ProviderId>(prov, "Id") == EF.Property<ProviderId?>(pr, "ProviderId")))
                .Select(pr => (ProjectResourceId?)EF.Property<ProjectResourceId>(pr, "Id")),
            "ResolveResourceSurrogate",
            ct);

        return row;
    }
}