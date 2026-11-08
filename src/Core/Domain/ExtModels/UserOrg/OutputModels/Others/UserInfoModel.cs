using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.UserPermission;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;

public sealed record UserInfoModel()
{
    public Guid Id { get; set; }
    public string? KeycloakSid { get; set; }
    public string? Name { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public IList<UserSystemRoleModel> SystemRoles { get; init; } = [];
}