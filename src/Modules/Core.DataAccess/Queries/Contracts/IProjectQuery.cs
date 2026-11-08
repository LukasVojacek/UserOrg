namespace Core.DataAccess.Queries.Contracts;

public interface IProjectQuery
{
    Task<ProjectDetails?> GetProjectAsync(ProjectShortName shortName, CancellationToken ct);
    Task<QueryPage<ProjectSummary>> GetProjectsAsync(ProjectFilter filter, int page, int limit, CancellationToken ct);
    Task<QueryPage<ProjectUserSummary>> GetProjectUsersAsync(ProjectShortName shortName, int page, int limit, CancellationToken ct);
    Task<ExtendedProjectSummary?> GetExtendedProjectAsync(ProjectShortName shortName, CancellationToken ct);
    Task<List<ProjectHeappeLinkEntry>?> GetProjectHeappeLinksAsync(ProjectShortName shortName, CancellationToken ct);
    Task<List<DomainTypeName>> GetProjectDomainTypesAsync(CancellationToken ct);
}