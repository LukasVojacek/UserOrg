using Core.DataAccess.Queries.Contracts;

using Microsoft.EntityFrameworkCore;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace Core.DataAccess.Queries;

internal sealed class UserDetailsQuery(
    IDataQuery<Domain.User.User> userQuery,
    IDataQuery<AssignedUserToProject> assignedUserQuery,
    IDataQuery<AssignedProjectResourceToProject> assignedResourceQuery,
    IDataQuery<AssignedLocation> assignedLocationQuery,
    IDataQuery<AssignedLocationValue> assignedLocationValueQuery,
    IDataQuery<Domain.Provider.Provider> providerQuery) : IUserInfoQuery
{
    public Task<UserDetails?> GetBySubAsync(IdpId identityProviderId, CancellationToken ct)
        => userQuery
            .FirstOrDefaultAsync(query => query
            .Where(u => u.IdentityProviderId == identityProviderId)
            .Select(u => new UserDetails(
                u.IdentityProviderId,
                Email.Create(u.Email.Value),
                UserName.Create(u.UserName.Value))),
        "GetBySubAsync",
            ct);

    public async Task<bool> IsDeactivatedAsync(IdpId identityProviderId, CancellationToken ct)
        => await userQuery.FirstOrDefaultAsync(
            query => query
                .IgnoreQueryFilters()
                .Where(u => u.IdentityProviderId == identityProviderId)
                .Select(u => (bool?)u.IsDeleted),
            "IsDeactivatedAsync",
            ct) == true;

    public async Task<UserExtendedDetails?> GetExtendedBySubAsync(IdpId identityProviderId, CancellationToken ct)
    {
        var userRow = await userQuery.FirstOrDefaultAsync(
            query => query
                .Where(u => u.IdentityProviderId == identityProviderId)
                .Select(u => new
                {
                    u.IdentityProviderId,
                    u.Email,
                    u.UserName,
                    Id = EF.Property<UserId>(u, "Id")
                }),
            "GetExtendedUserRow",
            ct);

        if (userRow is null)
            return null;

        var projectIds = await assignedUserQuery.ToListAsync(
            query => query
                .Where(aup => aup.UserId == userRow.Id)
                .Select(aup => aup.ProjectId),
            "GetUserProjectIds",
            ct);

        if (projectIds.Count == 0)
            return new UserExtendedDetails(userRow.IdentityProviderId, userRow.Email, userRow.UserName, []);

        var resources = await assignedResourceQuery.ToListAsync(
            query => query
                .Where(apr => projectIds.Contains(EF.Property<ProjectId>(apr, "ProjectId")) && !apr.IsDeleted && !apr.ProjectResource!.IsDeleted)
                .Select(apr => new ProjectResourceRow(
                    apr.Project!.ShortName,
                    EF.Property<ProjectResourceId>(apr.ProjectResource!, "Id").Value,
                    apr.ProjectResource!.ResourceName.Value,
                    apr.ProjectResource!.Description,
                    apr.ProjectResource!.StartDate,
                    apr.ProjectResource!.EndDate)),
            "GetUserProjectResources",
            ct);

        if (resources.Count == 0)
            return new UserExtendedDetails(userRow.IdentityProviderId, userRow.Email, userRow.UserName, []);

        var resourceIds = resources.Select(r => r.ResourceId).Distinct().ToList();
        var resourceProjectResourceIds = resourceIds.Select(ProjectResourceId.From).ToList();

        // 3. Get location data for those resources. AssignedLocation has no Location/AllocationUnitType
        //    navigations and Location has no LocationType navigation — these relationships are mapped
        //    as unnamed FKs — so join the lookup tables explicitly via their shadow FK columns.
        var locationRows = await assignedLocationQuery.ToListAsync<Location, LocationType, LocationRow>(
            (assigned, locations, locationTypes) =>
                from al in assigned
                where resourceProjectResourceIds.Contains(EF.Property<ProjectResourceId>(al, "ProjectResourceId")) && !al.IsDeleted
                join loc in locations on EF.Property<Guid>(al, "LocationId") equals EF.Property<Guid>(loc, "Id")
                join lt in locationTypes on loc.LocationType equals lt.Id
                select new LocationRow(
                    EF.Property<ProjectResourceId>(al, "ProjectResourceId").Value,
                    EF.Property<Guid>(al, "Id"),
                    al.AllocationUnitCount,
                    loc.Name.Value,
                    loc.Description,
                    loc.Endpoint,
                    loc.IsPrivate,
                    lt.Name,
                    EF.Property<int>(al, "AllocationUnitTypeId")),
            "GetUserLocationRows",
            ct);

        var assignedLocationIds = locationRows.Select(l => l.AssignedLocationId).ToList();

        // 4. Get location values — always run the query; empty IN() returns nothing
        var valueRows = await assignedLocationValueQuery.ToListAsync(
            query => query
                .Where(v => assignedLocationIds.Contains(EF.Property<Guid>(v, "AssignedLocationId")))
                .Select(v => new LocationValueRow(
                    EF.Property<Guid>(v, "AssignedLocationId"),
                    v.Name,
                    v.Value ?? string.Empty)),
            "GetUserLocationValues",
            ct);

        var valuesByLocationId = valueRows
            .GroupToLists(v => v.AssignedLocationId, v => (v.Name, v.Value));

        var locationsByResourceId = locationRows
            .GroupToLists(
                l => l.ResourceId,
                l => new AssignedLocationData(
                    l.LocationName,
                    l.LocationDescription,
                    l.Endpoint,
                    l.IsPrivate,
                    l.LocationTypeName,
                    l.AllocationUnitCount,
                    (EAllocationUnitType)l.AllocationUnitTypeId,
                    valuesByLocationId.GetValueOrDefault(l.AssignedLocationId, [])));

        var projectResources = resources
            .Select(r => new UserProjectResourceData(
                r.ShortName,
                r.ResourceName,
                r.Description,
                r.StartDate,
                r.EndDate,
                locationsByResourceId.GetValueOrDefault(r.ResourceId, [])))
            .ToList();

        return new UserExtendedDetails(
            userRow.IdentityProviderId,
            userRow.Email,
            userRow.UserName,
            projectResources);
    }

    public async Task<QueryPage<UserSummary>> GetUsersAsync(UserFilter filter, int page, int limit, CancellationToken ct)
    {
        List<UserId>? projectUserIds = null;

        if (filter.ProjectShortName is { } psn)
        {
            projectUserIds = await LoadProjectUserIdsAsync(psn, ct);
            if (projectUserIds.Count == 0)
                return new QueryPage<UserSummary>([], 0);
        }

        var userPage = await userQuery.ToPagedListAsync(
            query => query
                .WhereIf(filter.IdentityProviderId is not null, u => u.IdentityProviderId == filter.IdentityProviderId)
                .WhereIf(projectUserIds is not null, u => projectUserIds!.Contains(EF.Property<UserId>(u, "Id")))
                // IdentityProviderId is unique per user, so it doubles as the stable paging tie-breaker.
                .OrderBy(u => u.IdentityProviderId)
                .Select(u => new UserRow(
                    EF.Property<UserId>(u, "Id"),
                    u.IdentityProviderId,
                    u.Email,
                    u.UserName,
                    EF.Property<ProviderId?>(u, "_assignedProviderId"))),
            page,
            limit,
            "GetUsers",
            ct);

        var users = userPage.Items;
        if (users.Count == 0)
            return new QueryPage<UserSummary>([], userPage.TotalItems);

        var projectsByUserId = await LoadProjectsByUserIdAsync(users, ct);
        var providerNamesById = await LoadProviderNamesAsync(users, ct);

        var summaries = users
            .Select(u => new UserSummary(
                u.IdentityProviderId,
                u.Email,
                u.UserName,
                projectsByUserId.GetValueOrDefault(u.Id, []),
                ProviderNameOf(u, providerNamesById)))
            .ToList();

        return new QueryPage<UserSummary>(summaries, userPage.TotalItems);
    }

    public async Task<List<UserWithIdSummary>> GetUsersAsync(CancellationToken ct)
    {
        var users = await userQuery.ToListAsync(
            query => query
                .Select(u => new UserRow(
                    EF.Property<UserId>(u, "Id"),
                    u.IdentityProviderId,
                    u.Email,
                    u.UserName,
                    EF.Property<ProviderId?>(u, "_assignedProviderId"))),
            "GetUsers",
            ct);

        if (users.Count == 0)
            return new List<UserWithIdSummary>();

        var projectsByUserId = await LoadProjectsByUserIdAsync(users, ct);
        var providerNamesById = await LoadProviderNamesAsync(users, ct);

        var summaries = users
            .Select(u => new UserWithIdSummary(
                u.Id,
                u.IdentityProviderId,
                u.Email,
                u.UserName,
                projectsByUserId.GetValueOrDefault(u.Id, []),
                ProviderNameOf(u, providerNamesById)))
            .ToList();

        return summaries;
    }

    private Task<List<UserId>> LoadProjectUserIdsAsync(ProjectShortName psn, CancellationToken ct) =>
        assignedUserQuery.ToListAsync(
            query => query
                .Where(aup => !aup.IsDeleted && aup.Project!.ShortName == psn)
                .Select(aup => aup.UserId),
            "GetUsersProjectUserIds",
            ct);

    private async Task<Dictionary<UserId, IReadOnlyList<ProjectShortName>>> LoadProjectsByUserIdAsync(IReadOnlyList<UserRow> users, CancellationToken ct)
    {
        var userIds = users.Select(u => u.Id).ToList();

        var projectAssignments = await assignedUserQuery.ToListAsync(
            query => query
                .Where(aup => userIds.Contains(aup.UserId) && !aup.IsDeleted)
                .Select(aup => new { aup.UserId, aup.Project!.ShortName }),
            "GetUsersProjectAssignments",
            ct);

        return projectAssignments
            .GroupToLists(pa => pa.UserId, pa => pa.ShortName);
    }

    private async Task<Dictionary<ProviderId, ProviderName>> LoadProviderNamesAsync(IReadOnlyList<UserRow> users, CancellationToken ct)
    {
        // Resolve assigned-provider names for the page's distinct provider ids (a user is addressed by
        // its IdpId, a provider by its name — never a surrogate GUID, ADR-0024).
        var providerIds = users
            .Where(u => u.ProviderId is not null)
            .Select(u => u.ProviderId!.Value)
            .Distinct()
            .ToList();

        return providerIds.Count == 0
            ? []
            : (await providerQuery.ToListAsync(
                q => q.IgnoreQueryFilters()
                    .Where(pv => providerIds.Contains(EF.Property<ProviderId>(pv, "Id")))
                    .Select(pv => new ProviderNameRow(EF.Property<ProviderId>(pv, "Id"), pv.Name)),
                "GetUsersProviderNames",
                ct)).ToDictionary(p => p.Id, p => p.Name);
    }

    private static ProviderName? ProviderNameOf(UserRow user, Dictionary<ProviderId, ProviderName> providerNamesById) =>
        user.ProviderId is { } pid && providerNamesById.TryGetValue(pid, out var name) ? name : null;

    public async Task<string> GetUserPreferencesAsync(IdpId idpId, CancellationToken ct)
    {
        var result = await userQuery.FirstOrDefaultAsync(
            query => query
                .Where(u => u.IdentityProviderId == idpId)
                .Select(u => EF.Property<UserPreference>(u, "_userPreference") == null
                    ? "{}"
                    : EF.Property<UserPreference>(u, "_userPreference")!.Data),
            "GetUserPreferences",
            ct);

        return result ?? "{}";
    }

    // Private projection records — avoid anonymous types for better compiler inference
    private sealed record UserRow(
        UserId Id,
        IdpId IdentityProviderId,
        Email Email,
        UserName UserName,
        ProviderId? ProviderId);

    private sealed record ProviderNameRow(ProviderId Id, ProviderName Name);

    private sealed record ProjectResourceRow(
        ProjectShortName ShortName,
        Guid ResourceId,
        string ResourceName,
        string? Description,
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

    private sealed record LocationValueRow(
        Guid AssignedLocationId,
        string Name,
        string Value);
}