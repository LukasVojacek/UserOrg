using FluentValidation;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.ProjectResource;

public class ProjectResourceUpdate
{
    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? PrimaryInvestigatorEmail { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public class ProjectResourceUpdateValidator : AbstractValidator<ProjectResourceUpdate>
{
    public ProjectResourceUpdateValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(150);
        RuleFor(x => x.PrimaryInvestigatorEmail).NotEmpty().EmailAddress().MaximumLength(100);
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.EndDate).NotEmpty().GreaterThan(x => x.StartDate);
    }
}