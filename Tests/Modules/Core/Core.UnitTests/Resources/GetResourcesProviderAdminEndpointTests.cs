using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Resource;
using Core.Features.Resource.GetResourcesProviderAdmin;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Resources;

public sealed class GetResourcesProviderAdminEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly ProviderName Acme = ProviderName.Create("acme");
    private static readonly WellKnownRoles Roles = new();

    private static Caller AdminCaller() => new(
        new IdpId("a"), Roles.DefaultAdminRoleName, null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller OwnerCaller() => new(
        new IdpId("u"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private static Caller ProviderCallerWithProject(bool isServiceKeyIdentity) => new(
        new IdpId("t"), "ProviderAdmin", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } },
        Acme, IsServiceKeyIdentity: isServiceKeyIdentity);

    private static IOptions<PaginationOptions> Pagination() => Options.Create(new PaginationOptions());
    private static IOptions<WellKnownRoles> RoleOptions() => Options.Create(Roles);

    private static DefaultHttpContext HttpContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/ProviderAdmin/Providers/acme/Resources";
        return http;
    }

    private static ResourceSummary Sample() => new(
        "hpc-1", "acme|hpc-1", "desc", "pi@example.org",
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
        EOperationMode.Editable,
        "https://api.test/api/v2/resources/hpc-1",
        EProjectResourceState.Draft,
        Email.Create("no@example.org"),
        []);

    private static Mock<IResourceQuery> CapturingQuery(Action<ResourceFilter> capture, QueryPage<ResourceSummary> page)
    {
        var query = new Mock<IResourceQuery>();
        query.Setup(q => q.GetResourcesAsync(It.IsAny<ResourceFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<ResourceFilter, int, int, CancellationToken>((f, _, _, _) => capture(f))
            .ReturnsAsync(page);
        return query;
    }

    [Fact]
    public async Task Should_NotScopeFilter_When_CallerIsAdmin()
    {
        ResourceFilter? captured = null;
        var query = CapturingQuery(f => captured = f, new QueryPage<ResourceSummary>([], 0));

        await GetResourcesProviderAdminEndpoint.HandleAsync(HttpContext(), "acme", null, null, null, null, AdminCaller(), new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Null(captured.ScopeShortNames);
        Assert.Null(captured.ScopeProviderName);
        Assert.Equal(Acme, captured.FilterProviderName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Should_UnionOwnProviderWithProjects_When_CallerIsProviderIdentity(bool isServiceKeyIdentity)
    {
        ResourceFilter? captured = null;
        var query = CapturingQuery(f => captured = f, new QueryPage<ResourceSummary>([], 0));

        await GetResourcesProviderAdminEndpoint.HandleAsync(HttpContext(), "acme", null, null, null, null, ProviderCallerWithProject(isServiceKeyIdentity), new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(Acme, captured.ScopeProviderName);
        Assert.NotNull(captured.ScopeShortNames);
        Assert.Contains(ShortName, captured.ScopeShortNames);
    }

    [Fact]
    public async Task Should_ScopeToProjects_When_CallerIsScoped()
    {
        ResourceFilter? captured = null;
        var query = CapturingQuery(f => captured = f, new QueryPage<ResourceSummary>([], 0));

        await GetResourcesProviderAdminEndpoint.HandleAsync(HttpContext(), "acme", null, null, null, null, OwnerCaller(), new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Null(captured.ScopeProviderName);
        Assert.Equal([ShortName], captured.ScopeShortNames!);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_ProviderNameInvalid()
    {
        var query = new Mock<IResourceQuery>();

        var result = await GetResourcesProviderAdminEndpoint.HandleAsync(HttpContext(), "   ", null, null, null, null, OwnerCaller(), new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("provider.invalid_name", problem.ProblemDetails.Extensions["code"]);
        query.Verify(q => q.GetResourcesAsync(It.IsAny<ResourceFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_ProjectShortNameInvalid()
    {
        var query = new Mock<IResourceQuery>();

        var result = await GetResourcesProviderAdminEndpoint.HandleAsync(HttpContext(), "acme", "x", null, null, null, OwnerCaller(), new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        query.Verify(q => q.GetResourcesAsync(It.IsAny<ResourceFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_PassFiltersAndPaging_When_Listing()
    {
        ResourceFilter? captured = null;
        var http = HttpContext();
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);
        var query = CapturingQuery(f => captured = f, new QueryPage<ResourceSummary>([Sample()], 5));

        var result = await GetResourcesProviderAdminEndpoint.HandleAsync(http, "acme", "PROJ-1", ELocationType.HPC, start, end, AdminCaller(), new PageRequest(1, 1), query.Object, RoleOptions(), Pagination(), CancellationToken.None);

        var ok = Assert.IsType<Ok<IReadOnlyList<ResourceAdminResponse>>>(result);
        Assert.Equal("hpc-1", Assert.Single(ok.Value!).Name);
        Assert.Equal("5", http.Response.Headers["X-Total-Count"]);
        Assert.NotNull(captured);
        Assert.Equal(ShortName, captured.ProjectShortName);
        Assert.Equal(ELocationType.HPC, captured.LocationType);
        Assert.Equal(start, captured.StartDate);
        Assert.Equal(end, captured.EndDate);
        Assert.Equal(Acme, captured.FilterProviderName);
    }
}
