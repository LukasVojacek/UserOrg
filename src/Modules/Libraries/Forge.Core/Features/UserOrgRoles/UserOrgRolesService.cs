using Forge.Core.Features.Role.Services;

namespace Forge.Core.Features.UserOrgRoles;

internal sealed class UserOrgRolesService(UserOrgDbContext dbContext, IRoleService roleService) : IUserOrgRolesService
{
    public async Task<IList<UserPermissionDto>> GetUserPermissionsAsync(string identityProviderUserId, Guid? projectId, CancellationToken cancellationToken)
    {
        var userorgData = await dbContext.GetUserPermissionsAsync(identityProviderUserId, cancellationToken);
        var roles = await roleService.ListRoleNames(cancellationToken);
        if (!roles.IsSuccess)
        {
            throw new InvalidOperationException("Could not retrieve role names");
        }

        return userorgData
            .Select(role => role with
            {
                SystemRoleName = roles.Value.TryGetValue(role.SystemRoleId, out var name) ? name : "Unknown"
            })
            .ToList();
    }

    public async Task<HashSet<string>> GetUserPermissionKeysAsync(string identityProviderUserId, CancellationToken cancellationToken)
    {
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var userRoles = await GetUserPermissionsAsync(identityProviderUserId, projectId: null, cancellationToken);

        var roleNames = userRoles
            .Where(r => !string.IsNullOrEmpty(r.SystemRoleName))
            .Select(r => r.SystemRoleName!)
            .Distinct()
            .ToList();

        foreach (var roleName in roleNames)
        {
            var roleResult = await roleService.GetRole(roleName, cancellationToken);
            if (!roleResult.IsSuccess || roleResult.Value is null)
                continue;

            foreach (var permission in roleResult.Value.Permissions)
            {
                permissions.Add(permission.Key.Value);
            }
        }

        return permissions;
    }
}