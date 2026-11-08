using FluentValidation;

using UserOrganizationManagement.Domain.ExtModels.UserOrg.General;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Project;

public class ProjectCreate
{
    public string? Name { get; set; }
    public string? ShortName { get; set; }
    public string? Description { get; set; }
    public string? ContactEmail { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int? DomainId { get; set; }
}

public class ProjectCreateValidator : AbstractValidator<ProjectCreate>
{
    public ProjectCreateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ShortName).IsProjectShortName();
        RuleFor(x => x.Description).MaximumLength(150);
        RuleFor(x => x.ContactEmail).EmailAddress().MaximumLength(100);
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.EndDate).NotEmpty().GreaterThan(x => x.StartDate);
        RuleFor(x => x.DomainId).NotEmpty().GreaterThan(0);
    }
}