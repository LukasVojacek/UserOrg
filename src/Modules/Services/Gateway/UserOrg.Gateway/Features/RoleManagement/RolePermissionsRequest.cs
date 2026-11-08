namespace UserOrg.Gateway.Features.RoleManagement;

/// <summary>
/// Request payload used when updating permissions for an existing role.
/// </summary>
internal sealed class RolePermissionsRequest
{
    public required IReadOnlyCollection<string> Permissions { get; init; }
}