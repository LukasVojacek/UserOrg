using Heappe.Contracts;
using Heappe.Contracts.CommandTemplate;
using Heappe.Contracts.Connection;
using Heappe.Contracts.HEappeApiDto.Responses;
using Heappe.Features.VersionResolver;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Userorg.Adapter.Heappe;

using UserOrg.Shared;

using UserOrganizationManagement.Domain.ValueObjects;

using ExtClusterInfoModel = UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project.ClusterInfoModel;

namespace Heappe.Adapter.Tests;

// Guards the inner cache layer of TemplatesInfo: HeappeConnectionService caches each HEAppE cluster of an
// instance under its own key (CacheTags.HeappeCluster) for 5 minutes, tagged per-instance so command-template
// changes can still bulk-invalidate. The noCache flag must bypass this tier; a requested cluster that is
// absent from the catalog must be a plain cache miss (not stored), so a partial catalog never poisons the
// cache for other clusters/projects on the same instance.
public sealed class GetListAvailableClustersNoCacheTests : IDisposable
{
    private const string ResourceName = "open-123";
    private static readonly HeappeIdentifier Instance = new("heappe-example");
    private static readonly Uri ProxyBase = new("https+http://nexusapi/api/heappe/heappe-example/");
    private static readonly string[] Barbora = ["Barbora"];

    private readonly ServiceProvider _provider;
    private readonly HybridCache _cache;

    public GetListAvailableClustersNoCacheTests()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        _provider = services.BuildServiceProvider();
        _cache = _provider.GetRequiredService<HybridCache>();
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task Should_serve_cluster_from_inner_cache_When_noCache_false()
    {
        var heappeService = BuildHeappeServiceMock("Barbora");
        var sut = CreateSut(heappeService.Object);

        await GetClusters(sut, Barbora, noCache: false);
        await GetClusters(sut, Barbora, noCache: false);

        // Same instance + same location, two calls: the second is served from the per-cluster cache, so the
        // underlying HEAppE catalog query runs only once.
        VerifyCatalogQueried(heappeService, Times.Once());
    }

    [Fact]
    public async Task Should_bypass_inner_cache_When_noCache_true()
    {
        var heappeService = BuildHeappeServiceMock("Barbora");
        var sut = CreateSut(heappeService.Object);

        await GetClusters(sut, Barbora, noCache: true);
        await GetClusters(sut, Barbora, noCache: true);

        // noCache disables read AND write on the per-cluster cache, so every call queries HEAppE directly.
        VerifyCatalogQueried(heappeService, Times.Exactly(2));
    }

    [Fact]
    public async Task Should_not_cache_When_requested_cluster_absent_from_catalog()
    {
        var heappeService = BuildHeappeServiceMock("Barbora");
        var sut = CreateSut(heappeService.Object);

        // The catalog only has "Barbora"; the caller asks for "Karolina" (hydrating/absent).
        var first = await GetClusters(sut, ["Karolina"], noCache: false);
        var second = await GetClusters(sut, ["Karolina"], noCache: false);

        // The absent cluster is omitted from the result and NOT cached, so the second call refetches the
        // catalog instead of being served a stale "Karolina is missing" snapshot.
        Assert.True(first.IsSuccess);
        Assert.Empty(first.Value!);
        VerifyCatalogQueried(heappeService, Times.Exactly(2));
    }

    [Fact]
    public async Task Should_cache_present_cluster_independently_of_an_absent_one()
    {
        var heappeService = BuildHeappeServiceMock("Barbora");
        var sut = CreateSut(heappeService.Object);

        // First call asks for a present cluster AND an absent one. Only "Barbora" is cached.
        var first = await GetClusters(sut, ["Barbora", "Karolina"], noCache: false);
        // Second call asks only for the present cluster — served from cache, no new catalog query.
        var second = await GetClusters(sut, Barbora, noCache: false);

        Assert.True(first.IsSuccess);
        Assert.Single(first.Value!);
        Assert.Equal("Barbora", first.Value![0].Name);
        Assert.True(second.IsSuccess);
        Assert.Single(second.Value!);
        VerifyCatalogQueried(heappeService, Times.Once());
    }

