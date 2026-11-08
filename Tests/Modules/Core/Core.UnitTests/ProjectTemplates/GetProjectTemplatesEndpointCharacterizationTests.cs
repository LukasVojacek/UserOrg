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

/// <summary>
/// Characterization tests pinning the observable behaviour of <see cref="GetProjectTemplatesEndpoint.HandleAsync"/>
/// before its guard and catalog-building steps are extracted (T-197-03). They pass on the unchanged code.
/// </summary>
public sealed class GetProjectTemplatesEndpointCharacterizationTests
{
    private static readonly HeappeIdentifier Id1 = new("heappe-1");
    private static readonly HeappeIdentifier Id2 = new("heappe-2");

    [Fact]
    public async Task Should_ReturnProjectNotFound_When_CallerOutOfScope()
    {
        var heappe = Heappe();
        var query = Query(Link(Id1, "RES-A", "Karolina"));

        var result = await InvokeAsync(Http(), heappe, query, caller: OutOfScopeCaller());

        AssertProblem(result, StatusCodes.Status404NotFound, "project.not_found");
        query.VerifyNoOtherCalls();
        heappe.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic abc")]
    [InlineData("Bearer ")]
    public async Task Should_ReturnUnauthorized_When_ForwardedAuthorizationIsNotAUsableBearer(string? header)
    {
        var http = Http(header);
        var query = Query(Link(Id1, "RES-A", "Karolina"));

        var result = await InvokeAsync(http, Heappe(), query);

        Assert.IsType<UnauthorizedHttpResult>(result);
        query.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_AcceptLowercaseBearerScheme()
    {
        var heappe = Heappe(catalog: Catalog("Karolina", ("RES-A", [(1, "T1")])));

        var result = await InvokeAsync(Http("bearer tok"), heappe, Query(Link(Id1, "RES-A", "Karolina")), ct: TestContext.Current.CancellationToken);

        Assert.Single(Entries(result));
        heappe.Verify(h => h.GetClusterCatalogAsync("PROJ-1", Id1, "tok", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnProjectNotFound_When_ProjectHasNoLinkData()
    {
        var query = new Mock<IProjectQuery>();
        query.Setup(q => q.GetProjectHeappeLinksAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ProjectHeappeLinkEntry>?)null);
        var heappe = Heappe();

        var result = await InvokeAsync(Http(), heappe, query);

        AssertProblem(result, StatusCodes.Status404NotFound, "project.not_found");
        heappe.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReturnEmptyCacheablePage_When_ProjectHasNoLinks()
    {
        var http = Http();
        var heappe = Heappe();

        var result = await InvokeAsync(http, heappe, Query());

        Assert.Empty(Entries(result));
        Assert.Equal("0", http.Response.Headers["X-Total-Count"]);
        Assert.False(ResponseCacheControl.IsStorageSuppressed(http));
        heappe.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ProcessOnlyActiveLinks_When_SomeLinksAreOutsideTheirWindow()
    {
        var today = DateTimeOffset.UtcNow.Date;
        var active = Link(Id1, "RES-A", "Karolina");
        var future = Link(Id2, "RES-B", "Barbora") with { StartDate = today.AddDays(1), EndDate = today.AddDays(5) };
        var heappe = Heappe(catalog: Catalog("Karolina", ("RES-A", [(1, "T1")])));

        var result = await InvokeAsync(Http(), heappe, Query(active, future));

        var entry = Assert.Single(Entries(result));
        Assert.Equal("Karolina", entry.ClusterName);
        heappe.Verify(h => h.IsHostAliveAsync(Id2, It.IsAny<CancellationToken>()), Times.Never);
        heappe.Verify(h => h.GetActiveCommandTemplatesAsync(active.ProjectId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_MarkEntriesUnavailableAndSuppressCache_When_CatalogResponseIsNull()
    {
        var heappe = Heappe();
        heappe.Setup(h => h.GetClusterCatalogAsync(It.IsAny<string>(), It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result<IList<ClusterInfoModel>, Error>)null!);

        await AssertWholeGroupUnavailableAsync(heappe);
    }

    [Fact]
    public async Task Should_MarkEntriesUnavailableAndSuppressCache_When_CatalogResponseIsFailure()
    {
        var heappe = Heappe();
        heappe.Setup(h => h.GetClusterCatalogAsync(It.IsAny<string>(), It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterInfoModel>, Error>.Failure(new Error("upstream-error", ErrorSeverity.Error, "boom")));

        await AssertWholeGroupUnavailableAsync(heappe);
    }

    [Fact]
    public async Task Should_MarkEntriesUnavailableAndSuppressCache_When_CatalogCallTimesOut()
    {
        var heappe = Heappe();
        heappe.Setup(h => h.GetClusterCatalogAsync(It.IsAny<string>(), It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("timeout"));

        await AssertWholeGroupUnavailableAsync(heappe);
    }

    [Fact]
    public async Task Should_MarkEntriesUnavailableAndSuppressCache_When_CatalogCallIsCanceledByTheCaller()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var heappe = Heappe();
        heappe.Setup(h => h.GetClusterCatalogAsync(It.IsAny<string>(), It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("canceled"));

        await AssertWholeGroupUnavailableAsync(heappe, cts.Token);
    }

    [Fact]
    public async Task Should_MarkEntriesUnavailableAndSuppressCache_When_CatalogCallThrowsUnexpectedly()
    {
        var heappe = Heappe();
        heappe.Setup(h => h.GetClusterCatalogAsync(It.IsAny<string>(), It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected"));

        await AssertWholeGroupUnavailableAsync(heappe);
    }

    [Fact]
    public async Task Should_Return200WithOnlyUnavailableEntries_When_EveryHeappeInstanceFails()
    {
        var heappe = Heappe();
        heappe.Setup(h => h.IsHostAliveAsync(Id1, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        heappe.Setup(h => h.GetClusterCatalogAsync(It.IsAny<string>(), Id2, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("timeout"));
        var http = Http();

        var result = await InvokeAsync(http, heappe, Query(Link(Id1, "RES-A", "Karolina"), Link(Id2, "RES-B", "Barbora")), ct: TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectTemplatesEndpoint.Response>>>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Equal(["Barbora", "Karolina"], ok.Value!.Select(e => e.ClusterName).Order().ToArray());
        Assert.All(ok.Value!, e => Assert.False(e.IsAvailable));
        Assert.True(ResponseCacheControl.IsStorageSuppressed(http));
    }

    [Fact]
    public async Task Should_PropagateTheException_When_AllowedTemplateReadFails()
    {
        var heappe = Heappe();
        heappe.Setup(h => h.GetActiveCommandTemplatesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("integration db down"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeAsync(Http(), heappe, Query(Link(Id1, "RES-A", "Karolina")), ct: TestContext.Current.CancellationToken));

        heappe.Verify(h => h.IsHostAliveAsync(It.IsAny<HeappeIdentifier>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_KeepAllTemplates_When_AllowedTemplateReadReturnsNull()
    {
        var heappe = Heappe(catalog: Catalog("Karolina", ("RES-A", [(1, "T1"), (2, "T2")])));
        heappe.Setup(h => h.GetActiveCommandTemplatesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<CommandTemplatePartitionResponse>)null!);

        var result = await InvokeAsync(Http(), heappe, Query(Link(Id1, "RES-A", "Karolina")), ct: TestContext.Current.CancellationToken);

        var node = Assert.Single(Assert.Single(Entries(result)).Nodes);
        Assert.Equal([1L, 2L], Assert.Single(node.Allocations).CommandTemplates.Select(c => c.Id).Order().ToArray());
    }

    [Fact]
    public async Task Should_MapEveryCatalogFieldIntoTheResponse()
    {
        var link = Link(Id1, "RES-A", "Karolina");
        var heappe = Heappe(catalog: Catalog("Karolina", ("RES-A", [(7, "Seven")])));

        var result = await InvokeAsync(Http(), heappe, Query(link));

        var entry = Assert.Single(Entries(result));
        Assert.True(entry.IsAvailable);
        Assert.Equal("Karolina", entry.ClusterName);
        Assert.Equal("Karolina", entry.LocationId);
        Assert.Equal(10, entry.ClusterId);
        var node = Assert.Single(entry.Nodes);
        Assert.Equal(100, node.NodeId);
        Assert.Equal("qcpu", node.NodeType);
        Assert.Equal(4, node.NumberOfNodes);
        Assert.Equal(128, node.ClusterPerNode);
        Assert.Equal(3600, node.MaxWallTime);
        Assert.Equal(1, node.FileTransferMethodId);
        var allocation = Assert.Single(node.Allocations);
        Assert.Equal("RES-A", allocation.LexisResourceName);
        var template = Assert.Single(allocation.CommandTemplates);
        Assert.Equal(7, template.Id);
        Assert.Equal("Seven", template.Name);
        Assert.False(template.IsGeneric);
        var parameter = Assert.Single(template.Parameters);
        Assert.Equal("param-7", parameter.Identifier);
        Assert.Equal("desc-7", parameter.Description);
    }

    [Fact]
    public async Task Should_TagResponseByResourceAndLocation()
    {
        var link = Link(Id1, "RES-A", "Karolina");
        var http = Http();

        await InvokeAsync(http, Heappe(catalog: Catalog("Karolina", ("RES-A", [(1, "T1")]))), Query(link));

        var tags = ResponseCacheTags.Get(http);
        Assert.Contains(CacheTags.ProjectResource(link.ResourceId), tags);
        Assert.Contains(CacheTags.Location("Karolina"), tags);
    }

    [Fact]
    public async Task Should_MatchClusterNameIgnoringCase()
    {
        var heappe = Heappe(catalog: Catalog("KAROLINA", ("RES-A", [(1, "T1")])));

        var result = await InvokeAsync(Http(), heappe, Query(Link(Id1, "RES-A", "karolina")), ct: TestContext.Current.CancellationToken);

        var entry = Assert.Single(Entries(result));
        Assert.True(entry.IsAvailable);
        Assert.Equal("KAROLINA", entry.ClusterName);
    }

    [Fact]
    public async Task Should_KeepTheNodeWithNoAllocations_When_AllowedSetFiltersOutEveryTemplate()
    {
        var heappe = Heappe(
            catalog: Catalog("Karolina", ("RES-A", [(1, "T1")])),
            allowed: [new CommandTemplatePartitionResponse("Other", 99)]);
        var http = Http();

        var result = await InvokeAsync(http, heappe, Query(Link(Id1, "RES-A", "Karolina")), ct: TestContext.Current.CancellationToken);

        var entry = Assert.Single(Entries(result));
        Assert.True(entry.IsAvailable);
        Assert.Empty(Assert.Single(entry.Nodes).Allocations);
        Assert.False(ResponseCacheControl.IsStorageSuppressed(http));
    }

    [Fact]
    public async Task Should_ExcludeTheNode_When_ItsOnlyAllocationStartsExactlyToday()
    {
        var today = DateTimeOffset.UtcNow.Date;
        var catalog = Catalog("Karolina", ("RES-A", [(1, "T1")]), start: today, end: today.AddDays(30));

        var result = await InvokeAsync(Http(), Heappe(catalog: catalog), Query(Link(Id1, "RES-A", "Karolina")), ct: TestContext.Current.CancellationToken);

        var entry = Assert.Single(Entries(result));
        Assert.True(entry.IsAvailable);
        Assert.Empty(entry.Nodes);
    }

    [Fact]
    public async Task Should_MergeNodesAndAllocations_When_TwoResourcesResolveToTheSameCluster()
    {
        var heappe = Heappe(catalog: Catalog("Karolina", ("RES-A", [(1, "T1"), (2, "T2")]), ("RES-B", [(2, "T2"), (3, "T3")])));

        var result = await InvokeAsync(
            Http(), heappe, Query(Link(Id1, "RES-A", "Karolina"), Link(Id1, "RES-B", "Karolina")));

        var entry = Assert.Single(Entries(result));
        Assert.True(entry.IsAvailable);
        var node = Assert.Single(entry.Nodes);
        Assert.Equal(["RES-A", "RES-B"], node.Allocations.Select(a => a.LexisResourceName).Order().ToArray());
        Assert.Equal([1L, 2L], node.Allocations.Single(a => a.LexisResourceName == "RES-A").CommandTemplates.Select(c => c.Id).Order().ToArray());
        Assert.Equal([2L, 3L], node.Allocations.Single(a => a.LexisResourceName == "RES-B").CommandTemplates.Select(c => c.Id).Order().ToArray());
    }

    [Fact]
    public async Task Should_DeduplicateTemplatesById_When_TheSameResourceIsLinkedTwiceToOneCluster()
    {
        var heappe = Heappe(catalog: Catalog("Karolina", ("RES-A", [(1, "T1"), (2, "T2")])));

        var result = await InvokeAsync(
            Http(), heappe, Query(Link(Id1, "RES-A", "Karolina"), Link(Id2, "RES-A", "Karolina")));

        var node = Assert.Single(Assert.Single(Entries(result)).Nodes);
        var allocation = Assert.Single(node.Allocations);
        Assert.Equal([1L, 2L], allocation.CommandTemplates.Select(c => c.Id).Order().ToArray());
    }

    [Fact]
    public async Task Should_DropTheUnavailableDuplicate_When_AnotherLinkMakesTheClusterAvailable()
    {
        var heappe = Heappe(catalog: Catalog("Karolina", ("RES-A", [(1, "T1")])));
        heappe.Setup(h => h.IsHostAliveAsync(Id2, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var http = Http();

        var result = await InvokeAsync(
            http, heappe, Query(Link(Id1, "RES-A", "Karolina"), Link(Id2, "RES-B", "Karolina")));

        var entry = Assert.Single(Entries(result));
        Assert.True(entry.IsAvailable);
        Assert.True(ResponseCacheControl.IsStorageSuppressed(http));
    }

    [Fact]
    public async Task Should_EmitOneUnavailableEntry_When_NoLinkMakesTheClusterAvailable()
    {
        var heappe = Heappe();
        heappe.Setup(h => h.IsHostAliveAsync(It.IsAny<HeappeIdentifier>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var first = Link(Id1, "RES-A", "Karolina");

        var result = await InvokeAsync(Http(), heappe, Query(first, Link(Id2, "RES-B", "Karolina")), ct: TestContext.Current.CancellationToken);

        var entry = Assert.Single(Entries(result));
        Assert.False(entry.IsAvailable);
        Assert.Equal("Karolina", entry.ClusterName);
        Assert.Null(entry.ClusterId);
        Assert.Empty(entry.Nodes);
    }

    [Fact]
    public async Task Should_DropEntries_When_LinkHasNoLocationId()
    {
        var heappe = Heappe();
        heappe.Setup(h => h.IsHostAliveAsync(It.IsAny<HeappeIdentifier>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var http = Http();

        var result = await InvokeAsync(http, heappe, Query(Link(Id1, "RES-A", "Karolina") with { LocationId = string.Empty }), ct: TestContext.Current.CancellationToken);

        Assert.Empty(Entries(result));
        Assert.Equal("0", http.Response.Headers["X-Total-Count"]);
        Assert.True(ResponseCacheControl.IsStorageSuppressed(http));
    }

    [Fact]
    public async Task Should_ReportUnavailableAndSuppressCache_When_ALinkOnAUrlLacksAnAllocation()
    {
        var heappe = Heappe(catalog: Catalog("Karolina", ("RES-A", [(1, "T1")])));
        var http = Http();

        var result = await InvokeAsync(
            http, heappe, Query(Link(Id1, "RES-A", "Karolina"), Link(Id1, "RES-Z", "Barbora"), Link(Id1, "RES-Y", "Karolina") with { LocationId = "K2" }));

        var entries = Entries(result);
        Assert.True(entries.Single(e => e.LocationId == "Karolina").IsAvailable);
        Assert.False(entries.Single(e => e.LocationId == "Barbora").IsAvailable);
        Assert.False(entries.Single(e => e.LocationId == "K2").IsAvailable);
        Assert.True(ResponseCacheControl.IsStorageSuppressed(http));
    }

    private static async Task AssertWholeGroupUnavailableAsync(Mock<IHeappeConnectionService> heappe, CancellationToken ct = default)
    {
        var http = Http();

        var result = await InvokeAsync(
            http, heappe, Query(Link(Id1, "RES-A", "Karolina"), Link(Id1, "RES-B", "Barbora")), ct: ct);

        var entries = Entries(result);
        Assert.Equal(["Barbora", "Karolina"], entries.Select(e => e.ClusterName).Order().ToArray());
        Assert.All(entries, e =>
        {
            Assert.False(e.IsAvailable);
            Assert.Null(e.ClusterId);
            Assert.Empty(e.Nodes);
        });
        Assert.True(ResponseCacheControl.IsStorageSuppressed(http));
    }

    private static IReadOnlyList<GetProjectTemplatesEndpoint.Response> Entries(IResult result)
        => Assert.IsType<Ok<IReadOnlyList<GetProjectTemplatesEndpoint.Response>>>(result).Value!;

    private static void AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
    }

    private static Task<IResult> InvokeAsync(
        HttpContext http,
        Mock<IHeappeConnectionService> heappe,
        Mock<IProjectQuery> query,
        Caller? caller = null,
        CancellationToken ct = default)
        => GetProjectTemplatesEndpoint.HandleAsync(
            http,
            ProjectShortName.Create("PROJ-1"),
            caller ?? InScopeCaller(),
            query.Object,
            heappe.Object,
            NullLoggerFactory.Instance,
            ct);

    private static ProjectHeappeLinkEntry Link(HeappeIdentifier identifier, string resourceName, string location)
    {
        var today = DateTimeOffset.UtcNow.Date;
        return new ProjectHeappeLinkEntry(
            ProjectId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
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

    private static HttpContext Http(string? forwardedAuthorization = "Bearer test-token")
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/Projects/PROJ-1/Templates";
        if (forwardedAuthorization is not null)
            http.Request.Headers["X-Original-Authorization"] = forwardedAuthorization;
        return http;
    }

    private static Caller InScopeCaller() => new(
        new IdpId("u"),
        "User",
        null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>
        {
            ["ProjectUser"] = new HashSet<ProjectShortName> { ProjectShortName.Create("PROJ-1") },
        });

    private static Caller OutOfScopeCaller() => new(
        new IdpId("u"),
        "User",
        null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>
        {
            ["ProjectUser"] = new HashSet<ProjectShortName> { ProjectShortName.Create("OTHER-1") },
        });

    private static IList<ClusterInfoModel> Catalog(
        string clusterName,
        params (string AccountingString, (long Id, string Name)[] Templates)[] allocations)
        => Catalog(clusterName, allocations, start: null, end: null);

    private static IList<ClusterInfoModel> Catalog(
        string clusterName,
        (string AccountingString, (long Id, string Name)[] Templates) allocation,
        DateTime start,
        DateTime end)
        => Catalog(clusterName, [allocation], start, end);

    private static IList<ClusterInfoModel> Catalog(
        string clusterName,
        (string AccountingString, (long Id, string Name)[] Templates)[] allocations,
        DateTime? start,
        DateTime? end)
    {
        var now = DateTime.UtcNow;
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
                        Projects: allocations
                            .Select((a, i) => new ProjectModel(
                                Id: 1000 + i,
                                Name: a.AccountingString,
                                Description: "project",
                                AccountingString: a.AccountingString,
                                StartDate: start ?? now.AddDays(-1),
                                EndDate: end ?? now.AddDays(30),
                                UsageType: 0,
                                UseAccountingStringForScheduler: false,
                                KeyScriptsDirectoryPath: string.Empty,
                                IsOneToOneMapping: false,
                                CommandTemplates: a.Templates
                                    .Select(t => new CommandTemplateCompactModel(
                                        t.Id, t.Name, "desc", string.Empty, false, true, null,
                                        [new CommandTemplateParameterCompactModel($"param-{t.Id}", $"desc-{t.Id}")]))
                                    .ToList()))
                            .ToList())
                ])
        ];
    }
}
