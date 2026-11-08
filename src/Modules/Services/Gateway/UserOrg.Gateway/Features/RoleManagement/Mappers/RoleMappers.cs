using Forge.Core.Features.Role.Services;

namespace UserOrg.Gateway.Features.RoleManagement.Mappers;

/// <summary>
/// Extension methods for mapping between Forge.Core RoleInfo and Gateway DTOs.
/// </summary>
internal static class RoleMappers
{
    /// <summary>
    /// Converts Forge.Core RoleInfo to lightweight RoleSummary for list views.
    /// </summary>
    /// <param name="roleInfo">The source role information.</param>
    /// <returns>Role summary DTO.</returns>
    internal static RoleSummary ToRoleSummary(this RoleInfo roleInfo) =>
        new()
        {
            Name = roleInfo.Name,
            Description = roleInfo.Description
        };

    /// <summary>
    /// Converts Forge.Core RoleInfo to detailed RoleDetail including permissions.
    /// </summary>
    /// <param name="roleInfo">The source role information.</param>
    /// <returns>Role detail DTO with permission names.</returns>
    internal static RoleDetail ToRoleDetail(this RoleInfo roleInfo) =>
        new()
        {
            Name = roleInfo.Name,
            Description = roleInfo.Description,
            Category = roleInfo.Category,
            Level = roleInfo.Level,
            Permissions = [.. roleInfo.Permissions.Select(p => p.Key.Value)]
        };
}