using FluentValidation;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Location;

public class LocationUpdate
{
    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool IsPrivate { get; set; }
    public string? Endpoint { get; set; }
    public Guid? ProviderId { get; set; }
    public int? LocationTypeId { get; set; }
    public bool IsShared { get; set; }
#pragma warning disable CA1056 // URI properties for API model should be strings
    public string? DefaultServiceIntegrationUrl { get; set; }
#pragma warning restore CA1056
}

public class LocationUpdateValidator : AbstractValidator<LocationUpdate>
{
    public LocationUpdateValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(150);
        RuleFor(x => x.Endpoint).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ProviderId).NotEmpty();
        RuleFor(x => x.LocationTypeId).NotEmpty();

        RuleFor(x => x.DefaultServiceIntegrationUrl)
            .MaximumLength(100)
            .When(x => !string.IsNullOrWhiteSpace(x.DefaultServiceIntegrationUrl));
    }
}