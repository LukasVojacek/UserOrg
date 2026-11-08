using FluentValidation;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.General.Model;

public class LocationRequestModel
{
    public string? ProviderName { get; set; }
    public int? LocationTypeId { get; set; }

    public LocationRequestModel()
    {

    }

    public LocationRequestModel(string? providerName, int? locationTypeId)
    {
        ProviderName = providerName;
        LocationTypeId = locationTypeId;
    }
}
public class LocationRequestModelValidator : AbstractValidator<LocationRequestModel>
{
    public LocationRequestModelValidator()
    {
        RuleFor(x => x.LocationTypeId).GreaterThan(0);
        RuleFor(x => x.ProviderName).MaximumLength(100);
    }
}