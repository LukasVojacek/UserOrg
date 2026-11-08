namespace Core.Domain.User;

public sealed class UserPermission : ISoftDeletableEntity
{
    public ProjectId? ProjectId { get; private set; }
    public UserId UserId { get; private set; }
    public ProjectRoleId SystemRoleId { get; private set; }

    public bool IsDeleted { get; private set; }

    /// <summary>
    /// Navigation to the owning <see cref="User"/> (the <c>UserId</c> FK relationship). EF-only; set on
    /// project-role creation so the Project aggregate can match a row by reference (<c>r.User == target</c>)
    /// and EF resolves the <c>UserId</c> FK from it. Mirrors <c>AssignedUserToProject.User</c>.
    /// </summary>
    internal User User { get; private set; } = default!;

    internal static UserPermission CreateSystemPermission(ProjectRoleId systemRoleId) => new()
    {
        ProjectId = null,
        SystemRoleId = systemRoleId
        // UserId is set by EF via the navigation relationship when saving
    };

    /// <summary>
    /// Creates a project-scoped role row (the project role) owned by the Project aggregate's
    /// <c>_memberRoles</c>. EF resolves the <c>UserId</c> FK from <paramref name="user"/> and the
    /// <c>ProjectId</c> FK from the parent collection.
    /// </summary>
    internal static UserPermission CreateProjectRole(User user, ProjectRoleId roleId) => new()
    {
        User = user,
        SystemRoleId = roleId
    };

    internal void ChangeRole(ProjectRoleId roleId) => SystemRoleId = roleId;

    internal void Reactivate(ProjectRoleId roleId)
    {
        IsDeleted = false;
        SystemRoleId = roleId;
    }

    internal void SoftDelete() => IsDeleted = true;
}