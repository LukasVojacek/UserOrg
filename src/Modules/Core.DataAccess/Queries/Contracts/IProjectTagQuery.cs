namespace Core.DataAccess.Queries.Contracts;

public interface IProjectTagQuery
{
    Task<QueryPage<ProjectTagSummary>> GetProjectTagsAsync(ProjectTagFilter filter, int page, int limit, CancellationToken ct);
    Task<ProjectTagSummary?> GetProjectTagAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Resolves a single tag by its natural key (name, globally unique) or null when none matches (ADR-0022).
    /// </summary>
    Task<ProjectTagSummary?> GetProjectTagByNameAsync(TagName name, CancellationToken ct);

    /// <summary>
    /// True when an active (non-deleted) project is still assigned the named tag — the block-if-in-use gate
    /// for the catalogue delete (F1 G2).
    /// </summary>
    Task<bool> IsTagAssignedToAnyProjectAsync(TagName tagName, CancellationToken ct);
}