namespace Forge.Core.Features.EndpointMap;

/// <summary>
/// Represents a single endpoint mapping item in the import payload.
/// </summary>
public sealed record EndpointMappingItemDto(
    string EndpointName,
    string? Description,
    string PathTemplate,
    string HttpMethod,
    List<string>? PermissionKeys,
    DateTimeOffset? CreatedAt);

/// <summary>
/// Root payload for endpoint mapping import operations.
/// </summary>
public sealed record EndpointMappingImportDto(
    int? Count,
    List<EndpointMappingItemDto>? Items);