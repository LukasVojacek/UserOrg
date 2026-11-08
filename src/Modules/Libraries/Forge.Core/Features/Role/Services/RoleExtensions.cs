using Forge.Core.Features.Permission.Services;

namespace Forge.Core.Features.Role.Services;

internal static class RoleExtensions
{
    extension(Persistence.Role role)
    {
        internal RoleInfo ToRoleInfo()
        {
            var permissions = role.RolePermissions
                .Select(p => p.ToPermissionInfo())
                .ToArray();

            return new RoleInfo(
                role.Id,
                role.Name,
                role.Description ?? string.Empty,
                role.Category,
                permissions,
                role.IsActive,
                role.Level);
        }
    }
}