using Forge.Core.Features.Role.Services;

using Microsoft.Extensions.Logging;

using UserOrg.Shared;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Exceptions;
using UserOrganizationManagement.Domain.ExtModels.OpenId;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.UserPermission;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Application.UserOrg.Services;

public sealed class UserService(IUserOrgUnitOfWork uow, IRoleListService roleListService, ILogger<UserService> logger) : IUserService
{
    private readonly IUserOrgUnitOfWork _uow = uow;
    private readonly ILogger<UserService> _logger = logger;

    public async Task<Guid?> GetProviderIdByIdpUserIdAsync(string idpUserId, CancellationToken ct)
    {
        return await _uow.UserRepository.GetProviderIdByIdpUserIdAsync(idpUserId, ct);
    }

    public async Task<IDictionary<ProjectShortName, string>> GetUserProjectsRoleAsync(string idpUserId, CancellationToken ct)
    {
        var roles = await roleListService.ListRoles(ct);
        if (!roles.IsSuccess)
        {
            throw new Exception("Failed to retrieve roles from Role Service.");
        }
        var returnDict = new Dictionary<ProjectShortName, string>();
        var userProjectsRoles = await _uow.UserPermissionRepository.GetPermissionsForUserAsync(idpUserId, ct);
        foreach (var perm in userProjectsRoles.Where(x => !x.IsDeleted))
        {
            if (perm.Project != null && perm.SystemRoleId != null)
            {
                var projectShortName = perm.Project.ShortName;
                var roleName = roles.Value.FirstOrDefault(r => r.Id == perm.SystemRoleId)?.Name;
                if (roleName != null)
                {
                    returnDict.TryAdd(projectShortName, roleName);
                }
            }
        }
        return returnDict;
    }



    public async Task CreateUser(User user, CancellationToken ct = default)
    {
        await _uow.UserRepository.InsertAsync(user, ct)
            ;

        var userCount = await _uow.UserRepository.TotalCount(ct);
        var isFirstUser = userCount == 0;

        var roles = await roleListService.ListRoles(ct);
        if (!roles.IsSuccess)
        {
            throw new Exception("Failed to retrieve roles from Role Service.");
        }

        var selectedRoleName = isFirstUser
            ? RoleInfoConstants.PlatformAdministratorRoleName
            : RoleInfoConstants.PlatformUserRoleName;

        var selectedRole = roles.Value.FirstOrDefault(r => r.Name == selectedRoleName);
        if (selectedRole == null)
        {
            throw new SystemRoleNotFoundException($"System role '{selectedRoleName}' not found in Role Service.");
        }

        if (isFirstUser)
        {
            _logger.LogInformation(
                "First user created; assigning provider administrator role. UserId: {UserId}, Role: {Role}",
                user.IdentityProviderUserId,
                selectedRoleName);
        }

        var perm = new UserPermission()
        {
            CreatedAt = DateTime.UtcNow,
            User = user,
            Project = null,
            SystemRoleId = selectedRole.Id
        };

        await _uow.UserPermissionRepository.InsertAsync(perm, ct);
        await _uow.SaveAsync(ct);
    }

    public async Task<User?> GetUserBySidAsync(string sid, CancellationToken ct) => await _uow.UserRepository.GetBySidWithProjectsAsync(sid, ct);

    public async Task<User?> GetUserByUsernameAsync(string username, CancellationToken ct) => await _uow.UserRepository.GetByUsernameAsync(username, ct);

    public async Task<IEnumerable<User>> GetUsersWithoutProjectAsync(CancellationToken ct = default)
    {
        var users = await _uow.UserRepository.GetAllWithoutProjectAsync(ct);
        return users;
    }

    public async Task UpdateUser(User user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var storedUser = await _uow.UserRepository.GetByIdAsync(user.Id, ct)
            ?? throw new RecordNotFoundException($"""User with UserName:"{user.UserName}" does not exist!""");

        storedUser.UserName = user.UserName;
        storedUser.Email = user.Email;

        await _uow.SaveAsync(ct);
    }

