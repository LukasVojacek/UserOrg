using Microsoft.Extensions.Configuration;

using UserOrg.Shared;

namespace Forge.Core.Features.UserOrgRoles;

public static class UserPermissionDtoExtensions
{
    extension(IList<UserPermissionDto> userPermissions)
    {
        /// <summary>
        /// Converts a list of UserPermissionDto objects into a dictionary where the key is the system role name and the value is a list of project short names associated with that role. It also handles default role names for users and administrators based on configuration settings.
        /// </summary>
        /// <param name="configuration"></param>
        /// <returns></returns>
        public IDictionary<string, ICollection<string>> ToProjectRecords(IConfiguration configuration, out string roleClaim)
        {
            var defaultUserRoleName = configuration["DefaultUserRoleName"] ?? Environment.GetEnvironmentVariable("DefaultUserRoleName") ?? "User";
            var defaultAdminRoleName = configuration["DefaultAdminRoleName"] ?? Environment.GetEnvironmentVariable("DefaultAdminRoleName") ?? "Admin";
            roleClaim = defaultUserRoleName;

            var projectRecords = new Dictionary<string, ICollection<string>>();
            foreach (IGrouping<string, UserPermissionDto> roleProjects in userPermissions.GroupBy(x => x.SystemRoleName))
            {
                if (roleProjects.Key is RoleInfoConstants.PlatformAdministratorRoleName)
                {
                    roleClaim = roleProjects?.FirstOrDefault()?.SystemRoleName ?? defaultUserRoleName;
                    continue;
                }
                if (roleProjects.Key is RoleInfoConstants.ProviderAdministratorRoleName)
                {
                    roleClaim = roleProjects?.FirstOrDefault()?.SystemRoleName ?? defaultUserRoleName;
                    continue;
                }
                // Filter out null/empty short names: global/system roles have ProjectId = null,
                // which becomes ShortName = "" after the null-coalesce. Including empty short names
                // would cause GetByShortNameAsync to return null and throw ProjectNotFoundException
                // in the background cache warmer (ProjectTemplateInfo).
                var roleProjectsSelect = roleProjects
                    ?.Select(p => p.ShortName ?? string.Empty)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToHashSet() ?? [];
                if (roleProjectsSelect.Count == 0)
                {
                    continue;
                }
                projectRecords.TryAdd(roleProjects!.Key, roleProjectsSelect);
            }
            return projectRecords;
        }

    }
}