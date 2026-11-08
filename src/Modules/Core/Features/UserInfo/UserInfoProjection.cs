using Core.Features.UserInfo.GetUserInfo;

namespace Core.Features.UserInfo;

internal static class UserInfoProjection
{
    public static UserInfoResponse BuildUserInfo(
        UserDetails user,
        string? name,
        string defaultRoleName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> roleDefinitions,
        IDictionary<string, HashSet<ProjectShortName>> projectRoles)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!roleDefinitions.TryGetValue(defaultRoleName, out var defaultPerms))
            throw new InvalidOperationException($"Default user role '{defaultRoleName}' not found in Role Service.");

        var roles = new List<UserSystemRoleRecord>
        {
            // Default system-level role has no project scope (null short name)
            new(user.IdentityProviderUserId.Value, user.Email.Value, defaultRoleName, null, defaultPerms)
        };

        foreach (var (roleName, shortNames) in projectRoles)
        {
            if (!roleDefinitions.TryGetValue(roleName, out var perms))
                throw new InvalidOperationException($"Project role '{roleName}' not found in Role Service.");

            foreach (var shortName in shortNames)
                roles.Add(new(user.IdentityProviderUserId.Value, user.Email.Value, roleName, shortName.Value, perms));
        }

        return new UserInfoResponse(user.IdentityProviderUserId.Value, name, user.UserName.Value, user.Email.Value, roles);
    }
}