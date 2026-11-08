using Forge.Core.Features.Role.Services;

using UserOrg.Shared;

namespace UserOrganizationManagement.API.API;

/// <summary>
/// Determines the set of roles a user is permitted to assign to others within a project,
/// based on the role hierarchy. A user may only assign roles that are strictly lower in level
/// than their own highest role, and no lower than the project member baseline.
/// </summary>
internal static class ValidRolesFilter
{
    /// <summary>
    /// Returns all roles from <paramref name="allRoles"/> that the current user is permitted
    /// to assign to other project members.
    /// </summary>
    /// <param name="allRoles">The full list of system roles.</param>
    /// <param name="userRoleNamesForProject">Role names held by the current user in the project.</param>
    /// <param name="memberRoleName">The name of the baseline Member role (defines the minimum assignable level).</param>
    /// <returns>
    /// Roles satisfying both conditions:
    /// <list type="bullet">
    /// <item><description>Level is strictly lower than the user's highest role level.</description></item>
    /// <item><description>Level is at or above the Member baseline (matches service-side validation).</description></item>
    /// </list>
    /// Returns an empty sequence when the user has no role in the project.
    /// </returns>
    internal static IEnumerable<RoleInfo> GetValidRoles(
        IList<RoleInfo> allRoles,
        IEnumerable<string> userRoleNamesForProject,
        string memberRoleName)
    {
        var userRoles = allRoles
            .Where(r => userRoleNamesForProject.Contains(r.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (userRoles.Count == 0)
        {
            return [];
        }

        var highestUserRole = userRoles.MaxBy(r => r.Level)!;
        var memberRole = allRoles.FirstOrDefault(r => r.Name.Equals(memberRoleName, StringComparison.OrdinalIgnoreCase));

        return allRoles.Where(r =>
            highestUserRole.IsHigherLevelThan(r)
            && (memberRole is null || !memberRole.IsHigherLevelThan(r)));
    }
}