using UserOrg.Shared;

using ForgeCorePermissionInfo = Forge.Core.Features.Permission.Services.PermissionInfo;

namespace UserOrg.Gateway.Features.RoleManagement.Mappers;

/// <summary>
/// Extension methods for mapping between Forge.Core PermissionInfo and Gateway DTOs.
/// </summary>
internal static class PermissionMappers
{
    /// <summary>
    /// Converts Forge.Core PermissionInfo to Gateway PermissionInfo DTO.
    /// </summary>
    /// <param name="corePermissionInfo">The source permission information from Forge.Core.</param>
    /// <returns>Permission information DTO for Gateway UI.</returns>
    internal static PermissionInfo ToDto(this ForgeCorePermissionInfo corePermissionInfo) =>
        new()
        {
            Name = corePermissionInfo.Key.Value,
            Description = corePermissionInfo.Description
        };

    /// <summary>
    /// Converts a permission name string to PermissionKey.
    /// </summary>
    /// <param name="permissionName">The permission name string.</param>
    /// <returns>PermissionKey if valid, null if invalid format.</returns>
    internal static PermissionKey? ToPermissionKey(this string permissionName)
    {
        try
        {
            return new PermissionKey(permissionName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a collection of permission name strings to a HashSet of PermissionKey.
    /// Filters out invalid keys.
    /// </summary>
    /// <param name="permissionNames">Collection of permission name strings.</param>
    /// <returns>HashSet of valid PermissionKey instances.</returns>
    internal static HashSet<PermissionKey> ToPermissionKeys(this IEnumerable<string> permissionNames) =>
        [.. permissionNames
            .Select(name => name.ToPermissionKey())
            .Where(key => key is not null)
            .Select(key => key!.Value)];
}