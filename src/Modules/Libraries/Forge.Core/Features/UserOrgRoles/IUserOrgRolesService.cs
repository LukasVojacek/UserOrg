namespace Forge.Core.Features.UserOrgRoles;

public interface IUserOrgRolesService
{
    Task<IList<UserPermissionDto>> GetUserPermissionsAsync(string identityProviderUserId, Guid? projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves all permission keys for a user by querying user roles and expanding them to their assigned permissions.
    /// Returns a set of permission key strings (e.g., "perm:read", "perm:users:write").
    /// </summary>
    Task<HashSet<string>> GetUserPermissionKeysAsync(string identityProviderUserId, CancellationToken cancellationToken);
}