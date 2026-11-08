using Core.Features.UserInfo.GetExtendedUserInfo;

namespace Core.Features.UserInfo;

internal static class UserInfoExtendedProjection
{
    public static UserInfoExtendedResponse BuildExtendedUserInfo(
        UserExtendedDetails user,
        string? jwtName,
        string defaultRoleName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> roleDefinitions,
        IDictionary<string, HashSet<ProjectShortName>> projectRoles)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!roleDefinitions.TryGetValue(defaultRoleName, out var defaultPerms))
            throw new InvalidOperationException($"Default user role '{defaultRoleName}' not found in Role Service.");

        // Index resources by project short name for O(1) lookup during role assembly
        var resourcesByProject = user.ProjectResources
            .GroupBy(r => r.ProjectShortName)
            .ToDictionary(g => g.Key, g => g.Select(ToResourceSummary).ToList());

        var roles = new List<UserSystemRoleExtendedRecord>
        {
            // Default system-level role has no project scope (null short name) and no project resources
            new(user.IdentityProviderId.Value, user.Email.Value, defaultRoleName, null, defaultPerms, [])
        };

        foreach (var (roleName, shortNames) in projectRoles)
        {
            if (!roleDefinitions.TryGetValue(roleName, out var perms))
                throw new InvalidOperationException($"Project role '{roleName}' not found in Role Service.");

            foreach (var shortName in shortNames)
            {
                var resources = resourcesByProject.GetValueOrDefault(shortName, []);
                roles.Add(new(user.IdentityProviderId.Value, user.Email.Value, roleName, shortName.Value, perms, resources));
            }
        }

        return new UserInfoExtendedResponse(user.IdentityProviderId.Value, jwtName, user.UserName.Value, user.Email.Value, roles);
    }

    private static ProjectResourceSummary ToResourceSummary(UserProjectResourceData r)
        => new(
            r.ResourceName,
            r.Description,
            r.StartDate,
            r.EndDate,
            r.Locations.Select(ToClusterInfo).ToList());

    private static ClusterInfo ToClusterInfo(AssignedLocationData l)
        => new(
            l.LocationName,
            l.Description,
            l.Endpoint,
            l.IsPrivate,
            l.LocationType,
            l.AllocationUnitCount,
            l.AllocationUnitType,
            l.Values.Select(v => new ClusterAttribute(v.Name, v.Value)).ToList());
}