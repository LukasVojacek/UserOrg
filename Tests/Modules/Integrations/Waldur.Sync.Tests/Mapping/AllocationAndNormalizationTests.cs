using Waldur.Sync.Api.WaldurApiModels;
using Waldur.Sync.Mapping;

using Xunit;

namespace Waldur.Sync.Tests.Mapping;

public sealed class AllocationAndNormalizationTests
{
    private static ResourceOffering Ro(
        IReadOnlyDictionary<string, decimal> allocations,
        IReadOnlyList<ApiPublicOfferingComponent> components,
        string? heappeBasePath = null,
        string sshUrl = "ssh://hpc.example.org",
        string effectiveId = "res-1") =>
        new(
            MapperTestData.Resource(effectiveId: effectiveId, backendId: null, allocations: allocations),
            MapperTestData.Offering(
                endpoints: [MapperTestData.SshEndpoint(sshUrl)],
                components: components,
                heappeBasePath: heappeBasePath));

    [Theory]
    [InlineData("Node-hours", "node-hours", 10, 10)]      // ×1
    [InlineData("k-Hours", "core-hours", 3, 3000)]        // ×1000
    [InlineData("TB-hours", "GB", 2, 2000)]               // ×1000
    public void Should_ConvertAllocationUnit_PerMap(string measuredUnit, string userorgUnit, int raw, long expected)
    {
        var mapper = MapperTestData.Mapper();
        var result = mapper.MapProject(
            MapperTestData.Project(),
            [Ro(
                allocations: new Dictionary<string, decimal> { ["comp"] = raw },
                components: [MapperTestData.Component(type: "comp", measuredUnit: measuredUnit)])]);

        var resource = Assert.Single(result.Payload!.Resources);
        Assert.Equal(userorgUnit, resource.AllocationUnitType);
        Assert.Equal(expected, resource.AllocationUnitCount);
    }

    [Fact]
    public void Should_MarkAllocationUnspecified_When_NoComponentMatches()
    {
        var mapper = MapperTestData.Mapper();
        var result = mapper.MapProject(
            MapperTestData.Project(),
            [Ro(
                allocations: new Dictionary<string, decimal> { ["comp"] = 5 },
                components: [MapperTestData.Component(type: "comp", measuredUnit: "unmapped-unit")])]);

        var resource = Assert.Single(result.Payload!.Resources);
        Assert.Equal("unspecified", resource.AllocationUnitType);
        Assert.Null(resource.AllocationUnitCount);
    }

    [Fact]
    public void Should_CarryLeftoverAllocations_AsValues()
    {
        var mapper = MapperTestData.Mapper();
        var result = mapper.MapProject(
            MapperTestData.Project(),
            [Ro(
                allocations: new Dictionary<string, decimal> { ["cpu"] = 3, ["storage"] = 42 },
                components: [MapperTestData.Component(type: "cpu", measuredUnit: "Node-hours")])]);

        var resource = Assert.Single(result.Payload!.Resources);
        // The matched (cpu/core-hours) allocation is removed; "storage" remains as a value.
        Assert.Contains(resource.Values, v => v.Name == "storage" && v.Value == "42");
        Assert.DoesNotContain(resource.Values, v => v.Name == "cpu");
    }

    [Fact]
    public void Should_AddHeappeLocalBasePathValue_When_PluginOptionPresent()
    {
        var mapper = MapperTestData.Mapper();
        var result = mapper.MapProject(
            MapperTestData.Project(),
            [Ro(
                allocations: new Dictionary<string, decimal> { ["cpu"] = 3 },
                components: [MapperTestData.Component(type: "cpu", measuredUnit: "core-hours")],
                heappeBasePath: "/data/base/",
                effectiveId: "clusterX")]);

        var resource = Assert.Single(result.Payload!.Resources);
        Assert.Contains(resource.Values, v =>
            v.Name == "HEAPPE_LOCAL_BASE_PATH" && v.Value == "/data/base/clusterX");
    }

    [Fact]
    public void Should_ApplyDeucalionTrim_ToHeappeBasePathOnly_NotName()
    {
        var mapper = MapperTestData.Mapper();
        var result = mapper.MapProject(
            MapperTestData.Project(),
            [Ro(
                allocations: new Dictionary<string, decimal> { ["cpu"] = 3 },
                components: [MapperTestData.Component(type: "cpu", measuredUnit: "core-hours")],
                heappeBasePath: "/base",
                sshUrl: "ssh://login.deucalion.macc.fccn.pt",
                effectiveId: "deucalion7")]);

        var resource = Assert.Single(result.Payload!.Resources);
        Assert.Equal("deucalion7", resource.Name); // name NOT trimmed
        Assert.Contains(resource.Values, v =>
            v.Name == "HEAPPE_LOCAL_BASE_PATH" && v.Value == "/base/deucalion"); // last char trimmed in path
    }

    [Fact]
    public void Should_NotAddHeappeBasePath_When_NoComponentsOrAllocations()
    {
        var mapper = MapperTestData.Mapper();
        var result = mapper.MapProject(
            MapperTestData.Project(),
            [Ro(
                allocations: new Dictionary<string, decimal> { ["cpu"] = 3 },
                components: [],
                heappeBasePath: "/base")]);

        var resource = Assert.Single(result.Payload!.Resources);
        Assert.DoesNotContain(resource.Values, v => v.Name == "HEAPPE_LOCAL_BASE_PATH");
    }
}
