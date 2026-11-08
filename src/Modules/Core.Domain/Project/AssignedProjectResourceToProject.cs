namespace Core.Domain.Project;

/// <summary>
/// AssignedProjectResourceToProject - junction/mapping entity.
/// All IDs (including implicit Id PK) are shadow properties managed by EF only.
/// Maps project resources to projects (many-to-many relationship).
/// </summary>
public class AssignedProjectResourceToProject : ISoftDeletableEntity
{
    // Shadow properties (managed by EF, not exposed in domain):
    // - Id (Guid, implicit PK)
    // - ProjectResourceId (Guid, FK)
    // - ProjectId (Guid, FK)

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public bool IsDeleted { get; private set; }

    /// <summary>References to related aggregates (via shadow properties) - INTERNAL EF only.</summary>
    internal ProjectResource? ProjectResource { get; private set; }
    internal Project? Project { get; private set; }

    /// <summary>Creates an active link; EF derives the foreign keys from the navigations set by the aggregate.</summary>
    public static AssignedProjectResourceToProject Create()
    {
        return new AssignedProjectResourceToProject
        {
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false,
        };
    }

    /// <summary>
    /// Creates a link bound to <paramref name="resource"/>; EF derives the ProjectResourceId shadow FK from the
    /// navigation. Used by <see cref="Project.AssignResource"/> — the M:N link is created only through the aggregate.
    /// </summary>
    internal static AssignedProjectResourceToProject Create(ProjectResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return new AssignedProjectResourceToProject
        {
            ProjectResource = resource,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false,
        };
    }

    /// <summary>Reactivates a previously soft-deleted link (re-add of a removed resource assignment).</summary>
    internal void Reactivate()
    {
        IsDeleted = false;
    }

    public void Delete()
    {
        IsDeleted = true;
    }
}