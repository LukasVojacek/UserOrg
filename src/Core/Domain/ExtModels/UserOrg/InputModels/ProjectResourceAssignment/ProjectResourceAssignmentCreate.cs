using FluentValidation;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.ProjectResourceAssignment;

public class ProjectResourceAssignmentCreate
{
    public Guid? ProjectResourceId { get; set; }
    public Guid? LocationId { get; set; }
    public long? AllocationAmount { get; set; }
    public int? AllocationUnitTypeId { get; set; }
    public string? AggregationName { get; set; }
    public IList<KeyValuePair<string, string>>? Specifications { get; init; }
}

public class ProjectResourceAssignmentCreateValidator : AbstractValidator<ProjectResourceAssignmentCreate>
{
    public ProjectResourceAssignmentCreateValidator()
    {
        RuleFor(x => x.ProjectResourceId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.AllocationUnitTypeId).NotEmpty().GreaterThan(0);
        //Unique check for specifications
    }
}