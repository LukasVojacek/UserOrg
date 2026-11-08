using Core.DataAccess.Queries.Contracts;
using Core.Domain.Request;
using Core.Features.Request.GetSentRequests;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Requests;

public sealed class GetSentRequestsEndpointTests
{
    private static IOptions<PaginationOptions> Pagination() => Options.Create(new PaginationOptions());

    private static readonly IdpId CallerIdp = new("caller-idp");

    private static Caller TheCaller() => new(
        CallerIdp, "User", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static DefaultHttpContext HttpContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/Requests/Sent";
        return http;
    }

    private static async Task<IResult> InvokeAsync(
        GetSentRequestsEndpoint.Request request,
        List<RequestSummary>? summaries = null,
        Action<RequestFilter>? captureFilter = null)
    {
        var items = summaries ?? [];
        var query = new Mock<IRequestQuery>();
        query.Setup(q => q.GetRequestsAsync(It.IsAny<RequestFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<RequestFilter, int, int, CancellationToken>((f, _, _, _) => captureFilter?.Invoke(f))
            .ReturnsAsync(new QueryPage<RequestSummary>(items, items.Count));

        return await GetSentRequestsEndpoint.HandleAsync(
            HttpContext(), request, TheCaller(), new PageRequest(null, null), query.Object, Pagination(), CancellationToken.None);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static string? CodeOf(IResult result) =>
        Assert.IsType<ProblemHttpResult>(result).ProblemDetails.Extensions["code"] as string;

    [Fact]
    public async Task Should_FilterBySenderIdpId_And_NotScope()
    {
        RequestFilter? captured = null;

        var result = await InvokeAsync(new GetSentRequestsEndpoint.Request("Rejected"), captureFilter: f => captured = f);

        Assert.IsType<Ok<IReadOnlyList<GetSentRequestsEndpoint.Response>>>(result);
        Assert.NotNull(captured);
        Assert.Equal(CallerIdp, captured!.SenderIdpId);
        Assert.Equal(RequestState.Rejected.Value, captured.State);
        Assert.Null(captured.ScopeShortNames);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_WithInvalidStateCode_When_StateNameUnknown()
    {
        var result = await InvokeAsync(new GetSentRequestsEndpoint.Request("Bogus"));

        Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(RequestErrorCodes.InvalidState, CodeOf(result));
    }

    [Fact]
    public async Task Should_EmitEnumNames_InPagedEnvelope_When_ResultsExist()
    {
        var summary = new RequestSummary(
            null, RequestType.SelfOnboarding, RequestState.Rejected,
            ProjectShortName.Create("PROJ-7"), new IdpId("idp-7"), null, null);

        var result = await InvokeAsync(new GetSentRequestsEndpoint.Request(null), summaries: [summary]);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetSentRequestsEndpoint.Response>>>(result);
        var item = Assert.Single(ok.Value!);
        Assert.Null(item.ContactEmail);
        Assert.Equal("SelfOnboarding", item.Type);
        Assert.Equal("Rejected", item.State);
        Assert.Equal("PROJ-7", item.ProjectShortName);
    }
}
