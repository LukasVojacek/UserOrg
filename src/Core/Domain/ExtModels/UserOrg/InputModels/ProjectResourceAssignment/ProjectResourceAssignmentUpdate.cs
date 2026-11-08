using FluentValidation;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.ProjectResourceAssignment;

public class ProjectResourceAssignmentUpdate
{
    public Guid? Id { get; set; }
    public long? AllocationAmount { get; set; }
    public int? AllocationUnitTypeId { get; set; }
    public string? AggregationName { get; set; }

    public IList<KeyValuePair<string, string>>? Specifications { get; init; }
    public string? Description { get; set; }
}

public class ProjectResourceAssignmentUpdateValidator : AbstractValidator<ProjectResourceAssignmentUpdate>
{
    public ProjectResourceAssignmentUpdateValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.AllocationUnitTypeId).NotEmpty().GreaterThan(0);
        //Unique check for specifications
    }
}