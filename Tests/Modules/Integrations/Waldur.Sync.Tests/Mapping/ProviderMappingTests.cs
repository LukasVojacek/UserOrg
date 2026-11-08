using Waldur.Sync.Api;
using Waldur.Sync.Api.WaldurApiModels;
using Waldur.Sync.Mapping;

using Xunit;

namespace Waldur.Sync.Tests.Mapping;

public sealed class ProviderMappingTests
{
    [Fact]
    public void Should_DeriveOneLocation_When_OfferingsShareSshUrl()
    {
        var mapper = MapperTestData.Mapper();
        var offerings = new[]
        {
            MapperTestData.Offering(id: Guid.NewGuid(), name: "karolina-cpu", endpoints: [MapperTestData.SshEndpoint("ssh://login.karolina.it4i.cz")]),
            MapperTestData.Offering(id: Guid.NewGuid(), name: "karolina-gpu", endpoints: [MapperTestData.SshEndpoint("ssh://login.karolina.it4i.cz")])
        };

        var result = mapper.MapProvider(MapperTestData.Customer(), offerings);

        var location = Assert.Single(result.Payload!.Locations);
        // Longest common prefix of "karolina-cpu"/"karolina-gpu", trimmed of trailing '-'.
        Assert.Equal("karolina", location.Name);
        // ssh:// scheme stripped for HPC.
        Assert.Equal("login.karolina.it4i.cz", location.Endpoint);
        // W1: LocationType is case-folded (consumer resolves it case-insensitively → stable hash).
        Assert.Equal("hpc", location.LocationType);
    }

    [Fact]
    public void Should_CaseFold_CountryAndLocationType_For_StableHash()
    {
        var mapper = MapperTestData.Mapper();
        var offerings = new[]
        {
            MapperTestData.Offering(id: Guid.NewGuid(), name: "n", type: "HPC", endpoints: [MapperTestData.SshEndpoint("ssh://h")])
        };

        // Raw Waldur casing ("Czechia") must be folded so it hashes the same as the canonical DB row.
        var result = mapper.MapProvider(MapperTestData.Customer(country: "Czechia"), offerings);

        Assert.Equal("czechia", result.Payload!.Country);
        Assert.Equal("hpc", Assert.Single(result.Payload.Locations).LocationType);
    }

    [Fact]
    public void Should_DeriveSeparateLocations_When_SshUrlsDiffer()
    {
        var mapper = MapperTestData.Mapper();
        var offerings = new[]
        {
            MapperTestData.Offering(id: Guid.NewGuid(), name: "a", endpoints: [MapperTestData.SshEndpoint("ssh://host-a")]),
            MapperTestData.Offering(id: Guid.NewGuid(), name: "b", endpoints: [MapperTestData.SshEndpoint("ssh://host-b")])
        };

        var result = mapper.MapProvider(MapperTestData.Customer(), offerings);

        Assert.Equal(2, result.Payload!.Locations.Count);
    }

    [Fact]
    public void Should_GroupSshUrlCaseInsensitively()
    {
        var mapper = MapperTestData.Mapper();
        var offerings = new[]
        {
            MapperTestData.Offering(id: Guid.NewGuid(), name: "x1", endpoints: [MapperTestData.SshEndpoint("ssh://Host-A")]),
            MapperTestData.Offering(id: Guid.NewGuid(), name: "x2", endpoints: [MapperTestData.SshEndpoint("ssh://host-a")])
        };

        var result = mapper.MapProvider(MapperTestData.Customer(), offerings);

        Assert.Single(result.Payload!.Locations);
    }

    [Fact]
    public void Should_MarkLocationDeleted_When_AllOfferingsArchived()
    {
        var mapper = MapperTestData.Mapper();
        var offerings = new[]
        {
            MapperTestData.Offering(id: Guid.NewGuid(), name: "a1", state: "archived", endpoints: [MapperTestData.SshEndpoint("ssh://h")]),
            MapperTestData.Offering(id: Guid.NewGuid(), name: "a2", state: "archived", endpoints: [MapperTestData.SshEndpoint("ssh://h")])
        };

        var result = mapper.MapProvider(MapperTestData.Customer(), offerings);

        Assert.True(Assert.Single(result.Payload!.Locations).IsDeleted);
    }

