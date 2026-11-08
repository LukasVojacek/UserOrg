using UserOrg.Shared;

using UserOrganizationManagement.Domain.Contracts;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.UserPermission;
using UserOrganizationManagement.Domain.IRepositories.AbstractTypes;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.IRepositories;

public interface IProjectRepository : IRepository<Project, ProjectId>
{
    IQueryable<Project> Query();
    Task<ProjectResourceInfo?> GetByShortNameAndResourceNameAsync(ProjectShortName projectShortName, string resourceName, CancellationToken ct);
    Task<Project> GetByShortNameAsync(ProjectShortName projectShortName, CancellationToken ct);
    Task<Project?> GetByShortNameWithDetailsAsync(ProjectShortName projectShortName, CancellationToken ct);
    Task<Project?> GetProjectByShortNameWithCreatedUsersAsync(ProjectShortName projectShortName, CancellationToken ct);
    Task<List<Project>> GetProjectsContainsUserAsync(ProjectShortName? projectShortName, string idpUserId, CancellationToken ct);
    Task<IList<Project>> GetProjectsForProvider(Guid providerId, CancellationToken GetProjectsForProvider);
    Task<List<ProjectExtendedProjection>> GetProjectsContainsUserExtendedAsync(ProjectShortName? projectShortName, string idpUserId, CancellationToken ct);
    /// <summary>
    /// Retrieves projects filtered by various criteria, with optional provider scoping.
    /// </summary>
    /// <param name="shortName">Optional short name filter (case-insensitive LIKE match).</param>
    /// <param name="contactEmail">Optional contact email filter (substring match).</param>
    /// <param name="domainId">Optional domain ID filter.</param>
    /// <param name="startDate">Optional start date filter (inclusive, projects must start on or after this date).</param>
    /// <param name="endDate">Optional end date filter (inclusive, projects must end on or before this date).</param>
    /// <param name="providerId">Optional provider ID for scoping results to a provider's assigned resources.
    /// When <c>null</c>, no provider filter is applied (platform admin sees all projects).
    /// When set to a provider GUID, the result is the union of projects whose resource-assignment chain (via AssignedProjectResources → ProjectResource → AssignedLocations → Location) resolves to that provider and the caller's own projects (see <paramref name="callerIdpUserId"/>).
    /// Both link hops respect soft-delete: a soft-deleted AssignedProjectResourceToProject or AssignedLocation row will not keep the project in scope.
    /// The Location and ProjectResource entities themselves are not tested here.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="callerIdpUserId">The calling ProviderAdmin's identity-provider user id. When
    /// <paramref name="providerId"/> is set, a project the caller is assigned to or created is included even
    /// outside the provider's resource chain. Ignored when <paramref name="providerId"/> is <c>null</c>.</param>
    /// <returns>A list of projects matching all supplied filters.</returns>
    Task<IList<Project>> GetAllBySpecificationsAsync(string? shortName, string? contactEmail, int? domainId, DateTime? startDate, DateTime? endDate, Guid? providerId, CancellationToken ct, string? callerIdpUserId = null);
    Task<IList<IProjectHEAppELinks>> GetHeappeLinks(ProjectShortName projectShortName, CancellationToken ct);
    Task<HeappeInstance?> GetHeappeVerison(HeappeIdentifier heappeIdentifier, CancellationToken ct);
    Task<IList<ProjectAllocationInfo>> GetProjectResourceAllocationInfoByUserIdAndHeappeIdentifierAsync(Guid userId, string heappeIdentifier, CancellationToken ct);
    Task<IList<WeekCount>> CreatedFrom(DateOnly fromDate, DateOnly toDate, CancellationToken ct);
    Task<int> TotalCount(CancellationToken ct);
    Task<IList<(string ShortName, Guid Id, string ResourceName)>> GetAllWithResources(CancellationToken ct);
}