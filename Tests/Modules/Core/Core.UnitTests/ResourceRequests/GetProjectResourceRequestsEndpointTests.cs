using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.ResourceRequest.GetProjectResourceRequests;
using Core.Shared.Auth;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.ResourceRequests;

/// <summary>
/// Endpoint unit tests for <see cref="GetProjectResourceRequestsEndpoint"/> (B05, route 2): scope anti-leak,
/// projected list shape, state-name filter validation and offset paging headers.
/// </summary>
public sealed class GetProjectResourceRequestsEndpointTests
{
    private static readonly ProjectShortName Short = ProjectShortName.Create("proj");
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    private static IOptions<PaginationOptions> Pagination() => Options.Create(new PaginationOptions());

    private static Caller InScope() => new(
        new IdpId("mgr"), "Manager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Manager"] = new HashSet<ProjectShortName> { Short } });

    private static Caller OutOfScope() => new(
        new IdpId("mgr"), "Manager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static ResourceRequestSummary Summary(string slug = "proj-gpu", string state = "PendingPiApproval") =>
        new(slug, state, "proj", "acme", "gpu", "idp-1", Now, Now);

    private static DefaultHttpContext HttpContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/Projects/proj/ResourceRequests";
        return http;
    }

    private static async Task<IResult> InvokeAsync(
        GetProjectResourceRequestsEndpoint.Request request,
        List<ResourceRequestSummary>? summaries = null,
        int? totalItems = null,
        PageRequest? page = null,
        Caller? caller = null,
        Action<string, string?, int, int>? captureCall = null,
        DefaultHttpContext? http = null)
    {
        var items = summaries ?? [];
        var query = new Mock<IProjectResourceRequestQuery>();
        query.Setup(q => q.ListByProjectAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, int, int, CancellationToken>((sn, st, pg, lim, _) => captureCall?.Invoke(sn, st, pg, lim))
            .ReturnsAsync(new QueryPage<ResourceRequestSummary>(items, totalItems ?? items.Count));

        return await GetProjectResourceRequestsEndpoint.HandleAsync(
            http ?? HttpContext(), Short, request, caller ?? InScope(), page ?? new PageRequest(null, null),
            query.Object, Pagination(), CancellationToken.None);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static string? CodeOf(IResult result) =>
        Assert.IsType<ProblemHttpResult>(result).ProblemDetails.Extensions["code"] as string;

    [Fact]
    public async Task Should_ProjectAllFields_When_ItemsExist()
    {
        string? queried = null;
        var result = await InvokeAsync(
            new GetProjectResourceRequestsEndpoint.Request(null),
            summaries: [Summary()],
            captureCall: (sn, _, _, _) => queried = sn);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectResourceRequestsEndpoint.Response>>>(result);
        var item = Assert.Single(ok.Value!);
        Assert.Equal("proj-gpu", item.Slug);
        Assert.Equal("PendingPiApproval", item.State);
        Assert.Equal("acme", item.ProviderName);
        Assert.Equal("gpu", item.ResourceName);
        Assert.Equal("idp-1", item.RequesterIdpId);
        Assert.Equal(Now, item.CreatedAt);
        Assert.Equal("proj", queried);
    }

    [Fact]
    public async Task Should_ReturnProjectNotFound_When_CallerOutOfScope()
    {
        var result = await InvokeAsync(new GetProjectResourceRequestsEndpoint.Request(null), caller: OutOfScope());

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        Assert.Equal("project.not_found", CodeOf(result));
    }

    [Fact]
    public async Task Should_PassStateName_When_StateProvided()
    {
        string? captured = null;
        await InvokeAsync(
            new GetProjectResourceRequestsEndpoint.Request("AwaitingResourceCreation"),
            captureCall: (_, st, _, _) => captured = st);

        Assert.Equal("AwaitingResourceCreation", captured);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_StateNameUnknown()
    {
        var result = await InvokeAsync(new GetProjectResourceRequestsEndpoint.Request("Bogus"));

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal("resource_request.invalid_state", CodeOf(result));
    }

    [Fact]
    public async Task Should_HonorLimit_AndFlagNextPage_When_MoreResultsExist()
    {
        var http = HttpContext();
        var result = await InvokeAsync(
            new GetProjectResourceRequestsEndpoint.Request(null),
            summaries: [Summary("proj-gpu"), Summary("proj-fpga")],
            totalItems: 5,
            page: new PageRequest(1, 2),
            http: http);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectResourceRequestsEndpoint.Response>>>(result);
        Assert.Equal(2, ok.Value!.Count);
        Assert.Equal("5", http.Response.Headers["X-Total-Count"]);
        Assert.Contains("rel=\"next\"", http.Response.Headers.Link.ToString(), StringComparison.Ordinal);
    }
}
