namespace RemI.Features.Users;

/// <summary>
/// A user as the RemI service sees it. Mirrors the field selection of the v2 admin user list
/// (<c>GET /api/v2/Users</c>): identity is the identity-provider subject, not a database key.
/// </summary>
public sealed record UserResponse(
    string Id,
    string IdpUserId,
    string Email,
    string UserName,
    IReadOnlyList<string> Projects,
    string? AssignedProviderName) : BaseUserResponse(IdpUserId, UserName);