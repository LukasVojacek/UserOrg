using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.ProjectResourceAssignment;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectResourceAssignment;

namespace UserOrganizationManagement.Application.UserOrg.IServices;

public interface IProjectResourceAssignmentService
{
    Task<IList<ProjectResourceAssignmentModel>> GetProjectResourceAssignmentsAsync(Guid? providerId, Guid? projectResourceId, CancellationToken ct = default);
    Task<ProjectResourceAssignmentModel> GetProjectResourceAssignmentAsync(Guid? providerId, Guid id, CancellationToken ct = default);
    Task<Guid> InsertProjectResourceAssignmentAsync(ProjectResourceAssignmentCreate obj, CancellationToken ct = default);
    Task UpdateProjectResourceAssignmentAsync(ProjectResourceAssignmentUpdate obj, CancellationToken ct = default);
    Task DeleteProjectResourceAssignmentAsync(Guid id, CancellationToken ct = default);
}