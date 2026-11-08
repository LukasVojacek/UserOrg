using Microsoft.AspNetCore.Http;

using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.ProjectResource;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectResource;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Service;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Application.UserOrg.IServices;

public interface IProjectResourceService
{
    Task<IList<ProjectResourceModel>> GetProjectResourcesAsync(Guid? providerId, ProjectId? projectId, int? statusId, int? locationTypeId, DateTime? startDate, DateTime? endDate, CancellationToken ct = default);
    Task<IList<ProjectResourceModel>> GetProjectResourcesAsync(Guid? providerId, ProjectShortName? projectShortName, int? statusId, int? locationTypeId, DateTime? startDate, DateTime? endDate, CancellationToken ct = default);
    Task<IList<ProjectResourceModel>> GetProjectResourcesForUserAsync(string userSid, ProjectShortName? projectShortName, int? statusId, int? locationTypeId, DateTime? startDate, DateTime? endDate, CancellationToken ct = default);
    Task<ProjectResourceModel> GetProjectResourceAsync(Guid? providerId, Guid id, CancellationToken ct = default);
    Task<ProjectResourceModel> GetProjectResourceByNameAsync(string resourceName, CancellationToken ct = default);
    Task<IList<ProjectInfoModel>> GetAllProjectResourceWithDetailsAsync(CancellationToken ct = default);
    Task<Guid> InsertProjectResourceAsync(ProjectResourceCreate obj, HostString baseUri, CancellationToken ct = default);
    Task UpdateProjectResourceAsync(ProjectResourceUpdate obj, CancellationToken ct = default);
    Task DeleteProjectResourceAsync(Guid userId, Guid id, CancellationToken ct = default);
}