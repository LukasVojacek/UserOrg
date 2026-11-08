using Core.Domain.Provider;
using Core.Domain.Shared;

using DomainProvider = Core.Domain.Provider.Provider;
using DomainProviderName = Core.Domain.Provider.ProviderName;
using DomainLocationName = Core.Domain.Provider.LocationName;

namespace Core.UnitTests.Providers;

public sealed class ProviderDomainTests
{
    [Fact]
    public void Should_SetCountryId_When_ChangeCountry()
    {
        var provider = DomainProvider.Create(DomainProviderName.From("acme"), countryId: 1);

        Assert.True(provider.ChangeCountry(42).IsSuccess);

        Assert.Equal(42, provider.CountryId);
    }

    [Fact]
    public void Should_RejectEdits_When_ProviderReadOnly()
    {
        var provider = DomainProvider.Create(
            DomainProviderName.From("acme"), countryId: 1, mode: EOperationMode.ReadOnly);

        var details = provider.UpdateDetails(null, null);
        var privacy = provider.SetPrivacy(true);
        var country = provider.ChangeCountry(42);

        Assert.False(details.IsSuccess);
        Assert.Equal(ProviderErrorCodes.ProviderReadOnly, details.Error.Code);
        Assert.False(privacy.IsSuccess);
        Assert.Equal(ProviderErrorCodes.ProviderReadOnly, privacy.Error.Code);
        Assert.False(country.IsSuccess);
        Assert.Equal(ProviderErrorCodes.ProviderReadOnly, country.Error.Code);
        Assert.Equal(1, provider.CountryId);
    }

    [Fact]
    public void Should_CarryStableCode_OnEachProviderError()
    {
        var name = DomainProviderName.From("acme");
        var location = DomainLocationName.From("gpu");

        Assert.Equal(ProviderErrorCodes.AlreadyDeactivated, ProviderErrors.AlreadyDeactivated(name).Code);
        Assert.Equal(ProviderErrorCodes.NotDeactivated, ProviderErrors.NotDeactivated(name).Code);
        Assert.Equal(ProviderErrorCodes.NameAlreadyExists, ProviderErrors.NameAlreadyExists(name).Code);
        Assert.Equal(ProviderErrorCodes.LocationAlreadyExists, ProviderErrors.LocationAlreadyExists(location, name).Code);
        Assert.Equal(ProviderErrorCodes.LocationNotFound, ProviderErrors.LocationNotFound(location, name).Code);
        Assert.Equal(ProviderErrorCodes.LocationAlreadyDeleted, ProviderErrors.LocationAlreadyDeleted(location).Code);
        Assert.Equal(ProviderErrorCodes.AssignedLocationNotFound, ProviderErrors.AssignedLocationNotFound(location).Code);
        Assert.Equal(ProviderErrorCodes.AssignedLocationAlreadyDeleted, ProviderErrors.AssignedLocationAlreadyDeleted(location).Code);
        Assert.Equal(ProviderErrorCodes.ProviderReadOnly, ProviderErrors.ProviderIsReadOnly(name).Code);
        Assert.Equal(ProviderErrorCodes.LocationReadOnly, ProviderErrors.LocationReadOnly(location).Code);
        Assert.Equal(ProviderErrorCodes.AssignmentReadOnly, ProviderErrors.AssignmentReadOnly(location).Code);
        Assert.Equal(ProviderErrorCodes.ResourceAlreadyHasNonSharedLocation, ProviderErrors.ResourceAlreadyHasNonSharedLocation(ResourceName.From("res")).Code);
        Assert.Equal(ProviderErrorCodes.NonSharedLocationAlreadyHasResource, ProviderErrors.NonSharedLocationAlreadyHasResource(location).Code);
        Assert.Equal(ProviderErrorCodes.ResourceNotUnderProvider, ProviderErrors.ResourceNotUnderProvider(ResourceName.From("res"), name).Code);
    }

    [Fact]
    public void Should_RejectDuplicateLocationOfDifferentCasing()
    {
        var provider = DomainProvider.Create(DomainProviderName.From("acme"), countryId: 1);
        Assert.True(provider.AddLocation(DomainLocationName.From("GPU"), "https://gpu", ELocationType.HPC).IsSuccess);

        // Canonicalization collapses casing, so "gpu" is a duplicate of "GPU".
        var second = provider.AddLocation(DomainLocationName.From("gpu"), "https://gpu2", ELocationType.CLOUD);

        Assert.False(second.IsSuccess);
        Assert.Equal(ProviderErrorCodes.LocationAlreadyExists, second.Error.Code);
    }

    [Fact]
    public void Should_KeepRequestedLocationType_When_ProviderAddsLocation()
    {
        var provider = DomainProvider.Create(DomainProviderName.From("acme"), countryId: 1);

        Assert.True(provider.AddLocation(DomainLocationName.From("gpu"), "https://gpu", ELocationType.Storage).IsSuccess);

        Assert.Equal(ELocationType.Storage, provider.Locations.Single().LocationType);
    }

    [Theory]
    [InlineData(ELocationType.HPC)]
    [InlineData(ELocationType.Quantum)]
    public void Should_KeepRequestedLocationType_When_LocationCreated(ELocationType locationType)
    {
        var location = Location.Create(DomainLocationName.From("gpu"), "https://gpu", locationType);

        Assert.Equal(locationType, location.LocationType);
    }

    [Theory]
    [InlineData(ELocationType.Unspecified)]
    [InlineData((ELocationType)999)]
    public void Should_Throw_When_LocationCreatedWithUndefinedType(ELocationType locationType)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Location.Create(DomainLocationName.From("gpu"), "https://gpu", locationType));

        Assert.Equal("locationType", exception.ParamName);
    }
}
