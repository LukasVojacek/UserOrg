using FluentValidation;

using UserOrganizationManagement.Domain.Authorization;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.General;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.UserPermission;

public class AssignRoleToUser : IProjectRequest
{
    public ProjectShortName ProjectShortName { get; set; }
    public string? RoleName { get; set; }
    public string? IdpUserId { get; set; }
}

public class AssignRoleToUserValidator : AbstractValidator<AssignRoleToUser>
{
    public AssignRoleToUserValidator()
    {
        RuleFor(x => x.RoleName)
            .NotEmpty()
            .MaximumLength(50);

        RuleFor(x => (string)x.ProjectShortName).IsProjectShortName();
        RuleFor(x => x.IdpUserId).NotNull().NotEmpty();

    }
}