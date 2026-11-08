using Core.DataAccess.Queries.Contracts;
using Core.Domain.Request;
using Core.Features.Request.GetRequests;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Requests;

public sealed class GetRequestsEndpointCharacterizationTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");

    private static Caller CallerInScope() => new(
        new IdpId("u"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private sealed record Call(RequestFilter Filter, int Page, int Limit);

    private static async Task<(IResult Result, Call? Call)> InvokeAsync(
        GetRequestsEndpoint.Request request,
        PageRequest? page = null,
        List<RequestSummary>? items = null)
    {
        Call? call = null;
        var query = new Mock<IRequestQuery>();
        query.Setup(q => q.GetRequestsAsync(It.IsAny<RequestFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<RequestFilter, int, int, CancellationToken>((f, p, l, _) => call = new Call(f, p, l))
            .ReturnsAsync(new QueryPage<RequestSummary>(items ?? [], items?.Count ?? 0));

        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/Requests";

        var result = await GetRequestsEndpoint.HandleAsync(
            http, request, CallerInScope(), page ?? new PageRequest(null, null), query.Object,
            Options.Create(new WellKnownRoles()), Options.Create(new PaginationOptions()), CancellationToken.None);
        return (result, call);
    }

    [Fact]
    public async Task Should_TreatBlankFiltersAsAbsent_When_TheyAreWhitespace()
    {
        var (result, call) = await InvokeAsync(new GetRequestsEndpoint.Request(" ", " ", " "));

        Assert.IsType<Ok<IReadOnlyList<GetRequestsEndpoint.Response>>>(result);
        Assert.NotNull(call);
        Assert.Null(call!.Filter.ProjectShortName);
        Assert.Null(call.Filter.UserIdpId);
        Assert.Null(call.Filter.State);
        Assert.Null(call.Filter.SenderIdpId);
        Assert.Equal([ShortName], call.Filter.ScopeShortNames!);
    }

    [Fact]
    public async Task Should_PassTheEffectivePageAndResolvedLimit_When_Paging()
    {
        var (_, call) = await InvokeAsync(new GetRequestsEndpoint.Request(null, null, null), new PageRequest(3, 7));

        Assert.Equal(3, call!.Page);
        Assert.Equal(7, call.Limit);
    }

    [Fact]
    public async Task Should_UseTheFirstPageAndDefaultLimit_When_PagingIsAbsent()
    {
        var (_, call) = await InvokeAsync(new GetRequestsEndpoint.Request(null, null, null), new PageRequest(0, 0));

        Assert.Equal(1, call!.Page);
        Assert.Equal(new PaginationOptions().DefaultLimit, call.Limit);
    }

    [Fact]
    public async Task Should_ReturnInvalidStateWithoutQuerying_When_TheStateNameIsUnknown()
    {
        var (result, call) = await InvokeAsync(new GetRequestsEndpoint.Request(null, null, "Nope"));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(RequestErrorCodes.InvalidState, problem.ProblemDetails.Extensions["code"]);
        Assert.Null(call);
    }

    [Fact]
    public async Task Should_ReturnInvalidFormatWithoutQuerying_When_TheProjectFilterIsMalformed()
    {
        var (result, call) = await InvokeAsync(new GetRequestsEndpoint.Request("x", null, "Pending"));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("validation.invalid_format", problem.ProblemDetails.Extensions["code"]);
        Assert.Null(call);
    }

    [Fact]
    public async Task Should_ReportTheStateError_When_BothTheStateAndTheProjectFilterAreInvalid()
    {
        var (result, _) = await InvokeAsync(new GetRequestsEndpoint.Request("x", null, "Nope"));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(RequestErrorCodes.InvalidState, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_MapEveryField_When_TheSummaryCarriesProviderAndResource()
    {
        var summary = new RequestSummary(
            null, RequestType.ProjectResourceAssignment, RequestState.PendingForPIAccept,
            ShortName, new IdpId("idp-7"), ProviderName.From("acme"), ResourceName.Create("gpu-res"));

        var (result, _) = await InvokeAsync(new GetRequestsEndpoint.Request(null, null, null), items: [summary]);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetRequestsEndpoint.Response>>>(result);
        var row = Assert.Single(ok.Value!);
        Assert.Equal(
            new GetRequestsEndpoint.Response(
                null, RequestType.ProjectResourceAssignment.Name, RequestState.PendingForPIAccept.Name,
                "PROJ-1", "idp-7", "acme", "gpu-res"),
            row);
    }
}
