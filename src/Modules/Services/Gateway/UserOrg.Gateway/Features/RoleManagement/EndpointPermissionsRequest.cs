namespace UserOrg.Gateway.Features.RoleManagement;

/// <summary>
/// Request payload when setting permissions for an endpoint mapping.
/// </summary>
internal sealed class EndpointPermissionsRequest
{
    public required IReadOnlyCollection<string> Permissions { get; init; }
}