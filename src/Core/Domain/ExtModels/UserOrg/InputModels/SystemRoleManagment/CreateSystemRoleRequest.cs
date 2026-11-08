using FluentValidation;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.SystemRoleManagment;

public class CreateSystemRoleRequest
{
    public string? SystemRoleName { get; set; }
    public string? BaseSystemRoleName { get; set; }
    public string? Description { get; set; }
    public IList<string>? SystemPermissionTypes { get; init; }
}

public class CreateRoleRequestValidator : AbstractValidator<CreateSystemRoleRequest>
{
    public CreateRoleRequestValidator()
    {
        RuleFor(x => x.SystemRoleName)
            .NotEmpty()
            .MaximumLength(50);

        RuleFor(x => x.BaseSystemRoleName)
            .NotEmpty()
            .MaximumLength(50);

        RuleFor(x => x.Description)
            .NotNull()
            .NotEmpty()
            .MaximumLength(150);

        RuleFor(x => x.SystemPermissionTypes)
            .NotNull()
            .NotEmpty();

    }
}