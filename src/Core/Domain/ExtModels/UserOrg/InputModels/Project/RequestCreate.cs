using FluentValidation;

using UserOrganizationManagement.Domain.Enums;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.General;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Project;

public record RequestCreate(ProjectShortName ProjectShortName, ERequestType RequestType, string? ProjectResourceName, Guid? ProviderId, string? ContactEmail);

public class RequestCreateValidator : AbstractValidator<RequestCreate>
{
    public RequestCreateValidator()
    {
        RuleFor(x => x.ProjectShortName.Value).NotNull().NotEmpty().IsProjectShortName();
        RuleFor(x => x.RequestType).IsInEnum();

        RuleFor(x => x.ProjectResourceName).NotNull().When(x => x.RequestType is ERequestType.ProjectResourceAssignment);
        RuleFor(x => x.ProviderId).NotNull().When(x => x.RequestType is ERequestType.ProjectResourceAssignment);
        RuleFor(x => x.ContactEmail).NotNull().When(x => x.RequestType is ERequestType.ProjectResourceAssignment);
    }
}