using FluentValidation;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.General;

public static class ValidatorExtensions
{
    public static IRuleBuilder<T, string> IsProjectShortName<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        IRuleBuilderOptions<T, string> options = ruleBuilder
            .NotEmpty().WithMessage("Project ShortName cannot be empty!")
            .MaximumLength(50).WithMessage("Project ShortName cannot be greater than 50 characters!")
            .Matches(@"^[a-zA-Z0-9_\-]+$").WithMessage("Project ShortName has wrong format!");

        return options;
    }
}