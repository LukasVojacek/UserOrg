using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;

public class ProjectModel
{
    public ProjectId? Id { get; set; }
    public string? Name { get; set; }
    public string? ShortName { get; set; }
    public string? Description { get; set; }
    public string? ContactEmail { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? DomainId { get; set; }
    public EOperationModeResponse OperationMode { get; set; }
    public IList<Guid>? AssignedProjectResourceIds { get; init; }
    public IList<Guid>? AssignedTags { get; init; }
}