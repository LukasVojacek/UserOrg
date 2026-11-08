using FluentValidation;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.General.Model;

public class CountryRequestModel
{
    public string? CountryCode { get; set; }

    public CountryRequestModel()
    {

    }

    public CountryRequestModel(string? countryCode)
    {
        CountryCode = countryCode;
    }
}

public class CountryRequestModelValidator : AbstractValidator<CountryRequestModel>
{
    public CountryRequestModelValidator()
    {
        RuleFor(x => x.CountryCode).MaximumLength(3);
    }
}