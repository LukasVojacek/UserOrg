namespace Core.Features.UserInfo.GetUserInfo;

internal sealed record UserSystemRoleRecord(
    string IdpUserId,
    string UserEmail,
    string RoleName,
    string? ProjectShortName,
    IReadOnlyList<string> SystemPermissionTypes);