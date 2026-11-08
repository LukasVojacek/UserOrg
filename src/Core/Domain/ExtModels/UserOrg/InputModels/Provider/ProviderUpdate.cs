using FluentValidation;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Provider;

public class ProviderUpdate
{
    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool IsPrivate { get; set; }
    public string? CountryCode { get; set; }
    public string? SupportEmail { get; set; }
}
public class ProviderUpdateValidator : AbstractValidator<ProviderUpdate>
{
    public ProviderUpdateValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(150);
        RuleFor(x => x.CountryCode).NotEmpty().MaximumLength(3);
    }
}