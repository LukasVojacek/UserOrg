namespace Forge.Core.Features.Role.Persistence;

internal class Role
{
    internal int Id { get; init; }

    /// <summary>
    /// Gets or sets the unique name of the role.unique role name, e.g. "org-admin", "project-manager"
    /// </summary>
    /// <remarks>The role name is used to identify roles such as "org-admin" or "project-manager" within the
    /// system. The value should be unique among all roles.</remarks>
    internal string Name { get; set; } = string.Empty;

    internal string? Description { get; set; }

    internal ICollection<Permission.Persistence.Permission> RolePermissions { get; set; } = [];

    internal bool IsActive { get; set; } = true;

    // Role level for hierarchy, default is 1
    internal int Level { get; set; } = 1;
    internal string Category { get; set; } = string.Empty;
}