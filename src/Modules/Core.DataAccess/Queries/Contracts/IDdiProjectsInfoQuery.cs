namespace Core.DataAccess.Queries.Contracts;

/// <summary>
/// Reads every active project-resource grouped by its owning provider, projected with associated projects
/// and location assignments — the DDI "ProjectsInfo" aggregation (legacy
/// <c>ProjectResourceService.GetAllProjectResourceWithDetailsAsync</c>). Consumed by the DDI module endpoint.
/// </summary>
public interface IDdiProjectsInfoQuery
{
    /// <summary>
    /// Active resources (<c>EndDate &gt;= today</c>) grouped by their owning provider (ADR-0031 shadow FK
    /// <c>ProviderId</c>; null providers grouped under the empty-provider key), each projected with its active
    /// associated-project links and its active location assignments.
    /// </summary>
    Task<IReadOnlyList<ProviderResourcesView>> GetProjectsInfoAsync(CancellationToken ct);
}