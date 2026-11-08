using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.ProjectTag;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectTag;

namespace UserOrganizationManagement.Application.UserOrg.IServices;

public interface IProjectTagService
{
    Task<IList<ProjectTagModel>> GetProjectTagsAsync(string? tagName, CancellationToken ct);
    Task<ProjectTagModel> GetProjectTagAsync(Guid id, CancellationToken ct);
    Task<Guid> InsertProjectTagAsync(ProjectTagCreate obj, Guid createdByUserId, CancellationToken ct);
    Task UpdateProjectTagAsync(ProjectTagUpdate obj, CancellationToken ct);
    Task DeleteProjectTagAsync(Guid id, CancellationToken ct);
}