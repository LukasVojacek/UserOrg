using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.UserPermission;

public record UserSystemRoleModel(string IdpUserId, string UserEmail, string RoleName, ProjectShortName ProjectShortName, IList<string> SystemPermissionTypes)
{
    private UserSystemRoleModel() : this(Guid.Empty.ToString(), string.Empty, string.Empty, ProjectShortName.Empty, [])
    {

    }
    public static UserSystemRoleModel Empty => new();
}