namespace Forge.Core.Features.Role;

/// <summary>
/// Represents a single role item in the import payload.
/// </summary>
public sealed record RoleItemDto(
    string Name,
    string? Description,
    string? Category,
    bool IsActive,
    int Level,
    List<string>? PermissionKeys,
    DateTimeOffset? CreatedAt);

/// <summary>
/// Root payload for role import operations.
/// </summary>
public sealed record RoleImportDto(
    int? Count,
    List<RoleItemDto>? Items);