    [Fact]
    public void Should_NotMarkDeleted_When_OneOfferingActive()
    {
        var mapper = MapperTestData.Mapper();
        var offerings = new[]
        {
            MapperTestData.Offering(id: Guid.NewGuid(), name: "a1", state: "archived", endpoints: [MapperTestData.SshEndpoint("ssh://h")]),
            MapperTestData.Offering(id: Guid.NewGuid(), name: "a2", state: "active", endpoints: [MapperTestData.SshEndpoint("ssh://h")])
        };

        var result = mapper.MapProvider(MapperTestData.Customer(), offerings);

        Assert.False(Assert.Single(result.Payload!.Locations).IsDeleted);
    }

    [Fact]
    public void Should_CarryIsShared_FromOffering()
    {
        var mapper = MapperTestData.Mapper();
        var offerings = new[] { MapperTestData.Offering(shared: true, endpoints: [MapperTestData.SshEndpoint("ssh://h")]) };

        var result = mapper.MapProvider(MapperTestData.Customer(), offerings);

        Assert.True(Assert.Single(result.Payload!.Locations).IsShared);
    }

    [Fact]
    public void Should_SkipOffering_When_NoSshLoginEndpoint()
    {
        var mapper = MapperTestData.Mapper();
        var offerings = new[]
        {
            MapperTestData.Offering(id: Guid.NewGuid(), name: "web", endpoints: [new ApiPublicOfferingEndpoint(Guid.NewGuid(), "Web UI", "https://ui")]),
            MapperTestData.Offering(id: Guid.NewGuid(), name: "hpc", endpoints: [MapperTestData.SshEndpoint("ssh://h")])
        };

        var result = mapper.MapProvider(MapperTestData.Customer(), offerings);

        Assert.Single(result.Payload!.Locations);
        Assert.Contains(result.Skips, s => s.Reason == EMappingSkipReason.LocationInvalidNoSshEndpoint);
    }

    [Fact]
    public void Should_NotStripScheme_ForNonHpc()
    {
        var mapper = MapperTestData.Mapper();
        var offerings = new[] { MapperTestData.Offering(type: "Storage", endpoints: [MapperTestData.SshEndpoint("ssh://keep-scheme")]) };

        var result = mapper.MapProvider(MapperTestData.Customer(), offerings);

        Assert.Equal("ssh://keep-scheme", Assert.Single(result.Payload!.Locations).Endpoint);
    }

    [Fact]
    public void Should_SkipProvider_When_CountryMissing()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapProvider(MapperTestData.Customer(country: null), [MapperTestData.Offering()]);

