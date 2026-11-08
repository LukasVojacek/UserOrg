using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.UserPermission;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;

public sealed record UserInfoExtendedModel()
{
    public Guid Id { get; set; }
    public string? KeycloakSid { get; set; }
    public string? Name { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public bool IsDeleted { get; set; }
    public IList<UserSystemRoleExtendedModel> SystemRoles { get; init; } = [];
}