namespace UserOrg.Gateway.Features.RoleManagement;

/// <summary>
/// Lightweight view of a role for listing purposes.
/// </summary>
internal sealed class RoleSummary
{
    public required string Name { get; init; }

    public string? Description { get; init; }
}