        Assert.Null(result.Payload);
        Assert.Contains(result.Skips, s => s.Reason == EMappingSkipReason.ProviderCountryMissing);
    }

    [Fact]
    public void Should_SkipProvider_When_NameMissing()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapProvider(MapperTestData.Customer(name: "  "), [MapperTestData.Offering()]);

        Assert.Null(result.Payload);
        Assert.Contains(result.Skips, s => s.Reason == EMappingSkipReason.ProviderNameMissing);
    }

    [Fact]
    public void Should_SkipProvider_When_NoDerivableLocations()
    {
        var mapper = MapperTestData.Mapper();
        var offerings = new[] { MapperTestData.Offering(endpoints: [new ApiPublicOfferingEndpoint(Guid.NewGuid(), "Web", "https://x")]) };

        var result = mapper.MapProvider(MapperTestData.Customer(), offerings);

        Assert.Null(result.Payload);
        Assert.Contains(result.Skips, s => s.Reason == EMappingSkipReason.ProviderNoLocations);
    }

    [Fact]
    public void Should_ProduceCanonicalExternalId()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapProvider(MapperTestData.Customer(), [MapperTestData.Offering()]);

        Assert.Equal("11111111-1111-1111-1111-111111111111", result.Payload!.ExternalId);
    }

    [Fact]
    public void Should_StripControlCharacters_FromProviderName_BeforeTruncation()
    {
        var mapper = MapperTestData.Mapper();
        var dirtyName = $"Provider\u0007 Inc{new string('x', 100)}";
        var strippedThenTruncated = $"Provider Inc{new string('x', 100)}"[..100];

        var result = mapper.MapProvider(MapperTestData.Customer(name: dirtyName), [MapperTestData.Offering()]);

        Assert.Equal(strippedThenTruncated, result.Payload!.Name);
    }

    [Fact]
    public void Should_StripControlCharacters_FromLocationName_BeforeTruncation()
    {
        var mapper = MapperTestData.Mapper();
        var strippedThenTruncated = $"karolina{new string('x', 100)}"[..100];
        var offerings = new[] { MapperTestData.Offering(name: $"karolina\u0007{new string('x', 100)}", endpoints: [MapperTestData.SshEndpoint("ssh://h")]) };

        var result = mapper.MapProvider(MapperTestData.Customer(), offerings);

        Assert.Equal(strippedThenTruncated, Assert.Single(result.Payload!.Locations).Name);
    }

    [Fact]
    public void Should_StripControlCharacterThenTrim_FromProviderName()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapProvider(MapperTestData.Customer(name: "\u0007 Name"), [MapperTestData.Offering()]);

        Assert.Equal("Name", result.Payload!.Name);
    }

    [Fact]
    public void Should_StripControlCharacterThenTrim_FromLocationName()
    {
        var mapper = MapperTestData.Mapper();
        var offerings = new[] { MapperTestData.Offering(name: "\u0007 Name", endpoints: [MapperTestData.SshEndpoint("ssh://h")]) };

        var result = mapper.MapProvider(MapperTestData.Customer(), offerings);

        Assert.Equal("Name", Assert.Single(result.Payload!.Locations).Name);
    }

    [Fact]
    public void Should_SkipProvider_When_NameIsOnlyControlCharacters()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapProvider(MapperTestData.Customer(name: "\u0007\u0007"), [MapperTestData.Offering()]);

        Assert.Null(result.Payload);
        Assert.Contains(result.Skips, s => s.Reason == EMappingSkipReason.ProviderNameMissing);
    }

    [Fact]
    public void Should_SkipProvider_When_NameIsNull()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapProvider(MapperTestData.Customer(name: null!), [MapperTestData.Offering()]);

        Assert.Null(result.Payload);
        Assert.Contains(result.Skips, s => s.Reason == EMappingSkipReason.ProviderNameMissing);
    }

    [Fact]
    public void Should_FoldOfferingNames_IgnoringControlCharacters()
    {
        var mapper = MapperTestData.Mapper();
        var offerings = new[]
        {
            MapperTestData.Offering(id: Guid.Parse("33333333-3333-3333-3333-333333333331"),
                name: "karolina\u0007-cpu", endpoints: [MapperTestData.SshEndpoint("ssh://h")]),
            MapperTestData.Offering(id: Guid.Parse("33333333-3333-3333-3333-333333333332"),
                name: "karolina-cpu", endpoints: [MapperTestData.SshEndpoint("ssh://h")])
        };

        var result = mapper.MapProvider(MapperTestData.Customer(), offerings);

        Assert.Equal("karolina-cpu", Assert.Single(result.Payload!.Locations).Name);
    }

    [Fact]
    public void Should_SkipLocation_When_NameIsOnlyControlCharacters()
    {
        var mapper = MapperTestData.Mapper();
        var offerings = new[] { MapperTestData.Offering(name: "\u0007\u0007", endpoints: [MapperTestData.SshEndpoint("ssh://h")]) };

        var result = mapper.MapProvider(MapperTestData.Customer(), offerings);

        Assert.Null(result.Payload);
        Assert.Contains(result.Skips, s => s.Reason == EMappingSkipReason.LocationNameMissing);
    }
}
