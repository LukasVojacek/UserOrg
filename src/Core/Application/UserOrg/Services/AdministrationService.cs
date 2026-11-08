using Forge.Core.Features.Role.Services;

using Microsoft.Extensions.Caching.Hybrid;

using UserOrg.Shared;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Exceptions;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Application.UserOrg.Services;

public class AdministrationService(IUserService userService, IProjectService projectService, IUserOrgUnitOfWork uow, IRoleListService roleListService, HybridCache cache) : IAdministrationService
{
    public async Task<IList<UserInfoExtendedModel>> GetSyncDetails(Guid providerId, CancellationToken ct)
    {
        IList<Project> projects = await projectService.GetProjectsForProvider(providerId, default);
        IEnumerable<ProjectId> filterList = projects.Select(p => p.Id);
        var users = new List<User>();

        users.AddRange(projects.SelectMany(x => x.AssignedUsers)
                .Where(x => !x.IsDeleted)
          .Select(x => x.User)
          .DistinctBy(x => x.IdentityProviderUserId));

        //add all users, soft-deleted included: a deactivated user is reported with IsDeleted = true so the
        // consumer (iRODS sync) can drop it, instead of silently vanishing from the list (#52)
        IReadOnlyList<string> allUserIds = await uow.UserRepository.GetAllIdpUserIdsIncludingDeletedAsync(ct);

        var rtn = new List<UserInfoExtendedModel>();
        foreach (User user in users)
        {
            var roleProjects = await PrepareRoleProjects(user.IdentityProviderUserId, ct);

            UserInfoExtendedModel tmp = await userService.PopulateExtendedUserInfo(user.IdentityProviderUserId, filterList, roleProjects, ct);
            rtn.Add(tmp);
        }

        foreach (string idpUserId in allUserIds.Where(x => !rtn.Exists(y => y.KeycloakSid == x)))
        {
            var roleProjects = await PrepareRoleProjects(idpUserId, ct);

            UserInfoExtendedModel tmp = await userService.PopulateExtendedUserInfo(idpUserId, null, roleProjects, ct);
            rtn.Add(tmp);
        }

        return rtn;
    }

    private async Task<Dictionary<string, HashSet<ProjectShortName>>> PrepareRoleProjects(string idpUserId, CancellationToken ct)
    {
        var userRoles = await userService.GetUserProjectsRoleAsync(idpUserId, ct);
        var roleProjects = new Dictionary<string, HashSet<ProjectShortName>>();
        foreach (var role in userRoles)
        {
            if (!roleProjects.ContainsKey(role.Value))
            {
                roleProjects[role.Value] = new HashSet<ProjectShortName>();
            }
            roleProjects[role.Value].Add(role.Key);
        }

        return roleProjects;
    }

    public async Task PromoteToProviderAdmin(string idpUserId, Guid providerId, CancellationToken ct)
    {
        User user = await uow.UserRepository.GetBySidWithProviderAsync(idpUserId, ct)
            ;
        UserNotFoundException.ThrowIfNull(user);

        Provider provider = await uow.ProviderRepository.GetByIdAsync(providerId, ct)
            ;
        ProviderNotFoundException.ThrowIfNull(provider);

        var roles = await roleListService.ListRoles(ct);
        if (!roles.IsSuccess)
        {
            throw new Exception("Failed to retrieve roles from Role Service.");
        }
        var platformAdminRole = roles.Value.FirstOrDefault(r => r.Name == RoleInfoConstants.PlatformAdministratorRoleName);
        var providerAdminRole = roles.Value.FirstOrDefault(r => r.Name == RoleInfoConstants.ProviderAdministratorRoleName);
        if (platformAdminRole == null || providerAdminRole == null)
        {
            throw new Exception("Required roles not found in Role Service.");
        }

        UserPermission systemPermission = await uow.UserPermissionRepository.GetSystemPermissionsForUserAsync(user.Id, ct)
            ;

        UserPermissionNotFoundException.ThrowIfNull(systemPermission);

        if (systemPermission.SystemRoleId != platformAdminRole.Id)
        {
            systemPermission.SystemRoleId = providerAdminRole.Id;
        }

        user.AssignedProvider = provider;
        await uow.SaveAsync(ct);
    }

    public async Task ResetToPlatformUser(string idpUserId, CancellationToken ct)
    {
        User user = await uow.UserRepository.GetBySidWithProviderAsync(idpUserId, ct)
            ;
        UserNotFoundException.ThrowIfNull(user);

        var roles = await roleListService.ListRoles(ct);
        if (!roles.IsSuccess)
        {
            throw new Exception("Failed to retrieve roles from Role Service.");
        }
        var platformAdminRole = roles.Value.FirstOrDefault(r => r.Name == RoleInfoConstants.PlatformAdministratorRoleName);
        var platformUserRole = roles.Value.FirstOrDefault(r => r.Name == RoleInfoConstants.PlatformUserRoleName);
        if (platformAdminRole == null || platformUserRole == null)
        {
            throw new Exception("Required roles not found in Role Service.");
        }

        //SystemRole platformAdminRole = await uow.SystemRoleRepository.GetByIdAsync(Domain.Constants.PlatformAdminRoleId, ct)
        //    ;

        //SystemRole platformUserRole = await uow.SystemRoleRepository.GetByIdAsync(Domain.Constants.PlatformUserRoleId, ct)
        //    ;

        UserPermission systemPermission = await uow.UserPermissionRepository.GetSystemPermissionsForUserAsync(user.Id, ct)
            ;

        UserPermissionNotFoundException.ThrowIfNull(systemPermission);

        if (systemPermission.SystemRoleId == platformAdminRole.Id)
        {
            throw new ArgumentException("Cannot reset a platform admin to a platform user");
        }

        systemPermission.SystemRoleId = platformUserRole.Id;
        user.AssignedProvider = null;
        await uow.SaveAsync(ct);
    }

    /// <summary>
    /// Clears every HybridCache entry held by this process.
    /// </summary>
    /// <remarks>
    /// This method invalidates all cached data in this Core API instance only.
    /// Caches outside HybridCache (OpenID Connect metadata, signing keys in authentication middleware)
    /// are unaffected, as are the caches of other processes such as the Gateway.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    public async Task ResetAllCachesAsync(CancellationToken ct)
    {
        // "*" is the wildcard tag of Microsoft.Extensions.Caching.Hybrid (pinned to 10.9.0; guarantee verified at 10.7.0):
        // it flips a global invalidation timestamp that DefaultHybridCache checks ahead of any per-entry tag, so
        // it evicts every entry - tagged or not - without enumerating producers. No entry can collide
        // with it because TagSet.Validate rejects "*" at write time. Scope is this process: the
        // Gateway's own HybridCache (JWKS, internal tokens, Vault keys, permissions) is unaffected.
        await cache.RemoveByTagAsync("*", ct);
    }
}