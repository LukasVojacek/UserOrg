using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ExtModels.OpenId;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Application.UserOrg.IServices;

public interface IUserService
{
    Task<Guid?> GetProviderIdByIdpUserIdAsync(string idpUserId, CancellationToken ct);
    Task<User?> GetUserBySidAsync(string sid, CancellationToken ct);
    Task<User?> GetUserByUsernameAsync(string username, CancellationToken ct);
    Task<IEnumerable<User>> GetUsersWithoutProjectAsync(CancellationToken ct = default);
    Task<IEnumerable<UserModel>> GetUsersAsync(ProjectShortName? projectShortName, string? idpUserId, CancellationToken ct = default);
    Task<IDictionary<ProjectShortName, string>> GetUserProjectsRoleAsync(string idpUserId, CancellationToken ct);
    Task CreateUser(User user, CancellationToken ct = default);
    Task UpdateUser(User user, CancellationToken ct = default);
    Task<UserInfoModel> PopulateUserInfo(OpenIdUserInfoModel openIdUserInfo, IDictionary<string, HashSet<Domain.ValueObjects.ProjectShortName>> projectRoles, CancellationToken ct = default);
    Task<UserInfoExtendedModel> PopulateExtendedUserInfo(OpenIdUserInfoModel openIdUserInfo, IDictionary<string, HashSet<ProjectShortName>> projectRoles, CancellationToken ct = default);
    Task<UserInfoExtendedModel> PopulateExtendedUserInfo(string idpUserId, IEnumerable<ProjectId> filterProjects, IDictionary<string, HashSet<ProjectShortName>> projectRoles, CancellationToken ct = default);
    Task<string> GetUserPreferences(string idpUserId, CancellationToken ct);
    Task UpdateUserPreferences(string idpUserId, string jsonObject, CancellationToken ct);
}