using System.Text.Json.Serialization;

using UserOrg.Shared;

namespace Forge.Core.Features.Permission;

public sealed record PermissionItemDto(
    [property: JsonConverter(typeof(PermissionKeyJsonConverter))]
    PermissionKey Key,
    string? Description,
    DateTimeOffset? CreatedAt);

public sealed record PermissionImportDto(
    int? Count,
    System.Collections.Generic.List<PermissionItemDto>? Items);