    public async Task<UserInfoModel> PopulateUserInfo(OpenIdUserInfoModel openIdUserInfo, IDictionary<string, HashSet<ProjectShortName>> projectRoles, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(openIdUserInfo);

        var result = new UserInfoModel()
        {
            KeycloakSid = openIdUserInfo.Sub,
            Email = openIdUserInfo.Email,
            UserName = openIdUserInfo.PreferredUsername,
            Name = openIdUserInfo.Name
        };

        var user = await _uow.UserRepository.GetBySidWithProjectsAsync(openIdUserInfo.Sub, ct);
        UserNotFoundException.ThrowIfNull(user);
        result.Id = user.Id;

        var roles = await roleListService.ListRoles(ct);
        if (!roles.IsSuccess)
        {
            throw new Exception("Failed to retrieve roles from Role Service.");
        }

        var defaultUserRoleName = ResolveSystemRoleName(projectRoles);

        var defaultUserRole = roles.Value.FirstOrDefault(r => r.Name == defaultUserRoleName)
            ?? throw new SystemRoleNotFoundException($"Default user role '{defaultUserRoleName}' not found in Role Service.");

        var projects = await _uow.ProjectRepository.GetProjectsContainsUserAsync(null, user.IdentityProviderUserId, ct);
        var usrDefaultRoleModel = new UserSystemRoleModel(
          user.IdentityProviderUserId,
            user.Email,
            defaultUserRole.Name,
            ProjectShortName.Empty,
          defaultUserRole.Permissions.Select(p => p.Key.Value).ToList());
        result.SystemRoles.Add(usrDefaultRoleModel);
        foreach (var project in projects)
        {
            var projectRoleName = projectRoles.Where(x => x.Value.Any(pr => pr == project.ShortName))
                .Select(x => x.Key)
                .FirstOrDefault();

            if (string.IsNullOrEmpty(projectRoleName))
                continue;

            var projectRole = roles.Value.FirstOrDefault(r => r.Name == projectRoleName)
                ?? throw new SystemRoleNotFoundException($"Project role '{projectRoleName}' not found in Role Service.");

            var usrRoleModel = new UserSystemRoleModel(
              user.IdentityProviderUserId,
                user.Email,
                projectRole.Name,
                project.ShortName!,
              projectRole.Permissions.Select(p => p.Key.Value).ToList());
            result.SystemRoles.Add(usrRoleModel);

        }

        return result;
    }

    public async Task<UserInfoExtendedModel> PopulateExtendedUserInfo(OpenIdUserInfoModel openIdUserInfo, IDictionary<string, HashSet<ProjectShortName>> projectRoles, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(openIdUserInfo);

        var result = new UserInfoExtendedModel()
        {
            KeycloakSid = openIdUserInfo.Sub,
            Email = openIdUserInfo.Email,
            UserName = openIdUserInfo.PreferredUsername,
            Name = openIdUserInfo.Name
        };

        var keycloakGuidId = openIdUserInfo.Sub.Split('@')[0];
        var user = await _uow.UserRepository.GetBySidWithProjectsAsync(keycloakGuidId, ct);
        UserNotFoundException.ThrowIfNull(user);
        result.Id = user.Id;

        var projects = await _uow.ProjectRepository.GetProjectsContainsUserExtendedAsync(null, user.IdentityProviderUserId, ct);

        var roles = await roleListService.ListRoles(ct);
        if (!roles.IsSuccess)
        {
            throw new Exception("Failed to retrieve roles from Role Service.");
        }

        var defaultUserRoleName = ResolveSystemRoleName(projectRoles);

        var defaultUserRole = roles.Value.FirstOrDefault(r => r.Name == defaultUserRoleName)
            ?? throw new SystemRoleNotFoundException($"Default user role '{defaultUserRoleName}' not found in Role Service.");

        var usrDefaultRoleModel = new UserSystemRoleExtendedModel(
          ProjectShortName.Empty,
            user.Email,
            defaultUserRole?.Name!,
            [],
          defaultUserRole.Permissions.Select(p => p.Key.Value).ToList());
        result.SystemRoles.Add(usrDefaultRoleModel);

        foreach (var project in projects)
        {
            var projectRoleName = projectRoles
                .Where(x => x.Value.Any(pr => pr == project.ShortName))
                .Select(x => x.Key)
                .FirstOrDefault();

            if (string.IsNullOrEmpty(projectRoleName))
                continue;

            var projectRole = roles.Value.FirstOrDefault(r => r.Name == projectRoleName)
                ?? throw new SystemRoleNotFoundException($"Project role '{projectRoleName}' not found in Role Service.");

            // Resources are already projected and mapped by the repository — no further mapping needed here
            var tmpRoleDetail = new UserSystemRoleExtendedModel(
                project.ShortName,
                user.Email,
                projectRoleName,
                project.Resources,
                projectRole.Permissions.Select(p => p.Key.Value).ToList());

            result.SystemRoles.Add(tmpRoleDetail);
        }

        return result;
    }

