using FluentValidation;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.SystemRoleManagment;

public class UpdateSystemRoleRequest
{
    public string? SystemRoleName { get; set; }
    public string? Description { get; set; }
    public IList<string>? NewSystemPermissionTypes { get; init; }
}

public class UpdateRoleRequestValidator : AbstractValidator<UpdateSystemRoleRequest>
{
    public UpdateRoleRequestValidator()
    {
        RuleFor(x => x.SystemRoleName)
            .NotEmpty()
            .MaximumLength(50);

        RuleFor(x => x.Description)
            .NotNull()
            .NotEmpty()
            .MaximumLength(150);

        RuleFor(x => x.NewSystemPermissionTypes)
            .NotNull()
            .NotEmpty();

    }
}