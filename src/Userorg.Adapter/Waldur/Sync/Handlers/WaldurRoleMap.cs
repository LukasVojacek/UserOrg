using Forge.Core.Features.Role.Services;

using Microsoft.Extensions.Logging;

using Userorg.Adapter.Waldur.Base;

using UserOrg.Shared;

namespace Userorg.Adapter.Waldur.Sync.Handlers;

/// <summary>
/// Shared membership role resolution for the B09 handlers (ADR-0057/A4). The payload
/// <c>Role</c> is already the MAPPED Userorg role (contracts D1/D13), so here we only guard against a
/// value outside the known project-role set — an unknown role falls back to <c>Member</c> plus a
/// structured warning, preserving the old <c>WaldurEfpExporter.HandleUnknownRole</c> behaviour.
/// </summary>
internal static class WaldurRoleMap
{
    private static readonly HashSet<string> KnownProjectRoles = new(StringComparer.Ordinal)
    {
        RoleInfoConstants.ProjectOwnerRoleName,
        RoleInfoConstants.ProjectManagerRoleName,
        RoleInfoConstants.ProjectMemberRoleName,
    };

    /// <summary>Returns the mapped role if known, else logs a warning and falls back to ProjectMember.</summary>
    internal static string ResolveProjectRoleName(
        string mappedRole,
        ILogger logger,
        string integrationName,
        string userName,
        string externalProjectId)
    {
        if (KnownProjectRoles.Contains(mappedRole))
        {
            return mappedRole;
        }

        logger.UnknownWaldurRole(integrationName, mappedRole, userName, externalProjectId);
        return RoleInfoConstants.ProjectMemberRoleName;
    }

    /// <summary>Resolves the persisted <c>SystemRoleId</c> for a role name; throws (not acked) if absent.</summary>
    internal static int RequireRoleId(IList<RoleInfo> roles, string roleName)
    {
        var role = roles.FirstOrDefault(r => r.Name == roleName)
            ?? throw new InvalidOperationException(
                $"System role '{roleName}' not found among {roles.Count} available roles.");
        return role.Id;
    }
}