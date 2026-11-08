using Core.DataAccess.Queries.Contracts;
using Core.Domain.Request;
using Core.Features.Request.GetRequests;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Requests;

public sealed class GetRequestsEndpointTests
{
    private static IOptions<PaginationOptions> Pagination() => Options.Create(new PaginationOptions());

    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly WellKnownRoles Roles = new();

    private static Caller CallerInScope() => new(
        new IdpId("u"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private static Caller AdminCaller() => new(
        new IdpId("a"), Roles.DefaultAdminRoleName, null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static IOptions<WellKnownRoles> RoleOptions() => Options.Create(Roles);

    private static RequestSummary SampleSummary() => new(
        Email.Create("c@example.org"), RequestType.SelfOnboarding, RequestState.Pending,
        ShortName, new IdpId("idp-1"), null, null);

    private static DefaultHttpContext HttpContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/Requests";
        return http;
    }

    private static async Task<IResult> InvokeAsync(
        GetRequestsEndpoint.Request request,
        List<RequestSummary>? summaries = null,
        int? totalItems = null,
        PageRequest? page = null,
        Action<RequestFilter>? captureFilter = null,
        Caller? caller = null)
    {
        var items = summaries ?? [];
        var query = new Mock<IRequestQuery>();
        query.Setup(q => q.GetRequestsAsync(It.IsAny<RequestFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<RequestFilter, int, int, CancellationToken>((f, _, _, _) => captureFilter?.Invoke(f))
            .ReturnsAsync(new QueryPage<RequestSummary>(items, totalItems ?? items.Count));

        return await GetRequestsEndpoint.HandleAsync(
            HttpContext(), request, caller ?? CallerInScope(), page ?? new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static string? CodeOf(IResult result) =>
        Assert.IsType<ProblemHttpResult>(result).ProblemDetails.Extensions["code"] as string;

    [Fact]
    public async Task Should_PassCallerScopeToFilter_And_ReturnEmptyEnvelope_When_NoMatches()
    {
        RequestFilter? captured = null;

        var result = await InvokeAsync(new GetRequestsEndpoint.Request(null, null, null), captureFilter: f => captured = f);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetRequestsEndpoint.Response>>>(result);
        Assert.Empty(ok.Value!);
        Assert.NotNull(captured);
        Assert.Contains(ShortName, captured!.ScopeShortNames!);
    }

    [Fact]
    public async Task Should_NotScopeFilter_When_CallerIsAdmin()
    {
        RequestFilter? captured = null;

        await InvokeAsync(new GetRequestsEndpoint.Request(null, null, null), captureFilter: f => captured = f, caller: AdminCaller());

        // Admin sees every request — no scope filter applied.
        Assert.NotNull(captured);
        Assert.Null(captured!.ScopeShortNames);
    }

    [Fact]
    public async Task Should_PassNaturalKeyFilters_When_Provided()
    {
        RequestFilter? captured = null;

        await InvokeAsync(new GetRequestsEndpoint.Request("PROJ-1", "idp-9", "PendingForHPCProject"), captureFilter: f => captured = f);

        Assert.NotNull(captured);
        Assert.Equal(ShortName, captured!.ProjectShortName);
        Assert.Equal(new IdpId("idp-9"), captured.UserIdpId);
        Assert.Equal(RequestState.PendingForHPCProject.Value, captured.State);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_ProjectShortNameFilterMalformed()
    {
        var result = await InvokeAsync(new GetRequestsEndpoint.Request("x", null, null));

        Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
    }

    [Fact]
    public async Task Should_FilterByPendingName_When_StateIsPending()
    {
        RequestFilter? captured = null;

        await InvokeAsync(new GetRequestsEndpoint.Request(null, null, "Pending"), captureFilter: f => captured = f);

        Assert.NotNull(captured);
        Assert.Equal(RequestState.Pending.Value, captured!.State);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_WithInvalidStateCode_When_StateNameUnknown()
    {
        var result = await InvokeAsync(new GetRequestsEndpoint.Request(null, null, "Bogus"));

        Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(RequestErrorCodes.InvalidState, CodeOf(result));
    }

    [Fact]
    public async Task Should_EmitEnumNames_When_MappingSummary()
    {
        var summary = new RequestSummary(
            Email.Create("c@example.org"), RequestType.SelfOnboarding, RequestState.Pending,
            ShortName, new IdpId("idp-1"), null, null);

        var result = await InvokeAsync(new GetRequestsEndpoint.Request(null, null, null), summaries: [summary]);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetRequestsEndpoint.Response>>>(result);
        var item = Assert.Single(ok.Value!);
        Assert.Equal("SelfOnboarding", item.Type);
        Assert.Equal("Pending", item.State);
        Assert.Equal("c@example.org", item.ContactEmail);
    }

    [Fact]
    public async Task Should_ExposeNaturalKeys_When_MappingSummary()
    {
        var summary = new RequestSummary(
            Email.Create("c@example.org"), RequestType.SelfOnboarding, RequestState.Pending,
            ShortName, new IdpId("idp-1"), ProviderName.Create("LUMI"), ResourceName.Create("hpc-1"));

        var result = await InvokeAsync(new GetRequestsEndpoint.Request(null, null, null), summaries: [summary]);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetRequestsEndpoint.Response>>>(result);
        var item = Assert.Single(ok.Value!);
        Assert.Equal("PROJ-1", item.ProjectShortName);
        Assert.Equal("idp-1", item.IdpUserId);
        Assert.Equal("LUMI", item.ProviderName);
        Assert.Equal("hpc-1", item.ProjectResourceName);
    }

    [Fact]
    public async Task Should_HonorLimit_AndFlagNextPage_When_MoreResultsExist()
    {
        var http = HttpContext();
        List<RequestSummary> pageItems = [SampleSummary(), SampleSummary()];
        var query = new Mock<IRequestQuery>();
        query.Setup(q => q.GetRequestsAsync(It.IsAny<RequestFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryPage<RequestSummary>(pageItems, 5));

        var result = await GetRequestsEndpoint.HandleAsync(
            http, new GetRequestsEndpoint.Request(null, null, null), CallerInScope(), new PageRequest(1, 2), query.Object, RoleOptions(), Pagination(), CancellationToken.None)
            ;

        var ok = Assert.IsType<Ok<IReadOnlyList<GetRequestsEndpoint.Response>>>(result);
        Assert.Equal(2, ok.Value!.Count);
        Assert.Equal("5", http.Response.Headers["X-Total-Count"]);
        Assert.Contains("rel=\"next\"", http.Response.Headers.Link.ToString());
    }

    [Fact]
    public async Task Should_CapLimit_When_LimitExceedsMax()
    {
        int? capturedLimit = null;
        var query = new Mock<IRequestQuery>();
        query.Setup(q => q.GetRequestsAsync(It.IsAny<RequestFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<RequestFilter, int, int, CancellationToken>((_, _, limit, _) => capturedLimit = limit)
            .ReturnsAsync(new QueryPage<RequestSummary>([], 0));

        await GetRequestsEndpoint.HandleAsync(
            HttpContext(), new GetRequestsEndpoint.Request(null, null, null), CallerInScope(),
            new PageRequest(1, PageRequest.MaxLimit + 1000), query.Object, RoleOptions(), Pagination(), CancellationToken.None)
            ;

        Assert.Equal(PageRequest.MaxLimit, capturedLimit);
    }
}
