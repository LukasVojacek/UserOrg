using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Project.GetProjects;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Projects;

public sealed class GetProjectsEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly WellKnownRoles Roles = new();

    private static Caller CallerInScope() => new(
        new IdpId("u"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private static Caller AdminCaller() => new(
        new IdpId("a"), Roles.DefaultAdminRoleName, null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static IOptions<WellKnownRoles> RoleOptions() => Options.Create(Roles);

    private static GetProjectsEndpoint.Request Empty() => new(null, null, null, null, null);

    private static ProjectSummary SampleSummary(string shortName) => new(
        ProjectShortName.Create(shortName), ProjectName.Create("Project One"), Email.Create("p@example.org"),
        null, EOperationMode.Editable, EProjectState.Active, ServiceName.Default,
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero));

    private static DefaultHttpContext HttpContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/Projects";
        return http;
    }

    private static Mock<IProjectQuery> QueryReturning(
        List<ProjectSummary> items, int totalItems, Action<ProjectFilter>? captureFilter)
    {
        var query = new Mock<IProjectQuery>();
        query.Setup(q => q.GetProjectsAsync(It.IsAny<ProjectFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<ProjectFilter, int, int, CancellationToken>((f, _, _, _) => captureFilter?.Invoke(f))
            .ReturnsAsync(new QueryPage<ProjectSummary>(items, totalItems));
        return query;
    }

    private static async Task<IResult> InvokeAsync(
        GetProjectsEndpoint.Request request,
        Caller? caller = null,
        List<ProjectSummary>? summaries = null,
        int? totalItems = null,
        PageRequest? page = null,
        Action<ProjectFilter>? captureFilter = null)
    {
        var items = summaries ?? [];
        var query = QueryReturning(items, totalItems ?? items.Count, captureFilter);

        return await GetProjectsEndpoint.HandleAsync(
            HttpContext(), request, caller ?? CallerInScope(), page ?? new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);
    }

    private static IOptions<PaginationOptions> Pagination() => Options.Create(new PaginationOptions());

    private static void AssertValidationProblem(IResult result, string expectedCode)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(expectedCode, problem.ProblemDetails.Extensions["code"]);
        Assert.False(string.IsNullOrWhiteSpace(problem.ProblemDetails.Detail));
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_ShortNameInvalid()
    {
        var result = await InvokeAsync(Empty() with { ShortName = "x" });

        AssertValidationProblem(result, ProjectErrorCodes.ShortNameInvalid);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_ContactEmailInvalid()
    {
        var result = await InvokeAsync(Empty() with { ContactEmail = "not-an-email" });

        AssertValidationProblem(result, ProjectErrorCodes.ContactEmailInvalid);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_DomainTypeNameInvalid()
    {
        var result = await InvokeAsync(Empty() with { DomainTypeName = new string('a', 101) });

        AssertValidationProblem(result, ProjectErrorCodes.DomainTypeNameInvalid);
    }

    [Fact]
    public async Task Should_PassDomainTypeNameToFilter_When_Provided()
    {
        ProjectFilter? captured = null;

        await InvokeAsync(Empty() with { DomainTypeName = "Biology" }, captureFilter: f => captured = f);

        Assert.NotNull(captured);
        Assert.Equal(DomainTypeName.Create("Biology"), captured!.DomainTypeName);
    }

    [Fact]
    public async Task Should_NotSetDomainTypeNameFilter_When_DomainTypeNameIsWhitespace()
    {
        ProjectFilter? captured = null;

        await InvokeAsync(Empty() with { DomainTypeName = "   " }, captureFilter: f => captured = f);

        Assert.NotNull(captured);
        Assert.Null(captured!.DomainTypeName);
    }

    [Fact]
    public async Task Should_PassDateBoundsToFilter_When_Provided()
    {
        ProjectFilter? captured = null;
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

        await InvokeAsync(Empty() with { StartDate = start, EndDate = end }, captureFilter: f => captured = f)
            ;

        Assert.NotNull(captured);
        Assert.Equal(start, captured!.StartDate);
        Assert.Equal(end, captured.EndDate);
    }

    [Fact]
    public async Task Should_PassCallerScopeToFilter_And_ReturnEmptyList_When_NoMatches()
    {
        ProjectFilter? captured = null;

        var result = await InvokeAsync(Empty(), captureFilter: f => captured = f);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectsEndpoint.Response>>>(result);
        Assert.Empty(ok.Value!);
        Assert.NotNull(captured);
        Assert.Contains(ShortName, captured!.ScopeShortNames!);
    }

    [Fact]
    public async Task Should_NotScopeFilter_When_CallerIsAdmin()
    {
        ProjectFilter? captured = null;

        await InvokeAsync(Empty(), caller: AdminCaller(), captureFilter: f => captured = f);

        // Admin sees every project — no scope filter applied.
        Assert.NotNull(captured);
        Assert.Null(captured!.ScopeShortNames);
    }

    [Fact]
    public async Task Should_ReturnBareArray_AndTotalCountHeader_When_ResultsExist()
    {
        var http = HttpContext();
        var query = QueryReturning([SampleSummary("PROJ-1")], totalItems: 1, captureFilter: null);

        var result = await GetProjectsEndpoint.HandleAsync(
            http, Empty(), CallerInScope(), new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None)
            ;

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectsEndpoint.Response>>>(result);
        Assert.Single(ok.Value!);
        Assert.Equal("1", http.Response.Headers["X-Total-Count"]);
        Assert.False(http.Response.Headers.ContainsKey("Link")); // single page
    }

    [Fact]
    public async Task Should_SetTotalCount_AndNextLink_When_MoreResultsExist()
    {
        var http = HttpContext();
        List<ProjectSummary> pageItems = [SampleSummary("PROJ-1"), SampleSummary("PROJ-2")];
        var query = QueryReturning(pageItems, totalItems: 5, captureFilter: null);

        var result = await GetProjectsEndpoint.HandleAsync(
            http, Empty(), CallerInScope(), new PageRequest(1, 2), query.Object, RoleOptions(), Pagination(), CancellationToken.None)
            ;

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectsEndpoint.Response>>>(result);
        Assert.Equal(2, ok.Value!.Count);
        Assert.Equal("5", http.Response.Headers["X-Total-Count"]);
        Assert.Contains("rel=\"next\"", http.Response.Headers.Link.ToString());
    }

    [Fact]
    public async Task Should_CapLimit_When_LimitExceedsMax()
    {
        int? capturedLimit = null;
        var query = new Mock<IProjectQuery>();
        query.Setup(q => q.GetProjectsAsync(It.IsAny<ProjectFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<ProjectFilter, int, int, CancellationToken>((_, _, limit, _) => capturedLimit = limit)
            .ReturnsAsync(new QueryPage<ProjectSummary>([], 0));

        await GetProjectsEndpoint.HandleAsync(
            HttpContext(), Empty(), CallerInScope(), new PageRequest(1, PageRequest.MaxLimit + 1000), query.Object, RoleOptions(), Pagination(), CancellationToken.None)
            ;

        Assert.Equal(PageRequest.MaxLimit, capturedLimit);
    }

    [Fact]
    public async Task Should_MapSummaryToResponse_When_ResultsExist()
    {
        var summary = new ProjectSummary(
            ShortName, ProjectName.Create("Project One"), Email.Create("p@example.org"),
            null, EOperationMode.Editable, EProjectState.Active, ServiceName.Default,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero));

        var result = await InvokeAsync(Empty(), summaries: [summary]);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectsEndpoint.Response>>>(result);
        var item = Assert.Single(ok.Value!);
        Assert.Equal("PROJ-1", item.ShortName);
        Assert.Equal("Project One", item.Name);
        Assert.Equal("p@example.org", item.Email);
        Assert.Null(item.ExternalId);
        Assert.Equal("Editable", item.Mode);
    }
}
