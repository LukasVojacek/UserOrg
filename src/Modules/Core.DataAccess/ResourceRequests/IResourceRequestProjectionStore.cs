namespace Core.DataAccess.ResourceRequests;

/// <summary>
/// Write port over the Core-owned resource-request projection (ADR-0063). The read side is
/// <see cref="Core.DataAccess.Queries.Contracts.IProjectResourceRequestQuery"/>; this is its write counterpart,
/// used by the assign-resource flow's step handlers to advance a row's state / token as the workflow progresses.
/// Lives in <c>Core.DataAccess</c> because it wraps the internal, schema-isolated
/// <see cref="ResourceRequestsDbContext"/>; consumers in the <c>Core</c> module depend only on this interface.
/// </summary>
public interface IResourceRequestProjectionStore
{
    /// <summary>Loads the projection row for <paramref name="slug"/> (untracked), or null when none exists.</summary>
    Task<ProjectResourceRequest?> GetBySlugAsync(string slug, CancellationToken ct);

    /// <summary>Inserts a new projection row with the slug as PK; duplicate slug is rejected by the store.</summary>
    Task InsertAsync(ProjectResourceRequest row, CancellationToken ct);

    /// <summary>Deletes the projection row for the slug if it exists; used to roll back on workflow-start failure.</summary>
    Task DeleteBySlugAsync(string slug, CancellationToken ct);

    /// <summary>
    /// Loads the tracked row for <paramref name="slug"/>, applies <paramref name="mutate"/>, stamps
    /// <see cref="ProjectResourceRequest.UpdatedAt"/> and persists. Throws when the row does not exist.
    /// </summary>
    Task UpdateBySlugAsync(string slug, Action<ProjectResourceRequest> mutate, CancellationToken ct);
}