    public async Task<UserInfoExtendedModel> PopulateExtendedUserInfo(string idpUserId, IEnumerable<ProjectId>? filterProjects, IDictionary<string, HashSet<ProjectShortName>> projectRoles, CancellationToken ct = default)
    {
        var user = await _uow.UserRepository.GetBySidWithProjectsAsync(idpUserId, ct);

        UserNotFoundException.ThrowIfNull(user);
        var result = new UserInfoExtendedModel()
        {
            KeycloakSid = user.IdentityProviderUserId.ToString(),
            Email = user.Email,
            Id = user.Id,
            UserName = user.UserName,
            IsDeleted = user.IsDeleted
        };

        var roles = await roleListService.ListRoles(ct);
        if (!roles.IsSuccess)
        {
            throw new Exception("Failed to retrieve roles from Role Service.");
        }

        var defaultUserRoleName = ResolveSystemRoleName(projectRoles);

        var defaultUserRole = roles.Value.FirstOrDefault(r => r.Name == defaultUserRoleName)
            ?? throw new SystemRoleNotFoundException($"Default user role '{defaultUserRoleName}' not found in Role Service.");

        var projects = await _uow.ProjectRepository.GetProjectsContainsUserExtendedAsync(null, user.IdentityProviderUserId, ct);

        var filteredProjects = filterProjects switch
        {
            null => projects,
            _ => projects.Where(x => filterProjects.Contains(x.Id))
        };

        foreach (var project in filteredProjects)
        {
            var projectRoleName = projectRoles
                .Where(x => x.Value.Any(pr => pr == project.ShortName))
                .Select(x => x.Key)
                .FirstOrDefault();

            if (string.IsNullOrEmpty(projectRoleName))
                continue;

            var projectRole = roles.Value.FirstOrDefault(r => r.Name == projectRoleName)
                ?? throw new SystemRoleNotFoundException($"Project role '{projectRoleName}' not found in Role Service.");

            // Resources are already projected and mapped by the repository — no further mapping needed here
            var tmpRoleDetail = new UserSystemRoleExtendedModel(
                project.ShortName,
                user.Email,
                projectRole.Name,
                project.Resources,
                projectRole.Permissions.Select(p => p.Key.Value).ToList());

            result.SystemRoles.Add(tmpRoleDetail);
        }

        return result;
    }

    /// <summary>
    /// Resolves the effective platform-level role name for a user based on the roles present in
    /// <paramref name="projectRoles"/>. System role entries (added with an empty project set by the
    /// endpoint) are checked in priority order: Admin → ProviderAdmin → User.
    /// </summary>
    private static string ResolveSystemRoleName(IDictionary<string, HashSet<ProjectShortName>> projectRoles) =>
        projectRoles.ContainsKey(RoleInfoConstants.PlatformAdministratorRoleName) ? RoleInfoConstants.PlatformAdministratorRoleName :
        projectRoles.ContainsKey(RoleInfoConstants.ProviderAdministratorRoleName) ? RoleInfoConstants.ProviderAdministratorRoleName :
        RoleInfoConstants.PlatformUserRoleName;

    public async Task<IEnumerable<UserModel>> GetUsersAsync(ProjectShortName? projectShortName, string? idpUserId, CancellationToken ct = default)
    {
        var users = await _uow.UserRepository.GetAllAsync(projectShortName, idpUserId, ct);

        return users.Select(x => new UserModel(x.IdentityProviderUserId, x.Email, x.UserName, x.AssignedToProjects.Select(x => x.Project.ShortName), x.AssignedProvider?.Id));
    }

    public async Task<string> GetUserPreferences(string idpUserId, CancellationToken ct)
    {
        var preferences = await _uow.UserPreferenceRepository.GetUserPreferences(idpUserId, ct)
            ;
        return preferences?.Data ?? "{}";
    }
    public Task UpdateUserPreferences(string idpUserId, string jsonObject, CancellationToken ct) => _uow.UserPreferenceRepository.UpdateUserPreferences(idpUserId, jsonObject, ct);
}