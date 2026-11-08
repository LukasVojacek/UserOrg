using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectResourceAssignment;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectResource;

public class ProjectResourceModel
{
    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? PrimaryInvestigatorEmail { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public EOperationModeResponse OperationMode { get; set; }
    public IList<ProjectResourceAssignmentModel>? Assignments { get; init; }
}