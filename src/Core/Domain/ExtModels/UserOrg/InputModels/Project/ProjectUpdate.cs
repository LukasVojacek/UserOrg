using FluentValidation;

using UserOrganizationManagement.Domain.ExtModels.UserOrg.General;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Project;

public class ProjectUpdate
{
    public string? Name { get; set; }
    public ProjectShortName ShortName { get; set; }
    public string? Description { get; set; }
    public string? ContactEmail { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int? DomainId { get; set; }
}
public class ProjectUpdateValidator : AbstractValidator<ProjectUpdate>
{
    public ProjectUpdateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => (string)x.ShortName).IsProjectShortName();
        RuleFor(x => x.Description).MaximumLength(150);
        RuleFor(x => x.ContactEmail).EmailAddress().MaximumLength(100);
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.EndDate).NotEmpty().GreaterThan(x => x.StartDate);
        RuleFor(x => x.DomainId).NotEmpty().GreaterThan(0);
    }
}