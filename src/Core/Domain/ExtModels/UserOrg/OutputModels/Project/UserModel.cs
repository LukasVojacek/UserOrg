using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;

public record UserModel(string IdpUserId, string Email, string UserName, IEnumerable<ProjectShortName> InProjects, Guid? AdminForProviderId);