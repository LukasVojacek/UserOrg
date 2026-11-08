using Core.DataAccess.Queries.Contracts;

using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using UserOrg.Shared;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace Core.DataAccess.Queries;

internal sealed class ProjectDetailsQuery(
    IDataQuery<Domain.Project.Project> projectQuery,
    IDataQuery<AssignedUserToProject> assignedUserQuery,
    IDataQuery<UserPermission> userPermissionQuery,
    IDataQuery<AssignedProjectResourceToProject> assignedResourceQuery,
    IDataQuery<AssignedLocation> assignedLocationQuery,
    IDataQuery<AssignedLocationValue> assignedLocationValueQuery,
    IDataQuery<ProjectDomainType> domainTypeQuery,
    ILogger<ProjectDetailsQuery> logger) : IProjectQuery
{
    public async Task<ProjectDetails?> GetProjectAsync(ProjectShortName shortName, CancellationToken ct)
    {
        // IgnoreQueryFilters so a soft-deleted project is still reachable by short-name (the response
        // exposes IsDeleted); active-only filtering is applied explicitly to the sub-collections below.
        var projectRow = await projectQuery.FirstOrDefaultAsync(
            query => query
                .IgnoreQueryFilters()
                .Where(p => p.ShortName == shortName)
                .Select(p => new
                {
                    Id = EF.Property<ProjectId>(p, "Id"),
                    p.ShortName,
                    p.Name,
                    p.Description,
                    p.Email,
                    p.ExternalId,
                    p.Mode,
                    p.State,
                    p.ServiceName,
                    p.StartDate,
                    p.EndDate,
                    p.IsDeleted,
                    DomainTypeId = EF.Property<ProjectDomainTypeId>(p, "ProjectDomainTypeId")
                }),
            "GetProject",
            ct);

        if (projectRow is null)
            return null;

        var domainTypeName = await domainTypeQuery.FirstOrDefaultAsync(
            query => query
                .Where(dt => EF.Property<ProjectDomainTypeId>(dt, "Id") == projectRow.DomainTypeId)
                .Select(dt => dt.Name),
            "GetProjectDomainType",
            ct);

        // Project↔ProjectTag is a many-to-many with no join entity class; traverse the "_projectTags"
        // skip navigation, as GetExtendedProjectAsync does.
        var tags = await projectQuery.ToListAsync(
            query => query
                .IgnoreQueryFilters()
                .Where(p => p.ShortName == shortName)
                .SelectMany(p => EF.Property<IEnumerable<Domain.Project.ProjectTag>>(p, "_projectTags"))
                .Where(t => !t.IsDeleted)
                .Select(t => t.Name),
            "GetProjectTags",
            ct);

        var users = await assignedUserQuery.ToListAsync(
            query => query
                .Where(aup => aup.ProjectId == projectRow.Id && !aup.IsDeleted)
                .Select(aup => new ProjectUser(aup.User.UserName, aup.User.Email)),
            "GetProjectMembers",
            ct);

        // Project the owning provider's natural key (ADR-0044 §1) alongside the resource. The provider is
        // reached through the resource's shadow ProviderId FK, left-joined so legacy standalone resources
        // (null ProviderId) still appear, with a null provider name.
        var resources = await assignedResourceQuery.ToListAsync<Domain.Provider.Provider, ResourceDetail>(
            (assignedResources, providers) =>
                // Query syntax for a LEFT join: a legacy standalone resource carries a NULL shadow ProviderId
                // and must still appear in the project's resource list, so the provider is joined with
                // `into provGroup` + `DefaultIfEmpty()`. The method-syntax equivalent is
                // GroupJoin(…).SelectMany(g => g.DefaultIfEmpty(), …) — it emits identical SQL but needs a cast on
                // the inner key, because ProviderId? and ProviderId give generic inference no single TKey (see
                // ResourceDetailsQuery's paged read for the converted shape). Kept as query syntax: it reads
                // better here and converting it buys nothing.
                from apr in assignedResources
                where EF.Property<ProjectId>(apr, "ProjectId") == projectRow.Id && !apr.IsDeleted && !apr.ProjectResource!.IsDeleted
                join prov in providers
                    on EF.Property<ProviderId?>(apr.ProjectResource!, "ProviderId") equals EF.Property<ProviderId>(prov, "Id") into provGroup
                from prov in provGroup.DefaultIfEmpty()
                select new ResourceDetail(
                    apr.ProjectResource!.ResourceName,
                    prov != null ? prov.Name : (ProviderName?)null,
                    apr.ProjectResource.StartDate,
                    apr.ProjectResource.EndDate),
            "GetProjectResources",
            ct);

        return new ProjectDetails(
            projectRow.ShortName,
            projectRow.Name,
            projectRow.Description,
            projectRow.Email,
            projectRow.ExternalId,
            projectRow.Mode,
            projectRow.State,
            projectRow.ServiceName,
            projectRow.StartDate,
            projectRow.EndDate,
            domainTypeName,
            projectRow.IsDeleted,
            tags,
            users,
            resources);
    }

    public Task<List<DomainTypeName>> GetProjectDomainTypesAsync(CancellationToken ct)
        => domainTypeQuery.ToListAsync(
            query => query
                .OrderBy(dt => dt.Name)
                .Select(dt => dt.Name),
            "GetProjectDomainTypes",
            ct);

    public async Task<QueryPage<ProjectSummary>> GetProjectsAsync(ProjectFilter filter, int page, int limit, CancellationToken ct)
    {
        ProviderId? providerId = null;
        if (filter.ProviderName is { } providerName)
        {
            providerId = await ResolveProviderIdAsync(providerName, ct);
            if (providerId is null)
                return new QueryPage<ProjectSummary>([], 0);
        }

        return await projectQuery.ToPagedListAsync<AssignedProjectResourceToProject, ProjectResource, ProjectSummary>(
            (projects, assignedResources, projectResources) =>
                WhereProviderMember(WhereFilter(projects, filter), assignedResources, projectResources, providerId)
                    .OrderBy(p => p.ShortName)
                    .Select(p => new ProjectSummary(
                        p.ShortName,
                        p.Name,
                        p.Email,
                        p.ExternalId,
                        p.Mode,
                        p.State,
                        p.ServiceName,
                        p.StartDate,
                        p.EndDate)),
            page,
            limit,
            "GetProjects",
            ct);
    }

    private Task<ProviderId?> ResolveProviderIdAsync(ProviderName providerName, CancellationToken ct) =>
        // Provider-admin tier: resolve the named provider to its (shadow) id first (LocationDetailsQuery's
        // shape), so the main read joins on the ProjectResource.ProviderId shadow FK alone. The Provider set
        // carries its own "ProviderActive" filter, so a deactivated or unknown provider resolves to no id —
        // that resolution *is* the active-Provider guard, which is why the join below never revisits the
        // Provider set.
        projectQuery.FirstOrDefaultAsync<Domain.Provider.Provider, ProviderId?>(
            (_, providers) =>
                providers
                    .Where(prov => prov.Name == providerName)
                    .Select(prov => (ProviderId?)EF.Property<ProviderId>(prov, "Id")),
            "GetProviderIdByName",
            ct);

    private static IQueryable<Domain.Project.Project> WhereFilter(IQueryable<Domain.Project.Project> projects, ProjectFilter filter) =>
        projects
            .WhereIf(filter.ScopeShortNames is not null, p => filter.ScopeShortNames!.Contains(p.ShortName))
            .WhereIf(!string.IsNullOrWhiteSpace(filter.ShortName?.Value), p => p.ShortName == filter.ShortName)
            .WhereIf(!string.IsNullOrWhiteSpace(filter.ContactEmail?.Value), p => p.Email == filter.ContactEmail)
            .WhereIf(filter.DomainTypeName is not null,
                p => EF.Property<ProjectDomainType>(p, "_projectDomainType").Name == filter.DomainTypeName)
            .WhereIf(filter.StartDate is not null, p => p.StartDate >= filter.StartDate)
            .WhereIf(filter.EndDate is not null, p => p.EndDate <= filter.EndDate);

    private static IQueryable<Domain.Project.Project> WhereProviderMember(
        IQueryable<Domain.Project.Project> query,
        IQueryable<AssignedProjectResourceToProject> assignedResources,
        IQueryable<ProjectResource> projectResources,
        ProviderId? providerId) =>
        providerId is not { } id
            ? query
            // Existential membership: one non-deleted junction row to a non-deleted ProjectResource
            // carrying this provider is enough — never "all", and never gated on the validity period
            // of either side. A null shadow ProviderId (legacy standalone resource) never matches.
            // Explicit .Join(), never a nested correlated .Any(): over a shadow FK spanning this
            // junction the latter silently returns an empty set under the EF InMemory provider.
            // Distinct() has to collapse the one-to-many join here, before OrderBy/Select:
            // ToPagedListAsync counts and pages exactly the IQueryable returned below, so collapsing
            // any later would inflate the reported total and tear the pages.
            : query
                .Join(
                    assignedResources.Where(apr => !apr.IsDeleted),
                    p => EF.Property<ProjectId>(p, "Id"),
                    apr => EF.Property<ProjectId>(apr, "ProjectId"),
                    (p, apr) => new { Project = p, ResourceId = EF.Property<ProjectResourceId>(apr, "ProjectResourceId") })
                .Join(
                    projectResources.Where(pr => EF.Property<ProviderId?>(pr, "ProviderId") == id),
                    link => link.ResourceId,
                    pr => EF.Property<ProjectResourceId>(pr, "Id"),
                    (link, _) => link.Project)
                .Distinct();

    public async Task<QueryPage<ProjectUserSummary>> GetProjectUsersAsync(ProjectShortName shortName, int page, int limit, CancellationToken ct)
    {
        var projectId = await projectQuery.FirstOrDefaultAsync(
            query => query
                .Where(p => p.ShortName == shortName)
                .Select(p => (ProjectId?)EF.Property<ProjectId>(p, "Id")),
            "GetProjectIdByShortName",
            ct);

        if (projectId is not { } pid)
            return new QueryPage<ProjectUserSummary>([], 0);

        var assignedUserIds = await assignedUserQuery.ToListAsync(
            query => query
                .Where(aup => aup.ProjectId == pid && !aup.IsDeleted)
                .Select(aup => aup.UserId),
            "GetProjectAssignedUserIds",
            ct);

        if (assignedUserIds.Count == 0)
            return new QueryPage<ProjectUserSummary>([], 0);

        // Page on UserPermission alone (one active permission row per user/project) so Count + Skip/Take
        // run in SQL; ordering on UserId is the unique tie-breaker. The two-set User join has no paged
        // overload on IDataQuery, so user details for the page's ids are fetched separately and stitched
        // in memory, preserving the SQL page order.
        var permissionPage = await userPermissionQuery.ToPagedListAsync(
            query => query
                .IgnoreQueryFilters()
                .Where(up => assignedUserIds.Contains(up.UserId) && up.ProjectId == (ProjectId?)pid && !up.IsDeleted)
                .OrderBy(up => up.UserId)
                .Select(up => new PermissionRow(up.UserId, up.SystemRoleId)),
            page,
            limit,
            "GetProjectUserPermissions",
            ct);

        if (permissionPage.Items.Count == 0)
            return new QueryPage<ProjectUserSummary>([], permissionPage.TotalItems);

        var pageUserIds = permissionPage.Items.Select(r => r.UserId).ToList();

        // UserPermission has no CLR navigation to User (the relationship is configured one-way from
        // User), so read the User rows via the User set, keyed by the UserId shadow PK.
        var userRows = await userPermissionQuery.ToListAsync<Domain.User.User, UserRow>(
            (_, users) =>
                users.IgnoreQueryFilters()
                    .Where(u => pageUserIds.Contains(EF.Property<UserId>(u, "Id")))
                    .Select(u => new UserRow(
                        EF.Property<UserId>(u, "Id"),
                        u.IdentityProviderId,
                        u.Email,
                        u.UserName)),
            "GetProjectUserDetails",
            ct);

        var usersById = userRows.ToDictionary(u => u.UserId);

        var items = permissionPage.Items
            .Where(r => usersById.ContainsKey(r.UserId))
            .Select(r =>
            {
                var u = usersById[r.UserId];
                return new ProjectUserSummary(u.IdentityProviderId, u.Email, u.UserName, r.RoleId);
            })
            .ToList();

        return new QueryPage<ProjectUserSummary>(items, permissionPage.TotalItems);
    }

    private sealed record PermissionRow(UserId UserId, ProjectRoleId RoleId);

    private sealed record UserRow(UserId UserId, IdpId IdentityProviderId, Email Email, UserName UserName);

    public async Task<ExtendedProjectSummary?> GetExtendedProjectAsync(ProjectShortName shortName, CancellationToken ct)
    {
        var projectRow = await projectQuery.FirstOrDefaultAsync(
            query => query
                .Where(p => p.ShortName == shortName)
                .Select(p => new
                {
                    p.ShortName,
                    p.Name,
                    p.Description,
                    p.Email,
                    p.StartDate,
                    p.EndDate,
                    Id = EF.Property<ProjectId>(p, "Id"),
                    DomainTypeName = EF.Property<ProjectDomainType>(p, "_projectDomainType").Name
                }),
            "GetExtendedProject",
            ct);

        if (projectRow is null)
            return null;

        // 1b. Assigned (non-deleted) tag names — Project↔ProjectTag is a many-to-many with no join
        //     entity class, so traverse the "_projectTags" skip navigation from the project side.
        //     Address tags by their natural key (name), never the surrogate id (ADR-0024).
        var assignedTags = await projectQuery.ToListAsync(
            query => query
                .Where(p => p.ShortName == shortName)
                .SelectMany(p => EF.Property<IEnumerable<Domain.Project.ProjectTag>>(p, "_projectTags"))
                .Where(t => !t.IsDeleted)
                .Select(t => t.Name),
            "GetExtendedProjectTags",
            ct);

        var resources = await assignedResourceQuery.ToListAsync(
            query => query
                .Where(apr => EF.Property<ProjectId>(apr, "ProjectId") == projectRow.Id && !apr.IsDeleted && !apr.ProjectResource!.IsDeleted)
                .Select(apr => new ResourceRow(
                    EF.Property<ProjectResourceId>(apr.ProjectResource!, "Id").Value,
                    apr.ProjectResource!.ResourceName.Value,
                    apr.ProjectResource!.Description)),
            "GetExtendedProjectResources",
            ct);

        if (resources.Count == 0)
            return new ExtendedProjectSummary(
                projectRow.ShortName, projectRow.Name, projectRow.Description,
                projectRow.Email, projectRow.StartDate, projectRow.EndDate,
                projectRow.DomainTypeName, assignedTags, []);

        var resourceIds = resources.Select(r => r.ResourceId).Distinct().ToList();
        var resourceProjectResourceIds = resourceIds.Select(ProjectResourceId.From).ToList();

        // 3. Get location data for those resources. AssignedLocation has no Location/AllocationUnitType
        //    navigations — these relationships are mapped as unnamed FKs — so join the lookup tables
        //    explicitly via their shadow FK columns.
        var locationRows = await assignedLocationQuery.ToListAsync<Location, LocationRow>(
            (assigned, locations) =>
                from al in assigned
                where resourceProjectResourceIds.Contains(EF.Property<ProjectResourceId>(al, "ProjectResourceId")) && !al.IsDeleted
                join loc in locations on EF.Property<Guid>(al, "LocationId") equals EF.Property<Guid>(loc, "Id")
                select new LocationRow(
                    EF.Property<ProjectResourceId>(al, "ProjectResourceId").Value,
                    EF.Property<Guid>(al, "Id"),
                    al.AllocationUnitCount,
                    loc.Name.Value,
                    loc.LocationType,
                    EF.Property<int>(al, "AllocationUnitTypeId"),
                    al.AggregationName),
            "GetExtendedProjectLocationRows",
            ct);

        var assignedLocationIds = locationRows.Select(l => l.AssignedLocationId).ToList();

        var valueRows = await assignedLocationValueQuery.ToListAsync(
            query => query
                .Where(v => assignedLocationIds.Contains(EF.Property<Guid>(v, "AssignedLocationId")))
                .Select(v => new LocationValueRow(
                    EF.Property<Guid>(v, "AssignedLocationId"),
                    v.Name,
                    v.Value ?? string.Empty)),
            "GetExtendedProjectLocationValues",
            ct);

        var valuesByLocationId = valueRows
            .GroupToLists(v => v.AssignedLocationId, v => (v.Name, v.Value));

        var resourceMap = resources.ToDictionary(r => r.ResourceId);

        var assignedResources = locationRows
            .Select(l =>
            {
                var resource = resourceMap[l.ResourceId];
                return new AssignedResourceData(
                    resource.ResourceName,
                    resource.Description,
                    l.LocationName,
                    l.LocationType,
                    l.AllocationUnitCount,
                    (EAllocationUnitType)l.AllocationUnitTypeId,
                    l.AggregationName,
                    valuesByLocationId.GetValueOrDefault(l.AssignedLocationId, []));
            })
            .ToList();

        return new ExtendedProjectSummary(
            projectRow.ShortName, projectRow.Name, projectRow.Description,
            projectRow.Email, projectRow.StartDate, projectRow.EndDate,
            projectRow.DomainTypeName, assignedTags, assignedResources);
    }

    public async Task<List<ProjectHeappeLinkEntry>?> GetProjectHeappeLinksAsync(ProjectShortName shortName, CancellationToken ct)
    {
        var projectRow = await projectQuery.FirstOrDefaultAsync(
            query => query
                .Where(p => p.ShortName == shortName)
                .Select(p => new HeappeLinkProjectRow(
                    EF.Property<ProjectId>(p, "Id"),
                    p.StartDate,
                    p.EndDate)),
            "GetProjectHeappeLinksProject",
            ct);

        if (projectRow is null)
            return null;

        var resources = await LoadHeappeLinkResourcesAsync(projectRow.Id, ct);
        if (resources.Count == 0)
            return [];

        var locationRows = await LoadHeappeLinkLocationsAsync(resources, ct);
        if (locationRows.Count == 0)
            return [];

        var valuesByLocation = await LoadHeappeLinkValuesAsync(locationRows, ct);
        var resourceMap = resources.ToDictionary(r => r.ResourceId);

        // Collapse duplicate (ResourceName, LocationName) links: the same cluster can be assigned
        // more than once (multiple resource/location assignments, or the same location carrying a
        // second — possibly dead — HEAPPE_URL). Without this, callers would query the same cluster
        // repeatedly, doubling node allocations and emitting a stray IsAvailable=false entry.
        return locationRows
            .Select(locationRow => ToHeappeLink(shortName, projectRow, locationRow, valuesByLocation, resourceMap))
            .OfType<ProjectHeappeLinkEntry>()
            .DistinctBy(r => (r.ResourceName, r.LocationName))
            .ToList();
    }

    private Task<List<HeappeLinkResourceRow>> LoadHeappeLinkResourcesAsync(ProjectId projectId, CancellationToken ct) =>
        assignedResourceQuery.ToListAsync(
            query => query
                .Where(apr => EF.Property<ProjectId>(apr, "ProjectId") == projectId && !apr.IsDeleted && !apr.ProjectResource!.IsDeleted)
                .Select(apr => new HeappeLinkResourceRow(
                    EF.Property<ProjectResourceId>(apr.ProjectResource!, "Id").Value,
                    apr.ProjectResource!.ResourceName.Value,
                    apr.ProjectResource!.StartDate,
                    apr.ProjectResource!.EndDate)),
            "GetProjectHeappeLinksResources",
            ct);

    private Task<List<HeappeLinkLocationRow>> LoadHeappeLinkLocationsAsync(List<HeappeLinkResourceRow> resources, CancellationToken ct)
    {
        var resourceProjectResourceIds = resources.Select(r => ProjectResourceId.From(r.ResourceId)).ToList();

        return assignedLocationQuery.ToListAsync<Location, HeappeLinkLocationRow>(
            (query, locations) => query
                .Where(al => resourceProjectResourceIds.Contains(EF.Property<ProjectResourceId>(al, "ProjectResourceId")) && !al.IsDeleted)
                .SelectMany(
                    al => locations.Where(l =>
                        EF.Property<Guid>(l, "Id") == EF.Property<Guid>(al, "LocationId") &&
                        (l.LocationType == ELocationType.HPC ||
                         l.LocationType == ELocationType.Quantum)),
                    (al, l) => new HeappeLinkLocationRow(
                        EF.Property<Guid>(al, "Id"),
                        EF.Property<Guid>(al, "LocationId"),
                        EF.Property<ProjectResourceId>(al, "ProjectResourceId").Value,
                        l.Name.Value)),
            "GetProjectHeappeLinksLocations",
            ct);
    }

    private async Task<Dictionary<Guid, List<HeappeLinkValueRow>>> LoadHeappeLinkValuesAsync(
        List<HeappeLinkLocationRow> locationRows, CancellationToken ct)
    {
        var assignedLocationIds = locationRows.Select(l => l.AssignedLocationId).ToList();
        var valueRows = await assignedLocationValueQuery.ToListAsync(
            query => query
                .Where(v => assignedLocationIds.Contains(EF.Property<Guid>(v, "AssignedLocationId")))
                .Select(v => new HeappeLinkValueRow(
                    EF.Property<Guid>(v, "AssignedLocationId"),
                    v.Name,
                    v.Value)),
            "GetProjectHeappeLinksValues",
            ct);

        return valueRows
            .GroupBy(v => v.AssignedLocationId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    // A link without a valid HEAPPE_IDENTIFIER cannot be routed through the NexusApi proxy; skip it with one warning.
    private ProjectHeappeLinkEntry? ToHeappeLink(
        ProjectShortName shortName,
        HeappeLinkProjectRow projectRow,
        HeappeLinkLocationRow locationRow,
        Dictionary<Guid, List<HeappeLinkValueRow>> valuesByLocation,
        Dictionary<Guid, HeappeLinkResourceRow> resourceMap)
    {
        var values = valuesByLocation.GetValueOrDefault(locationRow.AssignedLocationId, []);
        var resource = resourceMap[locationRow.ResourceId];
        if (!HeappeIdentifier.TryParse(ValueOf(values, "HEAPPE_IDENTIFIER"), out var identifier))
        {
            logger.LogWarning(
                "Project {ProjectShortName}: link '{LocationName}' (Resource:{ResourceName}) has no HEAPPE_IDENTIFIER and is skipped",
                shortName.Value, locationRow.LocationName, resource.ResourceName);
            return null;
        }

        var heappeUrl = ValueOf(values, "HEAPPE_URL");
        return new ProjectHeappeLinkEntry(
            ProjectId: projectRow.Id.Value,
            HeappeApi: Uri.TryCreate(heappeUrl, UriKind.Absolute, out var url) ? url : null,
            HeappeIdentifier: identifier,
            ResourceId: locationRow.ResourceId,
            ResourceName: resource.ResourceName,
            LocationName: locationRow.LocationName,
            LocationId: locationRow.LocationGuid.ToString(),
            StartDate: projectRow.StartDate < resource.StartDate ? projectRow.StartDate : resource.StartDate,
            EndDate: projectRow.EndDate < resource.EndDate ? projectRow.EndDate : resource.EndDate);
    }

    private static string? ValueOf(List<HeappeLinkValueRow> values, string name)
        => values.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    private sealed record ResourceRow(Guid ResourceId, string ResourceName, string? Description);

    private sealed record LocationRow(
        Guid ResourceId,
        Guid AssignedLocationId,
        long? AllocationUnitCount,
        string LocationName,
        ELocationType LocationType,
        int AllocationUnitTypeId,
        string? AggregationName);

    private sealed record LocationValueRow(Guid AssignedLocationId, string Name, string Value);

    private sealed record HeappeLinkProjectRow(ProjectId Id, DateTimeOffset StartDate, DateTimeOffset EndDate);

    private sealed record HeappeLinkResourceRow(Guid ResourceId, string ResourceName, DateTimeOffset StartDate, DateTimeOffset EndDate);

    private sealed record HeappeLinkLocationRow(Guid AssignedLocationId, Guid LocationGuid, Guid ResourceId, string LocationName);

    private sealed record HeappeLinkValueRow(Guid AssignedLocationId, string Name, string Value);
}