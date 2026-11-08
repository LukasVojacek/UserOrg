using Core.DataAccess.Queries.Contracts;
using Core.Domain.OnboardingRequest;
using Core.Features.OnboardingRequest.GetMyOnboardingRequests;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Requests;

public sealed class GetMyOnboardingRequestsEndpointTests
{
    private static IOptions<PaginationOptions> Pagination() => Options.Create(new PaginationOptions());

    private static readonly IdpId CallerIdp = new("caller-idp");

    private static readonly ProjectShortName OwnedProject = ProjectShortName.Create("OWN-1");
    private static readonly ProjectShortName ManagedProject = ProjectShortName.Create("MGR-1");
    private static readonly ProjectShortName MemberProject = ProjectShortName.Create("MEM-1");

    private static Caller TheCaller() => new(
        CallerIdp, "User", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>
        {
            ["owner"] = new HashSet<ProjectShortName> { OwnedProject },
            ["Manager"] = new HashSet<ProjectShortName> { ManagedProject },
            ["Member"] = new HashSet<ProjectShortName> { MemberProject },
        });

    private static OnboardingRequestSummary SampleSummary(
        string slug = "req-abc-proj-7-aaa111",
        OnboardingRequestState? state = null,
        OnboardingRequestDirection direction = OnboardingRequestDirection.Sent) =>
        new(
            OnboardingRequestSlug.Create(slug),
            state ?? OnboardingRequestState.Pending,
            ProjectShortName.Create("PROJ-7"),
            new IdpId("idp-7"),
            Email.Create("c@example.org"),
            direction);

    private static DefaultHttpContext HttpContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/Users/Me/OnboardingRequests";
        return http;
    }

    private static async Task<IResult> InvokeAsync(
        GetMyOnboardingRequestsEndpoint.Request request,
        List<OnboardingRequestSummary>? summaries = null,
        Action<OnboardingRequestListFilter>? captureFilter = null)
    {
        var items = summaries ?? [];
        var query = new Mock<IOnboardingRequestQuery>();
        query.Setup(q => q.GetForCallerAsync(It.IsAny<OnboardingRequestListFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<OnboardingRequestListFilter, int, int, CancellationToken>((filter, _, _, _) => captureFilter?.Invoke(filter))
            .ReturnsAsync(new QueryPage<OnboardingRequestSummary>(items, items.Count));

        return await GetMyOnboardingRequestsEndpoint.HandleAsync(
            HttpContext(), request, TheCaller(), new PageRequest(null, null), query.Object, Pagination(), Options.Create(new WellKnownRoles()), CancellationToken.None);
    }

    private static async Task<OnboardingRequestListFilter> CaptureFilterAsync(GetMyOnboardingRequestsEndpoint.Request request)
    {
        OnboardingRequestListFilter? captured = null;
        var result = await InvokeAsync(request, captureFilter: f => captured = f);
        Assert.IsType<Ok<IReadOnlyList<GetMyOnboardingRequestsEndpoint.Response>>>(result);
        return Assert.IsType<OnboardingRequestListFilter>(captured);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static string? CodeOf(IResult result) =>
        Assert.IsType<ProblemHttpResult>(result).ProblemDetails.Extensions["code"] as string;

    [Fact]
    public async Task Should_QueryBothHalves_WithOwnerAndManagerProjectsOnly_When_NoDirection()
    {
        var filter = await CaptureFilterAsync(new GetMyOnboardingRequestsEndpoint.Request(null, null, null));

        Assert.Equal(CallerIdp, filter.Requester);
        Assert.Equal(
            new HashSet<ProjectShortName> { OwnedProject, ManagedProject },
            filter.ManagedProjects.ToHashSet());
        Assert.Null(filter.Project);
        Assert.Null(filter.State);
    }

    [Fact]
    public async Task Should_PassNoManagedProjects_When_DirectionIsSent()
    {
        var filter = await CaptureFilterAsync(new GetMyOnboardingRequestsEndpoint.Request(null, "Sent", null));

        Assert.Equal(CallerIdp, filter.Requester);
        Assert.Empty(filter.ManagedProjects);
    }

    [Fact]
    public async Task Should_PassNoRequester_When_DirectionIsReceived()
    {
        var filter = await CaptureFilterAsync(new GetMyOnboardingRequestsEndpoint.Request(null, "Received", null));

        Assert.Null(filter.Requester);
        Assert.Equal(
            new HashSet<ProjectShortName> { OwnedProject, ManagedProject },
            filter.ManagedProjects.ToHashSet());
    }

    [Fact]
    public async Task Should_PassProjectAndState_When_FiltersGiven()
    {
        var filter = await CaptureFilterAsync(new GetMyOnboardingRequestsEndpoint.Request("Rejected", null, "MGR-1"));

        Assert.Equal(ManagedProject, filter.Project);
        Assert.Equal(OnboardingRequestState.Rejected.Value, filter.State);
    }

    [Fact]
    public async Task Should_ReturnInvalidFormat_When_ProjectShortNameMalformed()
    {
        var result = await InvokeAsync(new GetMyOnboardingRequestsEndpoint.Request(null, null, "x"));

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(CommonErrorCodes.InvalidFormat, CodeOf(result));
    }

    [Theory]
    [InlineData("Bogus")]
    [InlineData("sent")]
    [InlineData("1")]
    [InlineData("Sent,Received")]
    [InlineData("received")]
    public async Task Should_ReturnInvalidDirection_When_DirectionUnknownLowercaseOrNumeric(string direction)
    {
        var result = await InvokeAsync(new GetMyOnboardingRequestsEndpoint.Request(null, direction, null));

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(OnboardingRequestErrorCodes.InvalidDirection, CodeOf(result));
    }

    [Fact]
    public async Task Should_MapDirection_When_ResultsExist()
    {
        var result = await InvokeAsync(
            new GetMyOnboardingRequestsEndpoint.Request(null, null, null),
            summaries:
            [
                SampleSummary("req-mine-proj-7-aaa111", direction: OnboardingRequestDirection.Sent),
                SampleSummary("req-them-proj-7-bbb222", direction: OnboardingRequestDirection.Received),
            ]);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetMyOnboardingRequestsEndpoint.Response>>>(result);
        Assert.Equal(["Sent", "Received"], ok.Value!.Select(i => i.Direction));
    }

    [Fact]
    public async Task Should_CarrySlug_InPagedEnvelope_When_ResultsExist()
    {
        var result = await InvokeAsync(
            new GetMyOnboardingRequestsEndpoint.Request(null, null, null),
            summaries: [SampleSummary("req-mine-proj-7-xyz789")]);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetMyOnboardingRequestsEndpoint.Response>>>(result);
        var item = Assert.Single(ok.Value!);
        Assert.Equal("req-mine-proj-7-xyz789", item.Slug);
        Assert.Equal("Pending", item.State);
        Assert.Equal("PROJ-7", item.ProjectShortName);
        Assert.Equal("idp-7", item.IdpUserId);
        Assert.Equal("c@example.org", item.ContactEmail);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_WithInvalidStateCode_When_StateNameUnknown()
    {
        var result = await InvokeAsync(new GetMyOnboardingRequestsEndpoint.Request("Bogus", null, null));

        Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(OnboardingRequestErrorCodes.InvalidState, CodeOf(result));
    }
}
