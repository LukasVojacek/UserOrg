namespace Core.Features.UserInfo.GetUserInfo;

internal sealed record UserInfoResponse(
    string IdentityProviderUserId,
    string? Name,
    string? UserName,
    string? Email,
    IReadOnlyList<UserSystemRoleRecord> SystemRoles);