namespace Core.Domain.Project;

/// <summary>
/// AssignedUserToProject - join entity representing the assignment of a User to a Project.
///
/// Both User and Project are aggregate roots; this entity is owned by the Project aggregate.
/// Navigation properties are EF-only — domain code uses the typed IDs (shadow FKs ProjectId/UserId
/// are managed by EF via the configured relationships).
/// </summary>
public sealed class AssignedUserToProject : BaseAuditableEntity, ISoftDeletableEntity
{
    /// <summary>Navigation property for EF relationships ONLY - do not mutate in domain code.</summary>
    internal User.User User { get; private set; } = default!;
    internal Project? Project { get; private set; }
    internal User.User AssignedByUser { get; private set; } = default!;

    public UserId UserId { get; private set; }
    public ProjectId ProjectId { get; private set; }
    public UserId AssignedByUserId { get; private set; }

    public bool IsDeleted { get; private set; }

    internal static AssignedUserToProject Create(User.User user, UserId assignedBy) =>
        new()
        {
            User = user,
            AssignedByUserId = assignedBy,
            IsDeleted = false,
        };

    internal void Reactivate(UserId assignedBy)
    {
        IsDeleted = false;
        AssignedByUserId = assignedBy;
    }

    internal void Remove(UserId unassignedBy)
    {
        IsDeleted = true;
        AssignedByUserId = unassignedBy;
    }
}