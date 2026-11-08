using Core.Domain.Provider;
using Core.Domain.Shared;

using DomainLocationName = Core.Domain.Provider.LocationName;
using DomainProvider = Core.Domain.Provider.Provider;
using DomainProviderName = Core.Domain.Provider.ProviderName;

namespace Core.UnitTests.Providers;

public sealed class ProviderUpdateLocationTests
{
    private static readonly DomainLocationName Gpu = DomainLocationName.From("gpu");

    private static DomainProvider ProviderWithGpu()
    {
        var provider = DomainProvider.Create(DomainProviderName.From("acme"), countryId: 1);
        Assert.True(provider.AddLocation(Gpu, "https://old", ELocationType.HPC, description: "old").IsSuccess);
        return provider;
    }

    [Fact]
    public void Should_UpdateEndpointAndDescription_When_LocationIsActive()
    {
        var provider = ProviderWithGpu();

        var result = provider.UpdateLocation(Gpu, "https://new", "new");

        Assert.True(result.IsSuccess);
        var location = Assert.Single(provider.Locations);
        Assert.Equal("https://new", location.Endpoint);
        Assert.Equal("new", location.Description);
    }

    [Fact]
    public void Should_ClearTheDescription_When_DescriptionIsNull()
    {
        var provider = ProviderWithGpu();

        Assert.True(provider.UpdateLocation(Gpu, "https://new", null).IsSuccess);

        Assert.Null(Assert.Single(provider.Locations).Description);
    }

    [Fact]
    public void Should_FailWithLocationNotFound_When_NameIsUnknown()
    {
        var provider = ProviderWithGpu();

        var result = provider.UpdateLocation(DomainLocationName.From("cpu"), "https://new", "new");

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderErrorCodes.LocationNotFound, result.Error.Code);
        Assert.Equal("https://old", Assert.Single(provider.Locations).Endpoint);
    }

    [Fact]
    public void Should_FailWithAlreadyDeleted_When_LocationIsDeleted()
    {
        var provider = ProviderWithGpu();
        Assert.True(provider.RemoveLocation(Gpu).IsSuccess);

        var result = provider.UpdateLocation(Gpu, "https://new", "new");

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderErrorCodes.LocationAlreadyDeleted, result.Error.Code);
        Assert.Equal("https://old", Assert.Single(provider.Locations).Endpoint);
    }

    [Fact]
    public void Should_ThrowAndKeepTheEndpoint_When_EndpointIsBlank()
    {
        var provider = ProviderWithGpu();

        Assert.Throws<ArgumentException>(() => provider.UpdateLocation(Gpu, " ", "new"));

        Assert.Equal("https://old", Assert.Single(provider.Locations).Endpoint);
    }
}
