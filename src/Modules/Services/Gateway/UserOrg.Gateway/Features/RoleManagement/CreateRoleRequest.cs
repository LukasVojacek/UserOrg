namespace UserOrg.Gateway.Features.RoleManagement;

/// <summary>
/// Request payload for creating a role through the gateway.
/// </summary>
internal sealed class CreateRoleRequest
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public IReadOnlyCollection<string> Permissions { get; init; } = Array.Empty<string>();
    public required string Category { get; init; }
    public int Level { get; init; } = 1;
}