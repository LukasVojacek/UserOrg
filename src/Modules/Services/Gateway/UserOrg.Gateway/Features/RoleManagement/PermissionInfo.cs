namespace UserOrg.Gateway.Features.RoleManagement;

/// <summary>
/// Represents a permission exposed by Forge.Core.
/// </summary>
internal sealed class PermissionInfo
{
    public required string Name { get; init; }

    public string? Description { get; init; }
}