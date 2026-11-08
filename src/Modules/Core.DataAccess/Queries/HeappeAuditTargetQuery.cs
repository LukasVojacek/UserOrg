using Core.DataAccess.Queries.Contracts;
using Core.Domain.Provider;
using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;

using UserOrg.Shared;

namespace Core.DataAccess.Queries;

/// <summary>
/// Lists the HEAppE resource validity audit targets (ADR-0025) over the modular read side. The object graph
/// spans the Project↔ProjectResource join, the ProjectResource→AssignedLocation→Location chain and the
/// assigned-location values, so — like <see cref="ResourceAssignmentQuery"/> — it is answered by focused
/// shadow-FK queries that are stitched in memory rather than one navigation-based load.
/// </summary>
internal sealed class HeappeAuditTargetQuery(
    IDataQuery<AssignedProjectResourceToProject> resourceToProject,
    IDataQuery<AssignedLocation> assignedLocations,
    IDataQuery<AssignedLocationValue> assignedLocationValues,
    TimeProvider timeProvider) : IHeappeAuditTargetQuery
{
    private static readonly ELocationType[] _heappeLocationTypes = [ELocationType.HPC, ELocationType.Quantum];

    private sealed record ResourceProjectRow(Guid ProjectResourceId, string ResourceName, string ProjectShortName, DateTimeOffset StartDate, DateTimeOffset EndDate);
    private sealed record AssignedLocationRow(Guid AssignedLocationId, Guid ProjectResourceId);
    private sealed record ValueRow(Guid AssignedLocationId, string Name, string Value);

    public async Task<List<HeappeAuditTargetRow>> GetAuditTargetsAsync(CancellationToken ct)
    {
        var resourceProjects = await LoadResourceProjectsAsync(timeProvider.GetUtcNow(), ct);
        if (resourceProjects.Count == 0)
        {
            return [];
        }

        var resourceIds = resourceProjects.Select(rp => ProjectResourceId.From(rp.ProjectResourceId)).Distinct().ToList();
        var assignedRows = await LoadAssignedLocationsAsync(resourceIds, ct);
        if (assignedRows.Count == 0)
        {
            return [];
        }

        var valuesByAssignedLocation = await LoadValuesByAssignedLocationAsync(assignedRows, ct);
        var resourceProjectsByResource = resourceProjects
            .GroupBy(rp => rp.ProjectResourceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return [.. assignedRows.SelectMany(assigned => TargetsFor(assigned, valuesByAssignedLocation, resourceProjectsByResource))];
    }

    private static IEnumerable<HeappeAuditTargetRow> TargetsFor(
        AssignedLocationRow assigned,
        Dictionary<Guid, List<ValueRow>> valuesByAssignedLocation,
        Dictionary<Guid, List<ResourceProjectRow>> resourceProjectsByResource)
    {
        // The identifier routes the audit call; the URL is optional and only labels the findings.
        var values = valuesByAssignedLocation.GetValueOrDefault(assigned.AssignedLocationId, []);
        var heappeUrl = values.FirstOrDefault(v => v.Name == ApplicationConstants.HeappeUriKey)?.Value;
        var heappeIdentifier = values.FirstOrDefault(v => v.Name == ApplicationConstants.HeappeIdentifierKey)?.Value;

        return string.IsNullOrWhiteSpace(heappeIdentifier)
            ? []
            : resourceProjectsByResource.GetValueOrDefault(assigned.ProjectResourceId, []).Select(owner => new HeappeAuditTargetRow(
                owner.ResourceName,
                owner.ProjectShortName,
                heappeUrl,
                heappeIdentifier,
                owner.StartDate,
                owner.EndDate));
    }

    private Task<List<ResourceProjectRow>> LoadResourceProjectsAsync(DateTimeOffset now, CancellationToken ct) =>
        // (resource, owning project) pairs for currently-valid resources. The ProjectResource query filter
        // drops soft-deleted resources; the link's own IsDeleted is filtered explicitly (no global filter on
        // the join). A resource linked to N projects yields N rows — emit one target per (project, instance).
        resourceToProject.ToListAsync<Domain.Project.Project, ProjectResource, ResourceProjectRow>(
            (links, projects, resources) =>
                // Query syntax: three sets joined over shadow FKs with the !link.IsDeleted + the validity-window
                // guard(s) interleaved between the joins. The compiler's transparent identifiers keep every earlier
                // range variable in scope; the same chain as .Join() needs a hand-written anonymous carrier per step.
                // Same expression tree either way (module-conventions.md § LINQ syntax in read queries).
                from link in links
                where !link.IsDeleted
                join project in projects on EF.Property<ProjectId>(link, "ProjectId") equals EF.Property<ProjectId>(project, "Id")
                join resource in resources on EF.Property<ProjectResourceId>(link, "ProjectResourceId") equals EF.Property<ProjectResourceId>(resource, "Id")
                where resource.StartDate <= now && now <= resource.EndDate
                select new ResourceProjectRow(
                    EF.Property<ProjectResourceId>(resource, "Id").Value,
                    resource.ResourceName.Value,
                    project.ShortName.Value,
                    resource.StartDate,
                    resource.EndDate),
            "GetHeappeAuditTargets.ResourceProjects",
            ct);

    private Task<List<AssignedLocationRow>> LoadAssignedLocationsAsync(List<ProjectResourceId> resourceIds, CancellationToken ct) =>
        // Active assignments at an HPC or Quantum location. AssignedLocation has no query filter, so !IsDeleted
        // is explicit; Location's own filter drops soft-deleted locations. Location.LocationType is the FK to
        // LocationType (HPC=1, Quantum=7) — filtering it directly avoids a LocationType join.
        assignedLocations.ToListAsync<Location, AssignedLocationRow>(
            (assigned, locations) =>
                // Query syntax: two sets joined over shadow FKs with the !al.IsDeleted + location-type guard(s)
                // interleaved between the joins. The compiler's transparent identifiers keep every earlier range
                // variable in scope; the same chain as .Join() needs a hand-written anonymous carrier per step. Same
                // expression tree either way (module-conventions.md § LINQ syntax in read queries).
                from al in assigned
                where !al.IsDeleted && resourceIds.Contains(EF.Property<ProjectResourceId>(al, "ProjectResourceId"))
                join loc in locations on EF.Property<Guid>(al, "LocationId") equals EF.Property<Guid>(loc, "Id")
                where _heappeLocationTypes.Contains(loc.LocationType)
                select new AssignedLocationRow(al.Id, EF.Property<ProjectResourceId>(al, "ProjectResourceId").Value),
            "GetHeappeAuditTargets.AssignedLocations",
            ct);

    private async Task<Dictionary<Guid, List<ValueRow>>> LoadValuesByAssignedLocationAsync(
        List<AssignedLocationRow> assignedRows, CancellationToken ct)
    {
        // The HEAppE URL/identifier values for those assignments (Values is an ignored read-only projection,
        // so the child rows are joined over their AssignedLocationId shadow FK).
        var assignedLocationIds = assignedRows.Select(a => a.AssignedLocationId).ToList();
        var valueRows = await assignedLocationValues.ToListAsync(
            values => values
                .Where(v => assignedLocationIds.Contains(EF.Property<Guid>(v, "AssignedLocationId"))
                    && (v.Name == ApplicationConstants.HeappeUriKey || v.Name == ApplicationConstants.HeappeIdentifierKey))
                .Select(v => new ValueRow(EF.Property<Guid>(v, "AssignedLocationId"), v.Name, v.Value)),
            "GetHeappeAuditTargets.Values",
            ct);

        return valueRows
            .GroupBy(v => v.AssignedLocationId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }
}