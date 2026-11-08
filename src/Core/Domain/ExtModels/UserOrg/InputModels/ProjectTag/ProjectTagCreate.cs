using FluentValidation;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.ProjectTag;

public class ProjectTagCreate
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}

public class ProjectTagCreateValidator : AbstractValidator<ProjectTagCreate>
{
    public ProjectTagCreateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(150);
    }
}