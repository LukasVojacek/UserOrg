namespace Core.Features.UserInfo.GetExtendedUserInfo;

internal sealed record UserInfoExtendedResponse(
    string IdentityProviderUserId,
    string? Name,
    string? UserName,
    string? Email,
    IReadOnlyList<UserSystemRoleExtendedRecord> SystemRoles);

internal sealed record UserSystemRoleExtendedRecord(
    string IdpUserId,
    string UserEmail,
    string RoleName,
    string? ProjectShortName,
    IReadOnlyList<string> SystemPermissionTypes,
    IReadOnlyList<ProjectResourceSummary> ProjectResources);