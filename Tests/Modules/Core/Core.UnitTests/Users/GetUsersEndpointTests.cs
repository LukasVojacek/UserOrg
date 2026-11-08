using System.Security.Claims;

using Core.DataAccess.Queries.Contracts;
using Core.Features.User.GetUsers;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Users;

public sealed class GetUsersEndpointTests
{
    private static Caller AnyCaller() => new(
        new IdpId("caller-sub"), "Admin", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static GetUsersEndpoint.Request Empty() => new(null, null);

    private static UserSummary SampleSummary(string idp) => new(
        IdpId.Create(idp), Email.Create("t@example.org"), UserName.Create("target"),
        [ProjectShortName.Create("PROJ-1")], null);

    private static DefaultHttpContext HttpContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/Users";
        return http;
    }

    private static async Task<IResult> InvokeAsync(
        GetUsersEndpoint.Request request,
        Caller? caller = null,
        List<UserSummary>? summaries = null,
        int? totalItems = null,
        PageRequest? page = null,
        Action<UserFilter>? captureFilter = null)
    {
        var items = summaries ?? [];
        var query = new Mock<IUserInfoQuery>();
        query.Setup(q => q.GetUsersAsync(It.IsAny<UserFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<UserFilter, int, int, CancellationToken>((f, _, _, _) => captureFilter?.Invoke(f))
            .ReturnsAsync(new QueryPage<UserSummary>(items, totalItems ?? items.Count));

        return await GetUsersEndpoint.HandleAsync(
            HttpContext(), request, caller ?? AnyCaller(), page ?? new PageRequest(null, null), query.Object, Pagination(), CancellationToken.None);
    }

    private static IOptions<PaginationOptions> Pagination() => Options.Create(new PaginationOptions());

    [Fact]
    public async Task Should_ReturnNull_When_SubClaimMissing()
    {
        // The Caller-bound endpoint no longer emits a manual 401; binding yields null (standard challenge)
        // when the token carries no sub, instead of the handler reaching for Results.Problem.
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };

        var caller = await Caller.BindAsync(context);

        Assert.Null(caller);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_ProjectShortNameInvalid()
    {
        var result = await InvokeAsync(new GetUsersEndpoint.Request("x", null));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(CommonErrorCodes.InvalidFormat, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("Invalid project short name.", problem.ProblemDetails.Detail);
    }

    [Theory]
    [InlineData(2)] // below IdpId's 3-character minimum
    [InlineData(IdpId.MaxLenght + 1)] // above its 150-character maximum
    public async Task Should_ReturnValidationProblem_When_IdpUserIdOutOfBounds(int idpUserIdLength)
    {
        // Only IsNullOrWhiteSpace was checked, so a too-short or too-long filter reached IdpId's throwing
        // factory and surfaced as a 500 — unlike the sibling ProjectShortName filter, which already parsed.
        var result = await InvokeAsync(new GetUsersEndpoint.Request(null, new string('a', idpUserIdLength)));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(CommonErrorCodes.InvalidFormat, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("Invalid identity provider user ID.", problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_PassFilter_And_ReturnEmptyList_When_NoMatches()
    {
        UserFilter? captured = null;

        var result = await InvokeAsync(new GetUsersEndpoint.Request("PROJ-1", null), captureFilter: f => captured = f);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetUsersEndpoint.Response>>>(result);
        Assert.Empty(ok.Value!);
        Assert.NotNull(captured);
        Assert.Equal(ProjectShortName.Create("PROJ-1"), captured!.ProjectShortName);
    }

    [Fact]
    public async Task Should_ReturnBareArray_AndTotalCountHeader_When_ResultsExist()
    {
        var http = HttpContext();
        var query = new Mock<IUserInfoQuery>();
        query.Setup(q => q.GetUsersAsync(It.IsAny<UserFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryPage<UserSummary>([SampleSummary("target-idp")], 1));

        var result = await GetUsersEndpoint.HandleAsync(
            http, Empty(), AnyCaller(), new PageRequest(null, null), query.Object, Pagination(), CancellationToken.None);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetUsersEndpoint.Response>>>(result);
        Assert.Single(ok.Value!);
        Assert.Equal("1", http.Response.Headers["X-Total-Count"]);
        Assert.False(http.Response.Headers.ContainsKey("Link")); // single page
    }

    [Fact]
    public async Task Should_SetTotalCount_AndNextLink_When_MoreResultsExist()
    {
        var http = HttpContext();
        List<UserSummary> pageItems = [SampleSummary("idp-1"), SampleSummary("idp-2")];
        var query = new Mock<IUserInfoQuery>();
        query.Setup(q => q.GetUsersAsync(It.IsAny<UserFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryPage<UserSummary>(pageItems, 5));

        var result = await GetUsersEndpoint.HandleAsync(
            http, Empty(), AnyCaller(), new PageRequest(1, 2), query.Object, Pagination(), CancellationToken.None);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetUsersEndpoint.Response>>>(result);
        Assert.Equal(2, ok.Value!.Count);
        Assert.Equal("5", http.Response.Headers["X-Total-Count"]);
        Assert.Contains("rel=\"next\"", http.Response.Headers.Link.ToString());
    }

    [Fact]
    public async Task Should_CapLimit_When_LimitExceedsMax()
    {
        int? capturedLimit = null;
        var query = new Mock<IUserInfoQuery>();
        query.Setup(q => q.GetUsersAsync(It.IsAny<UserFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<UserFilter, int, int, CancellationToken>((_, _, limit, _) => capturedLimit = limit)
            .ReturnsAsync(new QueryPage<UserSummary>([], 0));

        await GetUsersEndpoint.HandleAsync(
            HttpContext(), Empty(), AnyCaller(), new PageRequest(1, PageRequest.MaxLimit + 1000), query.Object, Pagination(), CancellationToken.None)
            ;

        Assert.Equal(PageRequest.MaxLimit, capturedLimit);
    }

    [Fact]
    public async Task Should_MapSummaryToResponse_When_ResultsExist()
    {
        var result = await InvokeAsync(Empty(), summaries: [SampleSummary("target-idp")]);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetUsersEndpoint.Response>>>(result);
        var item = Assert.Single(ok.Value!);
        Assert.Equal("target-idp", item.IdpUserId);
        Assert.Equal("t@example.org", item.Email);
        Assert.Equal("PROJ-1", Assert.Single(item.Projects));
        Assert.Null(item.AssignedProviderName);
    }

    [Fact]
    public async Task Should_ExposeProviderName_When_UserHasAssignedProvider()
    {
        var summary = new UserSummary(
            IdpId.Create("idp-x"), Email.Create("t@example.org"), UserName.Create("target"),
            [ProjectShortName.Create("PROJ-1")], ProviderName.Create("LUMI"));

        var result = await InvokeAsync(Empty(), summaries: [summary]);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetUsersEndpoint.Response>>>(result);
        Assert.Equal("LUMI", Assert.Single(ok.Value!).AssignedProviderName);
    }
}
