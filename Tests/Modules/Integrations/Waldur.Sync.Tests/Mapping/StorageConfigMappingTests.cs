using Waldur.Sync.Configuration;
using Waldur.Sync.Mapping;

using Xunit;

namespace Waldur.Sync.Tests.Mapping;

public sealed class StorageConfigMappingTests
{
    private static WaldurStorageOptions Storage() => new()
    {
        EfpCoreServiceName = "EFP-Core",
        UseSharedIrodsForAllEntities = true,
        SharedIrods = new WaldurStorageInstanceOptions { Name = "shared-irods", Endpoint = new Uri("https://irods.shared"), AllocationAmount = 100 },
        IrodsInstancesMap = new Dictionary<string, WaldurStorageInstanceOptions>(StringComparer.OrdinalIgnoreCase)
        {
            [MapperTestData.CustomerId.ToString("N")] = new() { Name = "irods-a", Endpoint = new Uri("https://irods.a"), AllocationAmount = 5 }
        },
        StagingAreaInstancesMap = new Dictionary<string, WaldurStorageInstanceOptions>(StringComparer.OrdinalIgnoreCase)
        {
            [MapperTestData.CustomerId.ToString("N")] = new() { Name = "staging-a", Endpoint = new Uri("https://staging.a") }
        }
    };

    [Fact]
    public void Should_MapStorageConfig_FromOptions()
    {
        var mapper = MapperTestData.Mapper(storage: Storage());

        var payload = mapper.MapStorageConfig();

        Assert.Equal("EFP-Core", payload.EfpCoreServiceName);
        Assert.True(payload.UseSharedIrodsForAllEntities);
        Assert.Equal("shared-irods", payload.SharedIrods!.Name);
        Assert.Null(payload.SharedIrods.HostingEntityExternalId);
    }

    [Fact]
    public void Should_MapPerProviderIrods_WithCanonicalHostingId_And_IrodsType()
    {
        var mapper = MapperTestData.Mapper(storage: Storage());

        var payload = mapper.MapStorageConfig();

        var irods = Assert.Single(payload.IrodsInstances);
        Assert.Equal("11111111-1111-1111-1111-111111111111", irods.HostingEntityExternalId);
        Assert.Equal("iRODS", irods.LocationType);
        Assert.Equal(5, irods.AllocationAmount);
    }

    [Fact]
    public void Should_MapStagingArea_WithStorageType()
    {
        var mapper = MapperTestData.Mapper(storage: Storage());

        var payload = mapper.MapStorageConfig();

        Assert.Equal("Storage", Assert.Single(payload.StagingAreaInstances).LocationType);
    }

    [Fact]
    public void Should_ExposeEfpCoreServiceName_ForRebuilderExclusion()
    {
        // D14/ADR-0055-A4: reconciliation-owned rows carry EfpCoreServiceName; the payload surfaces it
        // so the DB→payload rebuilder (B11) can exclude those rows. The API→payload mapper never emits
        // EfpCoreServiceName rows itself (it only consumes Waldur API resources).
        var mapper = MapperTestData.Mapper(storage: Storage());

        Assert.Equal("EFP-Core", mapper.MapStorageConfig().EfpCoreServiceName);
    }

    [Fact]
    public void Should_Throw_When_StorageOptionsMissing()
    {
        var mapper = MapperTestData.Mapper(storage: null);

        Assert.Throws<InvalidOperationException>(() => mapper.MapStorageConfig());
    }
}
