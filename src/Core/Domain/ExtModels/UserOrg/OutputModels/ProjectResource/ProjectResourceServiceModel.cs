using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectResourceAssignment;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectResource;

public class ProjectResourceServiceModel
{
    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public IList<AssociatedProjectModel>? AssociatedProjects { get; init; }
    public IList<ProjectResourceAssignmentServiceModel>? Assignments { get; init; }
}

public sealed record AssociatedProjectModel(Guid Id, string ProjectName, string ShortName, string CollectorName, DateTime? StartDate, DateTime? EndDate);