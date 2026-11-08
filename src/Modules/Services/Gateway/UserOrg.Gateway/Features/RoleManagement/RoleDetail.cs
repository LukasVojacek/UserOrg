namespace UserOrg.Gateway.Features.RoleManagement;

/// <summary>
/// Full role detail including assigned permissions.
/// </summary>
internal sealed class RoleDetail
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public required string Category { get; init; }

    public required int Level { get; init; }

    public IReadOnlyCollection<string> Permissions { get; init; } = Array.Empty<string>();
}