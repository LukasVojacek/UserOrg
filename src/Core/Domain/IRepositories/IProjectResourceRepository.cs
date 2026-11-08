using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;
using UserOrganizationManagement.Domain.IRepositories.AbstractTypes;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.IRepositories;

public interface IProjectResourceRepository : IRepository<ProjectResource, Guid>
{
    Task<ProjectResource> GetByIdWithIncludesAsync(Guid? projectResourceId, CancellationToken ct);
    Task<ProjectResource?> GetByNameWithIncludesAsync(string projectResourceName, CancellationToken ct);
    Task<IList<ProjectResource>> GetAllBySpecificationsAsync(Guid? providerId, ProjectId? projectId, int? statusId, int? locationTypeId, DateTime? startDate, DateTime? endDate, CancellationToken ct);
    Task<IList<ProjectResource>> GetAllBySpecificationsAsync(Guid? providerId, ProjectShortName? projectShortName, int? statusId, int? locationTypeId, DateTime? startDate, DateTime? endDate, CancellationToken ct);
    Task<IList<ProjectResource>> GetAllForServiceAsync(CancellationToken ct);
    Task<IList<WeekCount>> CreatedFrom(DateOnly fromDate, DateOnly toDate, CancellationToken ct);
    Task<int> TotalCount(CancellationToken ct);
}