namespace Forge.Core.Features.EndpointMap.Services;

public sealed record EndpointPermissionMap(string EndpointName, IList<string> Permissions)
{
    public IList<string> Permissions { get => field; init => field = [.. value.Distinct()]; } = Permissions;
    public string? PathTemplate { get; init; }
    public string? HttpMethod { get; init; }
    public string? Description { get; init; }
}