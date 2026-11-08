using Core.DataAccess.Queries.Contracts;
using Core.Domain.Provider;

using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace Core.DataAccess.Queries;

/// <summary>
/// DDI "ProjectsInfo" aggregation: every active project-resource grouped by its owning provider, projected
/// with its active associated projects and location assignments (legacy
/// <c>ProjectResourceService.GetAllProjectResourceWithDetailsAsync</c> + the two converters). Like
/// <see cref="ResourceDetailsQuery"/> the graph spans more entity sets than one <see cref="IDataQuery{T}"/>
/// join supports and the relationships are shadow FKs, so it is loaded in stages and assembled in memory.
/// </summary>
internal sealed class DdiProjectsInfoQuery(
    IDataQuery<ProjectResource> resources,
    IDataQuery<Domain.Provider.Provider> providers,
    IDataQuery<AssignedProjectResourceToProject> assignedProjects,
    IDataQuery<AssignedLocation> assignedLocations,
    IDataQuery<AssignedLocationValue> assignedLocationValues,
    IDataQuery<LocationType> locationTypes) : IDdiProjectsInfoQuery
{
    // ProviderId is the resource's owning provider (shadow FK, nullable); ProjectResourceId groups child rows.
    private sealed record ResourceRow(
        Guid Id, ProviderId? ProviderId, string Name, string? Description,
        DateTimeOffset StartDate, DateTimeOffset EndDate);

    private sealed record ProviderRow(Guid Id, string Name, Domain.Shared.Description? Description);

    private sealed record AssociatedProjectRow(
        Guid ProjectResourceId, Guid ProjectId, string ProjectName, string ShortName,
        DateTimeOffset StartDate, DateTimeOffset EndDate);

    private sealed record AssignmentRow(
        Guid Id, Guid ProjectResourceId, ELocationType LocationType, string LocationName, string? LocationDescription,
        long? AllocationAmount, int AllocationUnitTypeId, string? AggregationName);

    private sealed record SpecRow(Guid AssignedLocationId, string Name, string Value);

    public async Task<IReadOnlyList<ProviderResourcesView>> GetProjectsInfoAsync(CancellationToken ct)
    {
        // Legacy used DateTime.Today; the validity column is a DateTimeOffset, so compare against today's
        // date at UTC midnight (EndDate >= today). The ProjectResource !IsDeleted query filter applies.
        var today = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);

        // Stage A — active resource scalar rows, carrying their own owning provider id (shadow FK, nullable).
        var resourceRows = await resources.ToListAsync<ResourceRow>(
            projectResources => projectResources
                .Where(pr => pr.EndDate >= today)
                .Select(pr => new ResourceRow(
                    EF.Property<ProjectResourceId>(pr, "Id").Value,
                    EF.Property<ProviderId?>(pr, "ProviderId"),
                    pr.ResourceName.Value,
                    pr.Description,
                    pr.StartDate,
                    pr.EndDate)),
            "GetProjectsInfo.Resources",
            ct);

        if (resourceRows.Count == 0)
        {
            return [];
        }

        var resourceIds = resourceRows.Select(r => ProjectResourceId.From(r.Id)).ToList();

        // Stage B — the owning providers (name + description) for the distinct non-null provider ids.
        var providerIds = resourceRows
            .Where(r => r.ProviderId is not null)
            .Select(r => r.ProviderId!.Value)
            .Distinct()
            .ToList();

        var providerRows = providerIds.Count == 0
            ? []
            : await providers.ToListAsync<ProviderRow>(
                query => query
                    .Where(p => providerIds.Contains(EF.Property<ProviderId>(p, "Id")))
                    .Select(p => new ProviderRow(
                        EF.Property<ProviderId>(p, "Id").Value,
                        p.Name.Value,
                        p.Description)),
                "GetProjectsInfo.Providers",
                ct);

        var providersById = providerRows.ToDictionary(p => p.Id);

        var associatedByResource = await LoadAssociatedProjectsAsync(resourceIds, ct);
        var assignmentsByResource = await LoadAssignmentsByResourceAsync(resourceIds, ct);

        // Group by the resource's own owning provider id (ADR-0031). A null provider id is the empty-provider
        // group (legacy providerGroup.Key?.ToString() ?? string.Empty). CollectorName uses the group's
        // provider name (spaces → underscores), so it is computed per-group after the provider is resolved.
        return resourceRows
            .GroupBy(r => r.ProviderId)
            .Select(group =>
            {
                var provider = group.Key is { } id ? providersById.GetValueOrDefault(id.Value) : null;
                var providerId = group.Key?.Value.ToString() ?? string.Empty;
                var providerName = provider?.Name;
                var providerDescription = provider?.Description?.Value ?? string.Empty;
                var collectorProviderName = (providerName ?? string.Empty).Replace(' ', '_');

                var resourceViews = group.Select(r => new ProjectResourceInfoView(
                    r.Id,
                    r.Name,
                    r.Description,
                    r.StartDate,
                    r.EndDate,
                    associatedByResource.GetValueOrDefault(r.Id, [])
                        .Select(ap => new AssociatedProjectView(
                            ap.ProjectId,
                            ap.ProjectName,
                            ap.ShortName,
                            $"{ap.ShortName}|{collectorProviderName}|{r.Name}",
                            ap.StartDate,
                            ap.EndDate))
                        .ToList(),
                    assignmentsByResource.GetValueOrDefault(r.Id, []))).ToList();

                return new ProviderResourcesView(providerId, providerName, providerDescription, resourceViews);
            })
            .ToList();
    }

    // Active resource↔project links (exclude IsDeleted) joined to their project.
    private async Task<Dictionary<Guid, IReadOnlyList<AssociatedProjectRow>>> LoadAssociatedProjectsAsync(
        IReadOnlyList<ProjectResourceId> resourceIds,
        CancellationToken ct)
    {
        var rows = await assignedProjects.ToListAsync<Domain.Project.Project, AssociatedProjectRow>(
            (links, projects) => links
                .Where(ap => resourceIds.Contains(EF.Property<ProjectResourceId>(ap, "ProjectResourceId")) && !ap.IsDeleted)
                .Join(
                    projects,
                    ap => EF.Property<ProjectId>(ap, "ProjectId"),
                    p => EF.Property<ProjectId>(p, "Id"),
                    (ap, p) => new AssociatedProjectRow(
                        EF.Property<ProjectResourceId>(ap, "ProjectResourceId").Value,
                        EF.Property<ProjectId>(p, "Id").Value,
                        p.Name.Value,
                        p.ShortName.Value,
                        p.StartDate,
                        p.EndDate)),
            "GetProjectsInfo.AssociatedProjects",
            ct);

        return rows.GroupToLists(r => r.ProjectResourceId, r => r);
    }

    // Active assignments (AssignedLocation has no global soft-delete filter — exclude IsDeleted explicitly)
    // joined to their location, then lookup names (LocationType, AllocationUnitType) and spec values mapped
    // in memory — the join arity (resource set + location) is exhausted, so the lookups load separately.
    private async Task<Dictionary<Guid, IReadOnlyList<ResourceAssignmentInfoView>>> LoadAssignmentsByResourceAsync(
        IReadOnlyList<ProjectResourceId> resourceIds,
        CancellationToken ct)
    {
        var assignmentRows = await assignedLocations.ToListAsync<Location, AssignmentRow>(
            (assigned, locations) => assigned
                .Where(al => resourceIds.Contains(EF.Property<ProjectResourceId>(al, "ProjectResourceId")) && !al.IsDeleted)
                .Join(
                    locations,
                    al => EF.Property<Guid>(al, "LocationId"),
                    loc => EF.Property<Guid>(loc, "Id"),
                    (al, loc) => new AssignmentRow(
                        EF.Property<Guid>(al, "Id"),
                        EF.Property<ProjectResourceId>(al, "ProjectResourceId").Value,
                        loc.LocationType,
                        loc.Name.Value,
                        loc.Description,
                        al.AllocationUnitCount,
                        EF.Property<int>(al, "AllocationUnitTypeId"),
                        al.AggregationName)),
            "GetProjectsInfo.Assignments",
            ct);

        if (assignmentRows.Count == 0)
        {
            return [];
        }

        // Lookup names by id — loaded separately because the join arity is exhausted.
        var locationTypeIds = assignmentRows.Select(a => a.LocationType).Distinct().ToList();
        var locationTypeNames = await locationTypes.ToListAsync(
            query => query
                .Where(lt => locationTypeIds.Contains(lt.Id))
                .Select(lt => new { lt.Id, lt.Name }),
            "GetProjectsInfo.LocationTypes",
            ct);
        var locationTypeNameById = locationTypeNames.ToDictionary(lt => lt.Id, lt => lt.Name);

        // Spec values for those assignments.
        var assignedLocationIds = assignmentRows.Select(a => a.Id).ToList();
        var specRows = await assignedLocationValues.ToListAsync(
            query => query
                .Where(v => assignedLocationIds.Contains(EF.Property<Guid>(v, "AssignedLocationId")))
                .Select(v => new SpecRow(EF.Property<Guid>(v, "AssignedLocationId"), v.Name, v.Value)),
            "GetProjectsInfo.AssignmentValues",
            ct);

        var specsByAssignment = specRows
            .GroupToLists(s => s.AssignedLocationId, s => new SpecificationView(s.Name, s.Value));

        return assignmentRows.GroupToLists(
            a => a.ProjectResourceId,
            a => new ResourceAssignmentInfoView(
                a.LocationName,
                a.LocationDescription,
                locationTypeNameById.GetValueOrDefault(a.LocationType),
                a.AllocationAmount,
                (EAllocationUnitType)a.AllocationUnitTypeId,
                a.AggregationName,
                specsByAssignment.GetValueOrDefault(a.Id, [])));
    }
}