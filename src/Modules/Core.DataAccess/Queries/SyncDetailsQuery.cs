using Core.DataAccess.Queries.Contracts;

using Microsoft.EntityFrameworkCore;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace Core.DataAccess.Queries;

/// <summary>
/// Provider "SyncDetails" aggregation (legacy <c>AdministrationService.GetSyncDetails</c> +
/// <c>UserService.PopulateExtendedUserInfo</c> 252-overload). Like <see cref="UserDetailsQuery"/> and
/// <see cref="DdiProjectsInfoQuery"/> the graph spans more entity sets than a single <see cref="IDataQuery{T}"/>
/// join supports and the relationships are shadow FKs, so it loads in stages and assembles in memory.
///
/// Two faithful groups: the provider's own users (role entries filtered to the provider's projects) and every
/// other user (role entries for all of their projects). The query is a pure DB projection carrying role
/// <b>ids</b> only — the endpoint resolves names/permissions via the Forge.Core role service.
/// </summary>
internal sealed class SyncDetailsQuery(
    IDataQuery<Domain.User.User> users,
    IDataQuery<AssignedUserToProject> assignedUsers,
    IDataQuery<UserPermission> userPermissions,
    IDataQuery<AssignedProjectResourceToProject> assignedResources,
    IDataQuery<AssignedLocation> assignedLocations,
    IDataQuery<AssignedLocationValue> assignedLocationValues) : ISyncDetailsQuery
{
    public async Task<IReadOnlyList<SyncUserView>> GetSyncDetailsAsync(Guid providerId, CancellationToken ct)
    {
        var providerProjectIdSet = await LoadProviderProjectIdsAsync(providerId, ct);
        var providerUserIdHash = await LoadProviderUserIdsAsync(providerProjectIdSet, ct);

        // Stage C — all users (Id + scalars), soft-deleted included (User has a global !IsDeleted filter; it is
        // bypassed here so a deactivated user is reported with IsDeleted = true and the DDI sync can drop it).
        // Group 1 = providerUserIds; Group 2 = the rest.
        var allUsers = await users.ToListAsync(
            query => query.IgnoreQueryFilters().Select(u => new UserRow(
                EF.Property<UserId>(u, "Id"),
                u.IdentityProviderId,
                u.Email,
                u.IsDeleted,
                u.UserName)),
            "SyncDetails.Users",
            ct);

        if (allUsers.Count == 0)
        {
            return [];
        }

        var roleRows = await LoadRoleRowsAsync(allUsers, ct);
        var rolesByUser = GroupRolesByUser(roleRows);

        var roleProjectIds = roleRows.Select(r => r.ProjectId).Distinct().ToList();
        var projectInfoById = await LoadProjectInfoAsync(roleProjectIds, ct);

        return Assemble(allUsers, providerUserIdHash, rolesByUser, projectInfoById, providerProjectIdSet);
    }

    private async Task<HashSet<ProjectId>> LoadProviderProjectIdsAsync(Guid providerId, CancellationToken ct)
    {
        // Stage A — the provider's projects: projects (not deleted) that have any assigned resource whose
        // assigned location's provider id == providerId. Match legacy ProjectRepository.GetProjectsForProvider:
        // the location-provider join (Location.ProviderId), NOT the resource-owner shadow FK; the legacy join
        // does not soft-delete-filter the AssignedLocation / AssignedProjectResource links, only the project.
        var providerResourceIds = await assignedLocations.ToListAsync<Location, ProjectResourceId>(
            (assigned, locs) => assigned
                .Join(
                    locs.Where(loc => EF.Property<ProviderId>(loc, "ProviderId") == ProviderId.From(providerId)),
                    al => EF.Property<Guid>(al, "LocationId"),
                    loc => EF.Property<Guid>(loc, "Id"),
                    (al, loc) => EF.Property<ProjectResourceId>(al, "ProjectResourceId")),
            "SyncDetails.ProviderResourceIds",
            ct);

        providerResourceIds = providerResourceIds.Distinct().ToList();

        var providerProjectIds = providerResourceIds.Count == 0
            ? []
            : await assignedResources.ToListAsync<Domain.Project.Project, ProjectId>(
                (links, projects) => links
                    .Where(apr => providerResourceIds.Contains(EF.Property<ProjectResourceId>(apr, "ProjectResourceId")))
                    .Join(
                        projects.Where(p => !p.IsDeleted),
                        apr => EF.Property<ProjectId>(apr, "ProjectId"),
                        p => EF.Property<ProjectId>(p, "Id"),
                        (apr, p) => EF.Property<ProjectId>(apr, "ProjectId")),
                "SyncDetails.ProviderProjectIds",
                ct);

        return providerProjectIds.ToHashSet();
    }

    private async Task<HashSet<UserId>> LoadProviderUserIdsAsync(HashSet<ProjectId> providerProjectIdSet, CancellationToken ct)
    {
        var providerUserIds = providerProjectIdSet.Count == 0
            ? []
            : await assignedUsers.ToListAsync(
                query => query
                    .Where(aup => providerProjectIdSet.Contains(aup.ProjectId) && !aup.IsDeleted)
                    .Select(aup => aup.UserId),
                "SyncDetails.ProviderUserIds",
                ct);

        return providerUserIds.ToHashSet();
    }

    private Task<List<UserRoleRow>> LoadRoleRowsAsync(List<UserRow> allUsers, CancellationToken ct)
    {
        // Stage D — every user's project roles. UserPermission has a named query filter "SystemRoleOnly"
        // (ProjectId == null); project-scoped roles have ProjectId != null and are filtered out by default, so
        // bypass that one filter to read them. Carry the role id only (no name resolution here).
        //
        // Parity note (legacy gated role emission on live membership/creator via
        // GetProjectsContainsUserExtendedAsync; this emits from the role rows directly). The v2 write model
        // couples membership and the project-role row as one unit (ADR-0008: AssignUserWithRole /
        // UnassignUserWithRole / Delete create and soft-delete them together), so a live project role cannot
        // outlive its membership through any v2 path — the two are always consistent. A divergence could only
        // arise from inconsistent legacy-v1 role/membership state, which is gone at v1 cutoff; the F7 real-SQL
        // parity test covers it.
        var userIds = allUsers.Select(u => u.Id).ToList();
        return userPermissions.ToListAsync(
            query => query
                .IgnoreQueryFilters(["SystemRoleOnly"])
                .Where(up => userIds.Contains(up.UserId) && up.ProjectId != null && !up.IsDeleted)
                .Select(up => new UserRoleRow(up.UserId, up.ProjectId!.Value, up.SystemRoleId.Value)),
            "SyncDetails.UserRoles",
            ct);
    }

    private static Dictionary<UserId, Dictionary<ProjectId, int>> GroupRolesByUser(List<UserRoleRow> roleRows)
    {
        // Per user → (projectId → roleId). TryAdd: first non-deleted role wins per project (legacy used a
        // dictionary keyed by project short name with TryAdd).
        var rolesByUser = new Dictionary<UserId, Dictionary<ProjectId, int>>();
        foreach (var row in roleRows)
        {
            if (!rolesByUser.TryGetValue(row.UserId, out var byProject))
            {
                byProject = [];
                rolesByUser[row.UserId] = byProject;
            }

            byProject.TryAdd(row.ProjectId, row.SystemRoleId);
        }

        return rolesByUser;
    }

    private static List<SyncUserView> Assemble(
        List<UserRow> allUsers,
        HashSet<UserId> providerUserIdHash,
        Dictionary<UserId, Dictionary<ProjectId, int>> rolesByUser,
        Dictionary<ProjectId, ProjectInfo> projectInfoById,
        HashSet<ProjectId> providerProjectIdSet)
    {
        // Assemble. Group 1 first (deduped by IdpId, like legacy DistinctBy), then every other user.
        var result = new List<SyncUserView>();
        var emittedSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var user in allUsers.Where(u => providerUserIdHash.Contains(u.Id)))
        {
            if (!emittedSids.Add(user.IdentityProviderId.Value))
            {
                continue;
            }

            result.Add(BuildUser(user, rolesByUser, projectInfoById, providerProjectIdSet));
        }

        foreach (var user in allUsers.Where(u => !emittedSids.Contains(u.IdentityProviderId.Value)))
        {
            result.Add(BuildUser(user, rolesByUser, projectInfoById, filterProjects: null));
        }

        return result;
    }

    private static SyncUserView BuildUser(
        UserRow user,
        IReadOnlyDictionary<UserId, Dictionary<ProjectId, int>> rolesByUser,
        IReadOnlyDictionary<ProjectId, ProjectInfo> projectInfoById,
        IReadOnlySet<ProjectId>? filterProjects)
    {
        var roles = new List<SyncUserRoleView>();

        if (rolesByUser.TryGetValue(user.Id, out var byProject))
        {
            foreach (var (projectId, roleId) in byProject)
            {
                if (filterProjects is not null && !filterProjects.Contains(projectId))
                {
                    continue;
                }

                if (!projectInfoById.TryGetValue(projectId, out var info))
                {
                    continue;
                }

                roles.Add(new SyncUserRoleView(
                    info.ShortName,
                    user.Email.Value,
                    roleId,
                    info.Resources));
            }
        }

        return new SyncUserView(
            user.Id.Value,
            user.IdentityProviderId.Value,
            user.UserName.Value,
            user.Email.Value,
            user.IsDeleted,
            roles);
    }

    // Loads each project's short name plus its resources/locations/values (legacy ProjectResourceDetailModel /
    // LocationDetailModel shape). Staged like UserDetailsQuery.GetExtendedBySubAsync.
    private async Task<Dictionary<ProjectId, ProjectInfo>> LoadProjectInfoAsync(
        IReadOnlyList<ProjectId> projectIds,
        CancellationToken ct)
    {
        if (projectIds.Count == 0)
        {
            return [];
        }

        var shortNames = await assignedResources.ToListAsync<Domain.Project.Project, ProjectShortNameRow>(
            (_, projects) => projects
                .IgnoreQueryFilters()
                .Where(p => projectIds.Contains(EF.Property<ProjectId>(p, "Id")))
                .Select(p => new ProjectShortNameRow(EF.Property<ProjectId>(p, "Id"), p.ShortName.Value)),
            "SyncDetails.ProjectShortNames",
            ct);

        var shortNameById = shortNames.ToDictionary(p => p.ProjectId, p => p.ShortName);

        // Resources for those projects (exclude deleted links / deleted resources, like UserDetailsQuery).
        var resources = await assignedResources.ToListAsync(
            query => query
                .IgnoreQueryFilters()
                .Where(apr => projectIds.Contains(EF.Property<ProjectId>(apr, "ProjectId"))
                    && !apr.IsDeleted
                    )
                .Select(apr => new ProjectResourceRow(
                    EF.Property<ProjectId>(apr, "ProjectId"),
                    EF.Property<ProjectResourceId>(apr.ProjectResource!, "Id").Value,
                    apr.ProjectResource!.ResourceName.Value,
                    apr.ProjectResource!.Description,
                    apr.ProjectResource!.State,
                    apr.ProjectResource!.StartDate,
                    apr.ProjectResource!.EndDate)),
            "SyncDetails.ProjectResources",
            ct);

        var locationsByResourceId = await LoadLocationsAsync(
            resources.Select(r => r.ResourceId).Distinct().ToList(), ct);

        var resourcesByProject = resources.GroupToLists(
            r => r.ProjectId,
            r => new SyncResourceView(
                r.ResourceName,
                r.Description,
                r.StartDate,
                r.EndDate,
                r.State,
                locationsByResourceId.GetValueOrDefault(r.ResourceId, [])));

        var result = new Dictionary<ProjectId, ProjectInfo>();
        foreach (var projectId in projectIds)
        {
            if (!shortNameById.TryGetValue(projectId, out var shortName))
            {
                continue;
            }

            result[projectId] = new ProjectInfo(
                shortName,
                resourcesByProject.GetValueOrDefault(projectId, []));
        }

        return result;
    }

    private async Task<Dictionary<Guid, IReadOnlyList<SyncLocationView>>> LoadLocationsAsync(
        IReadOnlyList<Guid> resourceIds,
        CancellationToken ct)
    {
        if (resourceIds.Count == 0)
        {
            return [];
        }

        var resourceProjectResourceIds = resourceIds.Select(ProjectResourceId.From).ToList();

        var locationRows = await assignedLocations.ToListAsync<Location, LocationType, LocationRow>(
            (assigned, locs, locationTypes) => assigned
                .Where(al => resourceProjectResourceIds.Contains(EF.Property<ProjectResourceId>(al, "ProjectResourceId")) && !al.IsDeleted)
                .Join(
                    locs,
                    al => EF.Property<Guid>(al, "LocationId"),
                    loc => EF.Property<Guid>(loc, "Id"),
                    (al, loc) => new { al, loc })
                .Join(
                    locationTypes,
                    x => x.loc.LocationType,
                    lt => lt.Id,
                    (x, lt) => new LocationRow(
                        EF.Property<ProjectResourceId>(x.al, "ProjectResourceId").Value,
                        EF.Property<Guid>(x.al, "Id"),
                        x.al.AllocationUnitCount,
                        x.loc.Name.Value,
                        x.loc.Description,
                        x.loc.Endpoint,
                        x.loc.IsPrivate,
                        lt.Name,
                        EF.Property<int>(x.al, "AllocationUnitTypeId"))),
            "SyncDetails.LocationRows",
            ct);

        var assignedLocationIds = locationRows.Select(l => l.AssignedLocationId).ToList();

        var valueRows = await assignedLocationValues.ToListAsync(
            query => query
                .Where(v => assignedLocationIds.Contains(EF.Property<Guid>(v, "AssignedLocationId")))
                .Select(v => new LocationValueRow(
                    EF.Property<Guid>(v, "AssignedLocationId"),
                    v.Name,
                    v.Value ?? string.Empty)),
            "SyncDetails.LocationValues",
            ct);

        var valuesByLocationId = valueRows.GroupToLists(
            v => v.AssignedLocationId,
            v => new SyncLocationValueView(v.Name, v.Value));

        return locationRows.GroupToLists(
            l => l.ResourceId,
            l => new SyncLocationView(
                l.LocationName,
                l.LocationDescription,
                l.Endpoint,
                l.IsPrivate,
                l.LocationTypeName,
                l.AllocationUnitCount,
                (EAllocationUnitType)l.AllocationUnitTypeId,
                valuesByLocationId.GetValueOrDefault(l.AssignedLocationId, [])));
    }

    private sealed record UserRow(UserId Id, IdpId IdentityProviderId, Email Email, bool IsDeleted, UserName UserName);

    private sealed record UserRoleRow(UserId UserId, ProjectId ProjectId, int SystemRoleId);

    private sealed record ProjectShortNameRow(ProjectId ProjectId, string ShortName);

    private sealed record ProjectInfo(string ShortName, IReadOnlyList<SyncResourceView> Resources);

    private sealed record ProjectResourceRow(
        ProjectId ProjectId,
        Guid ResourceId,
        string ResourceName,
        string? Description,
        EProjectResourceState State,
        DateTimeOffset StartDate,
        DateTimeOffset EndDate);

    private sealed record LocationRow(
        Guid ResourceId,
        Guid AssignedLocationId,
        long? AllocationUnitCount,
        string LocationName,
        string? LocationDescription,
        string Endpoint,
        bool IsPrivate,
        string LocationTypeName,
        int AllocationUnitTypeId);

    private sealed record LocationValueRow(Guid AssignedLocationId, string Name, string Value);
}