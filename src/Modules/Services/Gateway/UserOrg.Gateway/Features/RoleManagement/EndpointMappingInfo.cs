namespace UserOrg.Gateway.Features.RoleManagement;

/// <summary>
/// Represents permissions associated with an endpoint.
/// </summary>
internal sealed class EndpointMappingInfo
{
    public required string EndpointName { get; init; }

    public IReadOnlyCollection<string> Permissions { get; init; } = Array.Empty<string>();

    public string? PathTemplate { get; init; }

    public string? HttpMethod { get; init; }

    public string? Description { get; init; }
}