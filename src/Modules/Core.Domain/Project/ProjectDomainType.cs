using Core.Domain.Shared;

namespace Core.Domain.Project;

/// <summary>
/// ProjectDomainType - Reference/Lookup table.
/// Domain types for projects.
/// </summary>
public class ProjectDomainType : IEntity
{
    // Matches the deployed nvarchar(150) column (ADR-0049 zero-diff overlay) — narrower than the shared
    // Description VO's own 500-char limit, so this entity enforces its own tighter bound at construction
    // time instead of letting an over-length value fail as an unhandled SqlException on save.
    private const int MaxDescriptionLength = 150;

    internal ProjectDomainTypeId Id { get; private set; }
    public DomainTypeName Name { get; private set; }
    public Description? Description { get; private set; }

    public static ProjectDomainType Create(DomainTypeName domainTypeName, Description description)
    {
        if (description.Value.Length > MaxDescriptionLength)
        {
            throw new ArgumentException(
                $"Description must not exceed {MaxDescriptionLength} characters", nameof(description));
        }

        return new ProjectDomainType
        {
            Id = new ProjectDomainTypeId(),
            Name = domainTypeName,
            Description = description,
        };
    }

    /// <summary>
    /// Creates a ProjectDomainType instance with an invalid ID, used for reference purposes only
    /// (e.g., when referencing by name without persistence).
    /// </summary>
    public static ProjectDomainType FromName(DomainTypeName domainTypeName) =>
        new()
        {
            Id = new ProjectDomainTypeId(-1),
            Name = domainTypeName,
        };
}