    [Fact]
    public async Task Should_return_failure_When_catalog_fetch_fails()
    {
        var heappeService = BuildHeappeServiceMock();
        heappeService
            .Setup(s => s.GetListAvailableClusters(
                It.IsAny<BaseHeappeConnectionInfo>(),
                It.IsAny<IEnumerable<KeyValuePair<string, string?>>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Result<IList<ClusterInfoModel>, Error>>(
                Result<IList<ClusterInfoModel>, Error>.Failure(new ErrorBuilder().WithDescription("HEAppE down").Build())));
        var sut = CreateSut(heappeService.Object);

        var result = await GetClusters(sut, Barbora, noCache: false);

        // A failed catalog fetch must surface as a failure (so the Core build is marked degraded), not as an
        // empty success that would be cached for the full TTL.
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Should_refetch_after_instance_tag_invalidated()
    {
        var heappeService = BuildHeappeServiceMock("Barbora");
        var sut = CreateSut(heappeService.Object);

        await GetClusters(sut, Barbora, noCache: false);
        // A command-template change invalidates the whole instance via the per-instance tag.
        await _cache.RemoveByTagAsync(CacheTags.HeappeClusters(Instance), CancellationToken.None);
        await GetClusters(sut, Barbora, noCache: false);

        // The per-instance tag still bulk-invalidates the per-cluster entries, so the second call refetches.
        VerifyCatalogQueried(heappeService, Times.Exactly(2));
    }

    [Fact]
    public async Task Should_fail_group_without_evicting_cluster_When_catalog_lacks_allocation_for_requested_resource()
    {
        var heappeService = BuildHeappeServiceMock("Barbora");
        var sut = CreateSut(heappeService.Object);

        // The catalog has "Barbora" but its only allocation belongs to another resource (#291).
        var first = await GetClusters(sut, Barbora, noCache: false, resourceName: "other-resource");
        var second = await GetClusters(sut, Barbora, noCache: false, resourceName: "other-resource");

        // The group fails with the allocation-missing cause so Core does not cache the composed snapshot, but
        // the shared per-cluster entry stays: the second call is served from it without another catalog fetch.
        Assert.False(first.IsSuccess);
        Assert.Equal(HeappeFailureCause.AllocationMissing, first.Error!.Code);
        Assert.False(second.IsSuccess);
        Assert.Equal(HeappeFailureCause.AllocationMissing, second.Error!.Code);
        VerifyCatalogQueried(heappeService, Times.Once());
    }

    [Fact]
    public async Task Should_keep_cluster_cached_for_other_resources_When_allocation_missing_for_one()
    {
        var heappeService = BuildHeappeServiceMock("Barbora");
        var sut = CreateSut(heappeService.Object);

        // A resource without an allocation must not push the other resources on the instance back onto the
        // uncached ForceRefresh path: all three calls share the one fetched "Barbora" entry.
        var cached = await GetClusters(sut, Barbora, noCache: false);
        var missing = await GetClusters(sut, Barbora, noCache: false, resourceName: "other-resource");
        var stillCached = await GetClusters(sut, Barbora, noCache: false);

        Assert.True(cached.IsSuccess);
        Assert.False(missing.IsSuccess);
        Assert.True(stillCached.IsSuccess);
        VerifyCatalogQueried(heappeService, Times.Once());
    }

    [Fact]
    public async Task Should_pass_through_cluster_without_allocation_When_noCache_true()
    {
        var heappeService = BuildHeappeServiceMock("Barbora");
        var sut = CreateSut(heappeService.Object);

        var result = await GetClusters(sut, Barbora, noCache: true, resourceName: "other-resource");

        // noCache is a pure pass-through: the live result is returned as-is, never escalated to degraded.
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
    }

    [Fact]
    public async Task Should_keep_templates_after_pruning_When_project_name_differs_from_accounting_string()
    {
        var heappeService = BuildHeappeServiceMock();
        heappeService
            .Setup(s => s.GetListAvailableClusters(
                It.IsAny<BaseHeappeConnectionInfo>(),
                It.IsAny<IEnumerable<KeyValuePair<string, string?>>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Result<IList<ClusterInfoModel>, Error>>(
                Result<IList<ClusterInfoModel>, Error>.Success(
                    [BuildCluster("Barbora", projectName: "Some HEAppE display name", templates: [(7, "tmpl")])])));
        var sut = CreateSut(heappeService.Object);

        // Pruning keys on AccountingString like every other matcher; a HEAppE project whose Name differs from
        // its AccountingString must keep its templates (the #291 log path: "pruning removed ... 0 remain").
        var result = await sut.GetListAvailableClusters(
            new ProjectShortName("proj"), new ProjectId(Guid.NewGuid()), ResourceName, Instance,
            "bearer", Barbora, [new CommandTemplatePartitionResponse("tmpl", 7)], CancellationToken.None);

        Assert.True(result.IsSuccess);
        var project = Assert.Single(Assert.Single(Assert.Single(result.Value!).NodeTypes).Projects);
        Assert.Equal(7, Assert.Single(project.CommandTemplates).Id);
    }

    private static ValueTask<Result<IList<ExtClusterInfoModel>, Error>> GetClusters(
        HeappeConnectionService sut, IReadOnlyCollection<string> locationNames, bool noCache, string resourceName = ResourceName)
        => sut.GetListAvailableClusters(
            new ProjectShortName("proj"), new ProjectId(Guid.NewGuid()), resourceName, Instance,
            "bearer", locationNames, [], CancellationToken.None, noCache);

    [Fact]
    public async Task Should_skip_instance_without_calling_data_endpoints_When_liveness_probe_fails()
    {
        var heappeService = BuildHeappeServiceMock("Barbora");
        var sut = CreateSut(heappeService.Object, instanceAlive: false);

        var result = await GetClusters(sut, Barbora, noCache: false);

        // A dead instance is skipped at the liveness gate (ADR 0073): the call fails so Core marks it degraded,
        // and the catalog endpoint is never touched.
        Assert.False(result.IsSuccess);
        VerifyCatalogQueried(heappeService, Times.Never());
    }

    private static void VerifyCatalogQueried(Mock<IHeappeService> heappeService, Times times)
        => heappeService.Verify(s => s.GetListAvailableClusters(
            It.IsAny<BaseHeappeConnectionInfo>(),
            It.IsAny<IEnumerable<KeyValuePair<string, string?>>?>(),
            It.IsAny<CancellationToken>()), times);

    private HeappeConnectionService CreateSut(IHeappeService heappeService, bool instanceAlive = true)
    {
        var versionResolver = new Mock<IHeappeVersionResolver>();
        versionResolver
            .Setup(r => r.GetVersion(It.IsAny<string>(), It.IsAny<HeappeIdentifier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Version(6, 0, 0));

        var livenessProbe = new Mock<IHeappeLivenessProbe>();
        livenessProbe
            .Setup(p => p.IsInstanceAliveAsync(It.IsAny<HeappeIdentifier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(instanceAlive);

        var connectionFactory = new Mock<IHeappeConnectionFactory>();
        connectionFactory
            .Setup(f => f.ForUserAsync(It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HeappeIdentifier id, string _, bool _, CancellationToken _) =>
                Result<BaseHeappeConnectionInfo, Error>.Success(new PublicHeappeConnectionInfo(ProxyBase, id)));

        return new HeappeConnectionService(
            heappeService, connectionFactory.Object, versionResolver.Object, livenessProbe.Object, _cache,
            NullLogger<HeappeConnectionService>.Instance);
    }

    private static Mock<IHeappeService> BuildHeappeServiceMock(params string[] clusterNames)
    {
        var heappeService = new Mock<IHeappeService>();

        heappeService
            .Setup(s => s.GetListAvailableClusters(
                It.IsAny<BaseHeappeConnectionInfo>(),
                It.IsAny<IEnumerable<KeyValuePair<string, string?>>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Result<IList<ClusterInfoModel>, Error>>(
                Result<IList<ClusterInfoModel>, Error>.Success(clusterNames.Select(n => BuildCluster(n)).ToList<ClusterInfoModel>())));

        return heappeService;
    }

    private static ClusterInfoModel BuildCluster(string clusterName, string? projectName = null, params (long Id, string Name)[] templates) => new(
        Id: 10,
        Name: clusterName,
        Description: "test cluster",
        FileTransferMethodIds: [1],
        NodeTypes:
        [
            new NodeTypeModel(
                Id: 100,
                Name: "qcpu",
                Description: "compute node",
                NumberOfNodes: 4,
                CoresPerNode: 128,
                MaxWalltime: 3600,
                FileTransferMethodId: 1,
                Queue: "qprod",
                QualityOfService: "normal",
                ClusterAllocationName: "alloc",
                ClusterNodeTypeAggregation: null,
                Accounting: [],
                Projects:
                [
                    new ProjectModel(
                        Id: 1000,
                        Name: projectName ?? ResourceName,
                        Description: "project",
                        AccountingString: ResourceName,
                        StartDate: DateTime.UtcNow.AddDays(-1),
                        EndDate: DateTime.UtcNow.AddDays(30),
                        UsageType: 0,
                        UseAccountingStringForScheduler: false,
                        KeyScriptsDirectoryPath: string.Empty,
                        IsOneToOneMapping: false,
                        CommandTemplates: templates
                            .Select(t => new CommandTemplateCompactModel(t.Id, t.Name, "desc", string.Empty, false, true, null, []))
                            .ToList())
                ])
        ]);
}
