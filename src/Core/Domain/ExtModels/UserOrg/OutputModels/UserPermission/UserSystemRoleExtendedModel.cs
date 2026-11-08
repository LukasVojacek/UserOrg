using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.UserPermission;

public record UserSystemRoleExtendedModel(ProjectShortName ProjectShortName, string UserEmail, string RoleName, IEnumerable<ProjectResourceDetailModel> ProjectResources, IList<string> SystemPermissionTypes)
{
    private UserSystemRoleExtendedModel() : this(ProjectShortName.Empty, string.Empty, string.Empty, [], [])
    {

    }
    public static UserSystemRoleExtendedModel Empty => new();
}