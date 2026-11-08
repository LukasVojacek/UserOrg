using FluentValidation;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.General.Model;

public class ProjectRequestModel
{
    public string? ShortName { get; set; }
    public string? ContactEmail { get; set; }
    public int? DomainId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public ProjectRequestModel()
    {

    }

    public ProjectRequestModel(string? shortName, string? contactEmail, int? domainId, DateTime? startDate, DateTime? endDate)
    {
        ShortName = shortName;
        ContactEmail = contactEmail;
        DomainId = domainId;
        StartDate = startDate;
        EndDate = endDate;
    }
}

public class ProjectRequestModelValidator : AbstractValidator<ProjectRequestModel>
{
    public ProjectRequestModelValidator()
    {
        RuleFor(x => x.ShortName).IsProjectShortName();
        RuleFor(x => x.ContactEmail).EmailAddress().MaximumLength(100);
        RuleFor(x => x.EndDate).GreaterThan(x => x.StartDate).When(w => w.StartDate.HasValue && w.EndDate.HasValue);
        RuleFor(x => x.DomainId).GreaterThan(0);
    }
}