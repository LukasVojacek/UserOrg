using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Contracts.HEappeApiDto.Responses;
using Heappe.Features.Connection;
using Heappe.Features.VersionResolver;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Moq;

using UserOrg.Shared;

using Xunit;

namespace Heappe.UnitTests;

// Guards the v2 catalog cache (one entry per instance identifier and caller token, tagged CacheTags.HeappeClusters): a usable
// catalog is served from the cache, and a mid-rebuild skeleton is never stored.
public sealed class GetClusterCatalogCacheTests
{
    private static readonly HeappeIdentifier Instance = new("heappe-example");
    private static readonly Uri ProxyBase = new("https+http://nexusapi/api/heappe/heappe-example/");

    [Fact]
    public async Task Should_serve_catalog_from_cache_When_called_twice()
    {
        var heappeService = BuildHeappeServiceMock(BuildCluster("Karolina", withAllocation: true));
        var sut = CreateSut(heappeService.Object);

        var first = await sut.GetClusterCatalogAsync("proj", Instance, "bearer", CancellationToken.None);
        var second = await sut.GetClusterCatalogAsync("proj", Instance, "bearer", CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        VerifyCatalogQueried(heappeService, Times.Once());
    }

    [Fact]
    public async Task Should_fail_and_not_cache_When_catalog_is_a_skeleton_without_allocations()
    {
        var heappeService = BuildHeappeServiceMock(BuildCluster("Karolina", withAllocation: false));
        var sut = CreateSut(heappeService.Object);

        var first = await sut.GetClusterCatalogAsync("proj", Instance, "bearer", CancellationToken.None);
        var second = await sut.GetClusterCatalogAsync("proj", Instance, "bearer", CancellationToken.None);

        // No node type carries a project allocation: the instance is mid-rebuild. The call fails (so the
        // endpoint marks the group Failed) and nothing is stored, so the next call refetches.
        Assert.False(first.IsSuccess);
        Assert.Equal(HeappeFailureCause.InvalidResponse, first.Error!.Code);
        Assert.False(second.IsSuccess);
        VerifyCatalogQueried(heappeService, Times.Exactly(2));
    }

    [Fact]
    public async Task Should_serve_each_users_own_catalog_When_two_users_ask_for_same_instance()
    {
        var heappeService = BuildPerTokenHeappeServiceMock(new Dictionary<string, ClusterInfoModel[]>
        {
            ["token-a"] = [BuildCluster("Karolina", withAllocation: true, accountingString: "RES-A")],
            ["token-b"] = [BuildCluster("Karolina", withAllocation: true, accountingString: "RES-B")],
        });
        var sut = CreateSut(heappeService.Object, bearerConnection: true);

        await sut.GetClusterCatalogAsync("proj-a", Instance, "token-a", TestContext.Current.CancellationToken);
        var second = await sut.GetClusterCatalogAsync("proj-b", Instance, "token-b", TestContext.Current.CancellationToken);

        Assert.Equal(["RES-B"], AccountingStrings(second));
        VerifyCatalogQueried(heappeService, Times.Exactly(2));
    }

    [Fact]
    public async Task Should_resolve_version_per_caller_When_second_project_asks_for_same_instance()
    {
        var heappeService = BuildHeappeServiceMock(BuildCluster("Karolina", withAllocation: true));
        var versionResolver = VersionResolver();
        var sut = CreateSut(heappeService.Object, versionResolver.Object);

        await sut.GetClusterCatalogAsync("proj-a", Instance, "token-a", TestContext.Current.CancellationToken);
        await sut.GetClusterCatalogAsync("proj-b", Instance, "token-b", TestContext.Current.CancellationToken);

        versionResolver.Verify(r => r.GetVersion("proj-a", Instance, It.IsAny<CancellationToken>()), Times.Once());
        versionResolver.Verify(r => r.GetVersion("proj-b", Instance, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Should_cache_partial_catalog_When_only_one_cluster_has_allocations()
    {
        var heappeService = BuildHeappeServiceMock(
            BuildCluster("Karolina", withAllocation: true),
            BuildCluster("Barbora", withAllocation: false));
        var sut = CreateSut(heappeService.Object);

        await sut.GetClusterCatalogAsync("proj", Instance, "bearer", CancellationToken.None);
        var second = await sut.GetClusterCatalogAsync("proj", Instance, "bearer", CancellationToken.None);

        var barbora = Assert.Single(second.Value, c => c.Name == "Barbora");
        Assert.Empty(barbora.NodeTypes.SelectMany(nt => nt.Projects));
        VerifyCatalogQueried(heappeService, Times.Once());
    }

    [Fact]
    public async Task Should_refetch_When_instance_tag_is_evicted()
    {
        var heappeService = BuildHeappeServiceMock(BuildCluster("Karolina", withAllocation: true));
        var cache = new FakeHybridCache();
        var sut = CreateSut(heappeService.Object, cache: cache);

        await sut.GetClusterCatalogAsync("proj", Instance, "bearer", TestContext.Current.CancellationToken);
        await cache.RemoveByTagAsync(CacheTags.HeappeClusters(Instance), TestContext.Current.CancellationToken);
        await sut.GetClusterCatalogAsync("proj", Instance, "bearer", TestContext.Current.CancellationToken);

        VerifyCatalogQueried(heappeService, Times.Exactly(2));
    }

    [Fact]
    public async Task Should_serve_from_cache_When_same_token_asks_twice()
    {
        var heappeService = BuildHeappeServiceMock(BuildCluster("Karolina", withAllocation: true));
        var sut = CreateSut(heappeService.Object);

        await sut.GetClusterCatalogAsync("proj-a", Instance, "token-a", TestContext.Current.CancellationToken);
        await sut.GetClusterCatalogAsync("proj-b", Instance, "token-a", TestContext.Current.CancellationToken);

        VerifyCatalogQueried(heappeService, Times.Once());
    }

    [Fact]
    public async Task Should_refetch_When_caller_forgot_own_catalog()
    {
        var heappeService = BuildHeappeServiceMock(BuildCluster("Karolina", withAllocation: true));
        var sut = CreateSut(heappeService.Object);

        await sut.GetClusterCatalogAsync("proj", Instance, "token-a", TestContext.Current.CancellationToken);
        await sut.ForgetClusterCatalogAsync(Instance, "token-a", TestContext.Current.CancellationToken);
        await sut.GetClusterCatalogAsync("proj", Instance, "token-a", TestContext.Current.CancellationToken);

        VerifyCatalogQueried(heappeService, Times.Exactly(2));
    }

    [Fact]
    public async Task Should_keep_other_users_catalog_When_another_token_forgets()
    {
        var heappeService = BuildHeappeServiceMock(BuildCluster("Karolina", withAllocation: true));
        var sut = CreateSut(heappeService.Object);

        await sut.GetClusterCatalogAsync("proj", Instance, "token-a", TestContext.Current.CancellationToken);
        await sut.ForgetClusterCatalogAsync(Instance, "token-b", TestContext.Current.CancellationToken);
        await sut.GetClusterCatalogAsync("proj", Instance, "token-a", TestContext.Current.CancellationToken);

        VerifyCatalogQueried(heappeService, Times.Once());
    }

    private static string[] AccountingStrings(Result<IList<ClusterInfoModel>, Error> catalog)
        => [.. catalog.Value.SelectMany(c => c.NodeTypes).SelectMany(nt => nt.Projects).Select(p => p.AccountingString)];

    private static void VerifyCatalogQueried(Mock<IHeappeService> heappeService, Times times)
        => heappeService.Verify(s => s.GetListAvailableClusters(
            It.IsAny<BaseHeappeConnectionInfo>(),
            It.IsAny<IEnumerable<KeyValuePair<string, string?>>?>(),
            It.IsAny<CancellationToken>()), times);

    private static HeappeConnectionService CreateSut(
        IHeappeService heappeService, IHeappeVersionResolver? versionResolver = null, FakeHybridCache? cache = null, bool bearerConnection = false)
        => new(
            heappeService,
            ConnectionFactory(bearerConnection).Object,
            versionResolver ?? VersionResolver().Object,
            new Mock<IHttpClientFactory>().Object,
            cache ?? new FakeHybridCache());

    private static Mock<IHeappeConnectionFactory> ConnectionFactory(bool bearerConnection)
    {
        var factory = new Mock<IHeappeConnectionFactory>();
        factory
            .Setup(f => f.ForUserAsync(It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HeappeIdentifier id, string token, bool _, CancellationToken _) =>
                Result<BaseHeappeConnectionInfo, Error>.Success(
                    bearerConnection ? new BearerTokenHeappeConnectionInfo(ProxyBase, id, token) : new PublicHeappeConnectionInfo(ProxyBase, id)));
        return factory;
    }

    private static Mock<IHeappeVersionResolver> VersionResolver()
    {
        var versionResolver = new Mock<IHeappeVersionResolver>();
        versionResolver
            .Setup(r => r.GetVersion(It.IsAny<string>(), It.IsAny<HeappeIdentifier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Version(6, 0, 0));
        return versionResolver;
    }

    // HEAppE answers ListAvailableClusters with the caller's view, so the catalog depends on the token.
    private static Mock<IHeappeService> BuildPerTokenHeappeServiceMock(Dictionary<string, ClusterInfoModel[]> catalogByToken)
    {
        var heappeService = new Mock<IHeappeService>();

        heappeService
            .Setup(s => s.GetListAvailableClusters(
                It.IsAny<BaseHeappeConnectionInfo>(),
                It.IsAny<IEnumerable<KeyValuePair<string, string?>>?>(),
                It.IsAny<CancellationToken>()))
            .Returns((BaseHeappeConnectionInfo connInfo, IEnumerable<KeyValuePair<string, string?>>? _, CancellationToken _) =>
                new ValueTask<Result<IList<ClusterInfoModel>, Error>>(
                    Result<IList<ClusterInfoModel>, Error>.Success(
                        catalogByToken[((BearerTokenHeappeConnectionInfo)connInfo).BearerToken].ToList<ClusterInfoModel>())));

        return heappeService;
    }

    private static Mock<IHeappeService> BuildHeappeServiceMock(params ClusterInfoModel[] clusters)
    {
        var heappeService = new Mock<IHeappeService>();

        heappeService
            .Setup(s => s.GetListAvailableClusters(
                It.IsAny<BaseHeappeConnectionInfo>(),
                It.IsAny<IEnumerable<KeyValuePair<string, string?>>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask<Result<IList<ClusterInfoModel>, Error>>(
                Result<IList<ClusterInfoModel>, Error>.Success(clusters.ToList<ClusterInfoModel>())));

        return heappeService;
    }

    private static ClusterInfoModel BuildCluster(string clusterName, bool withAllocation, string accountingString = "open-123") => new(
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
                Projects: withAllocation
                    ?
                    [
                        new ProjectModel(
                            Id: 1000,
                            Name: accountingString,
                            Description: "project",
                            AccountingString: accountingString,
                            StartDate: DateTime.UtcNow.AddDays(-1),
                            EndDate: DateTime.UtcNow.AddDays(30),
                            UsageType: 0,
                            UseAccountingStringForScheduler: false,
                            KeyScriptsDirectoryPath: string.Empty,
                            IsOneToOneMapping: false,
                            CommandTemplates: [])
                    ]
                    : [])
        ]);
}
