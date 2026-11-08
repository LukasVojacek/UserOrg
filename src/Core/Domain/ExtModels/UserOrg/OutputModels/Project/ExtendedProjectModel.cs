using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectResourceAssignment;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;

public class ExtendedProjectModel
{
    public string? Name { get; set; }
    public string? ShortName { get; set; }
    public string? Description { get; set; }
    public string? ContactEmail { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? DomainId { get; set; }
    public IList<ProjectResourceAssignmentExtendedModel>? AssignedProjectResources { get; init; }

    public IList<Guid>? AssignedTags { get; init; }
}