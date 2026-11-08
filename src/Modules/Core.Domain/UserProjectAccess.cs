namespace Core.Domain;

/// <summary>
/// UserProjectAccess - Access control mapping entity representing which users have access to which projects.
///
/// IMPORTANT DDD Pattern:
/// - User and Project are separate aggregate roots
/// - This entity manages access relationships but doesn't own either aggregate
/// - Navigation properties are INTERNAL to EF only
/// - Domain code should NOT access aggregates via these properties
/// - Instead, use the shadow properties: UserId, ProjectId
/// </summary>
public class UserProjectAccess : ISoftDeletableEntity
{
    // Shadow properties (managed by EF, not exposed in domain):
    // - Id (Guid, PK)
    // - UserId (Guid, FK) - Reference to User aggregate
    // - ProjectId (Guid, FK) - Reference to Project aggregate
    // - ProjectResourceId (Guid, nullable FK)
    // - AssignedByUserId (Guid, nullable FK)

    public bool HasAccess { get; private set; } = true;
    public string ServiceName { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public bool IsDeleted { get; private set; }

    /// <summary>
    /// Navigation properties for EF relationships ONLY - do not use in domain code.
    /// Use shadow properties (UserId, ProjectId) instead to reference aggregates.
    /// </summary>
    internal User.User? User { get; private set; }
    internal Project.Project? Project { get; private set; }
    internal ProjectResource? ProjectResource { get; private set; }

    public static UserProjectAccess Create(bool hasAccess = true, string serviceName = "LEXIS")
    {
        return new UserProjectAccess
        {
            HasAccess = hasAccess,
            ServiceName = serviceName,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false,
        };
    }

    public void GrantAccess()
    {
        HasAccess = true;
    }

    public void RevokeAccess()
    {
        HasAccess = false;
    }

    public void Delete()
    {
        IsDeleted = true;
    }
}