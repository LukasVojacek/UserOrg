using Core.DataAccess.Queries.Contracts;
using Core.Features.Project.GetProjectTemplates;
using Core.Shared.OutputCache;

using Heappe.Contracts;
using Heappe.Contracts.CommandTemplate;
using Heappe.Contracts.HEappeApiDto.Responses;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using HeappeIdentifier = UserOrg.Shared.HeappeIdentifier;

namespace Core.UnitTests.ProjectTemplates;

public sealed class GetProjectTemplatesEndpointTests
{
    private const string ResourceName = "FTA-24-49";

    [Fact]
    public async Task Should_SkipExpensiveCall_When_HostIsNotAlive()
    {
        var link = Link(new HeappeIdentifier("dead-host"), ResourceName, "Karolina");

        var heappe = Heappe();
        heappe.Setup(h => h.IsHostAliveAsync(It.IsAny<HeappeIdentifier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var http = Http();
        var result = await Invoke(http, heappe, Query(link));

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectTemplatesEndpoint.Response>>>(result);
        var entry = Assert.Single(ok.Value!);
        Assert.False(entry.IsAvailable);
        Assert.Equal("Karolina", entry.ClusterName);
        Assert.Equal("1", http.Response.Headers["X-Total-Count"]);
        Assert.False(http.Response.Headers.ContainsKey("Link"));

        // A dead host suppresses output-cache storage (Failed) and never fetches the catalog.
        Assert.True(ResponseCacheControl.IsStorageSuppressed(http));
        heappe.Verify(
            h => h.GetClusterCatalogAsync(
                It.IsAny<string>(), It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_ProbeAndFetchCatalog_OncePerDistinctUrl_AndReadAllowedOnce_When_ResourcesShareUrls()
    {
        // Trailing slash = the normalized form the endpoint groups/probes by (ToNormalizeUrl()).
        var id1 = new HeappeIdentifier("heappe-1");
        var id2 = new HeappeIdentifier("heappe-2");

        // Two resources share id1, one sits on id2 → 2 distinct URLs, 3 links.
        var links = new[]
        {
            Link(id1, "RES-A", "Karolina"),
            Link(id1, "RES-B", "Barbora"),
            Link(id2, "RES-C", "Lumi"),
        };

        var heappe = Heappe();
        var result = await Invoke(Http(), heappe, Query(links));

        Assert.IsType<Ok<IReadOnlyList<GetProjectTemplatesEndpoint.Response>>>(result);

        // Structural dedup: each distinct URL probed once and catalog fetched once, regardless of resource count.
        heappe.Verify(h => h.IsHostAliveAsync(id1, It.IsAny<CancellationToken>()), Times.Once);
        heappe.Verify(h => h.IsHostAliveAsync(id2, It.IsAny<CancellationToken>()), Times.Once);
        heappe.Verify(h => h.IsHostAliveAsync(It.IsAny<HeappeIdentifier>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        heappe.Verify(h => h.GetClusterCatalogAsync(It.IsAny<string>(), id1, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        heappe.Verify(h => h.GetClusterCatalogAsync(It.IsAny<string>(), id2, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        heappe.Verify(h => h.GetClusterCatalogAsync(It.IsAny<string>(), It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Allowed-template set read exactly once for the whole request.
        heappe.Verify(h => h.GetActiveCommandTemplatesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_KeepOnlyAllowedCommandTemplates_When_AllowedSetIsNonEmpty()
    {
        var identifier = new HeappeIdentifier("heappe");
        var link = Link(identifier, ResourceName, "Karolina");

        var heappe = Heappe(
            catalog: Catalog("Karolina", ResourceName, (1, "Allowed"), (2, "Disallowed")),
            allowed: [new CommandTemplatePartitionResponse("Allowed", 1)]);

        var http = Http();
        var result = await Invoke(http, heappe, Query(link));

        var template = Assert.Single(CommandTemplates(result));
        Assert.Equal("Allowed", template.Name);
        Assert.Equal(1, template.Id);
        Assert.False(ResponseCacheControl.IsStorageSuppressed(http));
    }

    [Fact]
    public async Task Should_KeepAllCommandTemplates_When_AllowedSetIsEmpty()
    {
        var identifier = new HeappeIdentifier("heappe");
        var link = Link(identifier, ResourceName, "Karolina");

        var heappe = Heappe(
            catalog: Catalog("Karolina", ResourceName, (1, "Allowed"), (2, "Disallowed")),
            allowed: []);

        var result = await Invoke(Http(), heappe, Query(link));

        var names = CommandTemplates(result).Select(c => c.Name).OrderBy(n => n).ToArray();
        Assert.Equal(["Allowed", "Disallowed"], names);
    }

    [Fact]
    public async Task Should_SuppressCacheWithoutForgettingCatalog_When_ResourceAbsentFromFetchedCatalog()
    {
        var identifier = new HeappeIdentifier("heappe");
        var link = Link(identifier, ResourceName, "Karolina");

        // The project holds an active link to Karolina, but the catalog does not list that cluster: the
        // answer is incomplete for this project, so it is not stored; the catalog entry itself is kept.
        var heappe = Heappe(catalog: Catalog("SomeOtherCluster", ResourceName, (1, "Allowed")));

        var http = Http();
        var result = await Invoke(http, heappe, Query(link));

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectTemplatesEndpoint.Response>>>(result);
        var entry = Assert.Single(ok.Value!);
        Assert.False(entry.IsAvailable);
        Assert.True(ResponseCacheControl.IsStorageSuppressed(http));
        VerifyForgotten(heappe, Times.Never());
    }

    [Fact]
    public async Task Should_SuppressCache_When_MatchedClusterLacksAllocationForActiveLink()
    {
        var identifier = new HeappeIdentifier("heappe");
        var link = Link(identifier, ResourceName, "Karolina");

        // The cluster is there, but HEAppE's catalog has no allocation for the resource UserOrg holds an
        // active link for — an incomplete answer (#291), not "no allocation".
        var heappe = Heappe(catalog: Catalog("Karolina", "OTHER-RESOURCE", (1, "Allowed")));

        var http = Http();
        var result = await Invoke(http, heappe, Query(link));

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectTemplatesEndpoint.Response>>>(result);
        var entry = Assert.Single(ok.Value!);
        Assert.False(entry.IsAvailable);
        Assert.True(ResponseCacheControl.IsStorageSuppressed(http));
    }

    [Fact]
    public async Task Should_ReturnEmptyWithoutSuppressingCache_When_OnlyLinkIsExpiredInUserOrg()
    {
        var identifier = new HeappeIdentifier("heappe");
        var today = DateTimeOffset.UtcNow.Date;
        var expired = Link(identifier, ResourceName, "Karolina") with { StartDate = today.AddDays(-10), EndDate = today.AddDays(-1) };

        var heappe = Heappe(catalog: Catalog("Karolina", "OTHER-RESOURCE", (1, "Allowed")));

        var http = Http();
        var result = await Invoke(http, heappe, Query(expired));

        // Genuinely no allocation in UserOrg: empty 200, cacheable, and HEAppE is never asked.
        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectTemplatesEndpoint.Response>>>(result);
        Assert.Empty(ok.Value!);
        Assert.False(ResponseCacheControl.IsStorageSuppressed(http));
        heappe.Verify(
            h => h.GetClusterCatalogAsync(It.IsAny<string>(), It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_ForgetCallersCatalog_When_CachedCatalogHoldsAnotherUsersAllocations()
    {
        // The cached catalog lists the project's clusters, but the allocations belong to other resources:
        // the answer is not stored and the caller's catalog entry is dropped once per instance URL.
        var heappe = Heappe(catalog:
        [
            .. Catalog("Karolina", "OTHER-RESOURCE"),
            .. Catalog("Barbora", "OTHER-RESOURCE"),
            .. Catalog("Zenith", "OTHER-RESOURCE"),
            .. Catalog("EFP Staging Slurm 1", "OTHER-RESOURCE"),
        ]);

        var http = Http();
        var result = await Invoke(http, heappe, Query(ObservedLinks()));

        AssertEveryLocationUnavailable(result);
        Assert.True(ResponseCacheControl.IsStorageSuppressed(http));
        heappe.Verify(
            h => h.ForgetClusterCatalogAsync(new HeappeIdentifier("heappe-it4i"), "test-token", It.IsAny<CancellationToken>()),
            Times.Once());
        heappe.Verify(
            h => h.ForgetClusterCatalogAsync(new HeappeIdentifier("heappe-zenith"), "test-token", It.IsAny<CancellationToken>()),
            Times.Once());
        heappe.Verify(
            h => h.ForgetClusterCatalogAsync(new HeappeIdentifier("heappe-efp"), "test-token", It.IsAny<CancellationToken>()),
            Times.Once());
        VerifyForgotten(heappe, Times.Exactly(3));
    }

    [Fact]
    public async Task Should_SuppressStorage_When_CachedCatalogOmitsTheProjectsClusters()
    {
        // The catalog does not list this project's clusters at all: the all-unavailable answer is not stored.
        var heappe = Heappe(catalog: Catalog("SomeOtherCluster", "OTHER-RESOURCE"));

        var http = Http();
        var result = await Invoke(http, heappe, Query(ObservedLinks()));

        AssertEveryLocationUnavailable(result);
        Assert.True(ResponseCacheControl.IsStorageSuppressed(http));
    }

    [Fact]
    public async Task Should_ForgetCatalogOnce_When_OneLinkedClusterLacksAllocation()
    {
        var identifier = new HeappeIdentifier("heappe");
        var heappe = Heappe(catalog:
        [
            .. Catalog("Karolina", ResourceName, (1, "Allowed")),
            .. Catalog("Barbora", "OTHER-RESOURCE", (1, "Allowed")),
        ]);

        var http = Http();
        var result = await Invoke(http, heappe, Query(Link(identifier, ResourceName, "Karolina"), Link(identifier, ResourceName, "Barbora")));

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectTemplatesEndpoint.Response>>>(result);
        Assert.True(Assert.Single(ok.Value!, e => e.ClusterName == "Karolina").IsAvailable);
        Assert.False(Assert.Single(ok.Value!, e => e.ClusterName == "Barbora").IsAvailable);
        Assert.True(ResponseCacheControl.IsStorageSuppressed(http));
        heappe.Verify(h => h.ForgetClusterCatalogAsync(identifier, "test-token", It.IsAny<CancellationToken>()), Times.Once());
        VerifyForgotten(heappe, Times.Once());
    }

    [Fact]
    public async Task Should_NotForgetCatalog_When_EveryLinkHasAllocation()
    {
        var identifier = new HeappeIdentifier("heappe");
        var heappe = Heappe(catalog: Catalog("Karolina", ResourceName, (1, "Allowed")));

        var http = Http();
        var result = await Invoke(http, heappe, Query(Link(identifier, ResourceName, "Karolina")));

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectTemplatesEndpoint.Response>>>(result);
        Assert.True(Assert.Single(ok.Value!).IsAvailable);
        Assert.False(ResponseCacheControl.IsStorageSuppressed(http));
        VerifyForgotten(heappe, Times.Never());
    }

    // ---- helpers ----

    private static void VerifyForgotten(Mock<IHeappeConnectionService> heappe, Times times)
        => heappe.Verify(
            h => h.ForgetClusterCatalogAsync(It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            times);

    private static ProjectHeappeLinkEntry[] ObservedLinks()
    {
        var it4i = new HeappeIdentifier("heappe-it4i");
        return
        [
            Link(it4i, ResourceName, "Karolina"),
            Link(it4i, ResourceName, "Barbora"),
            Link(new HeappeIdentifier("heappe-zenith"), ResourceName, "Zenith"),
            Link(new HeappeIdentifier("heappe-efp"), ResourceName, "EFP Staging Slurm 1"),
        ];
    }

    private static void AssertEveryLocationUnavailable(IResult result)
    {
        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectTemplatesEndpoint.Response>>>(result);
        Assert.Equal(["Karolina", "Barbora", "Zenith", "EFP Staging Slurm 1"], ok.Value!.Select(e => e.ClusterName));
        Assert.All(ok.Value!, e =>
        {
            Assert.False(e.IsAvailable);
            Assert.Null(e.ClusterId);
            Assert.Empty(e.Nodes);
        });
    }

    private static IReadOnlyList<GetProjectTemplatesEndpoint.CommandTemplateResponse> CommandTemplates(IResult result)
    {
        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectTemplatesEndpoint.Response>>>(result);
        var entry = Assert.Single(ok.Value!);
        Assert.True(entry.IsAvailable);
        var node = Assert.Single(entry.Nodes);
        var allocation = Assert.Single(node.Allocations);
        return allocation.CommandTemplates;
    }

    private static async Task<IResult> Invoke(HttpContext http, Mock<IHeappeConnectionService> heappe, Mock<IProjectQuery> query)
        => await GetProjectTemplatesEndpoint.HandleAsync(
            http: http,
            projectShortName: ProjectShortName.Create("PROJ-1"),
            caller: Caller(),
            projectQuery: query.Object,
            heappeConnectionService: heappe.Object,
            loggerFactory: NullLoggerFactory.Instance,
            ct: CancellationToken.None);

    private static ProjectHeappeLinkEntry Link(HeappeIdentifier identifier, string resourceName, string location)
    {
        var today = DateTimeOffset.UtcNow.Date;
        return new ProjectHeappeLinkEntry(
            ProjectId: Guid.NewGuid(),
            HeappeApi: null,
            HeappeIdentifier: identifier,
            ResourceId: Guid.NewGuid(),
            ResourceName: resourceName,
            LocationName: location,
            LocationId: location,
            StartDate: today.AddDays(-1),
            EndDate: today.AddDays(1));
    }

    private static Mock<IProjectQuery> Query(params ProjectHeappeLinkEntry[] links)
    {
        var query = new Mock<IProjectQuery>();
        query.Setup(q => q.GetProjectHeappeLinksAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(links.ToList());
        return query;
    }

    private static Mock<IHeappeConnectionService> Heappe(
        IList<ClusterInfoModel>? catalog = null,
        HashSet<CommandTemplatePartitionResponse>? allowed = null)
    {
        var heappe = new Mock<IHeappeConnectionService>();
        heappe.Setup(h => h.IsHostAliveAsync(It.IsAny<HeappeIdentifier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        heappe.Setup(h => h.GetClusterCatalogAsync(
                It.IsAny<string>(), It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterInfoModel>, Error>.Success(catalog ?? []));
        heappe.Setup(h => h.GetActiveCommandTemplatesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(allowed ?? []);
        return heappe;
    }

    private static HttpContext Http()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/Projects/PROJ-1/Templates";
        http.Request.Headers["X-Original-Authorization"] = "Bearer test-token";
        return http;
    }

    private static Caller Caller() => new(
        new IdpId("u"),
        "User",
        null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>
        {
            ["ProjectUser"] = new HashSet<ProjectShortName> { ProjectShortName.Create("PROJ-1") },
        });

    private static IList<ClusterInfoModel> Catalog(
        string clusterName, string accountingString, params (long Id, string Name)[] templates)
    {
        var today = DateTime.UtcNow;
        return
        [
            new ClusterInfoModel(
                Id: 10,
                Name: clusterName,
                Description: "cluster",
                FileTransferMethodIds: [1],
                NodeTypes:
                [
                    new NodeTypeModel(
                        Id: 100,
                        Name: "qcpu",
                        Description: "node",
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
                                Name: accountingString,
                                Description: "project",
                                AccountingString: accountingString,
                                StartDate: today.AddDays(-1),
                                EndDate: today.AddDays(30),
                                UsageType: 0,
                                UseAccountingStringForScheduler: false,
                                KeyScriptsDirectoryPath: string.Empty,
                                IsOneToOneMapping: false,
                                CommandTemplates: templates
                                    .Select(t => new CommandTemplateCompactModel(
                                        t.Id, t.Name, "desc", string.Empty, false, true, null, []))
                                    .ToList())
                        ])
                ])
        ];
    }
}
