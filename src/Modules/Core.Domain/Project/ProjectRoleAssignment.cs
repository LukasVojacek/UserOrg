namespace Core.Domain.Project;

/// <summary>
/// The inputs a relative role-hierarchy decision needs (see <see cref="Project.ChangeUserRole"/>):
/// the role being assigned with its level, the requester's level, the project-member baseline level, and
/// the owner role id. Levels are supplied by the caller (resolved from the role service) because the
/// aggregate is EF-free and the rule is relative — the Gateway's permission gate cannot express it.
/// </summary>
public readonly record struct ProjectRoleAssignment(
    ProjectRoleId RoleId,
    int RoleLevel,
    int RequesterLevel,
    int MemberBaselineLevel,
    ProjectRoleId OwnerRoleId);