using Core.DataAccess.Queries.Contracts;
using Core.Features.Project.GetProjectsProviderAdmin;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Projects;

public sealed class GetProjectsProviderAdminEndpointTests
{
    private static readonly WellKnownRoles Roles = new();

    private static Caller AdminCaller() => new(
        new IdpId("a"), Roles.DefaultAdminRoleName, null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller ProviderCaller() => new(
        new IdpId("p"), "ProviderAdmin", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(), ProviderName.Create("acme"));

    private static Caller ScopedCaller() => new(
        new IdpId("u"), "User", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>
        {
            ["Member"] = new HashSet<ProjectShortName> { ProjectShortName.Create("proj-a") },
        });

    private static IOptions<WellKnownRoles> RoleOptions() => Options.Create(Roles);

    private static IOptions<PaginationOptions> Pagination() => Options.Create(new PaginationOptions());

    private static DefaultHttpContext HttpContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/ProviderAdmin/Projects";
        return http;
    }

    private static async Task<IResult> InvokeAsync(Caller caller, Action<ProjectFilter>? captureFilter = null)
    {
        var query = new Mock<IProjectQuery>();
        query.Setup(q => q.GetProjectsAsync(It.IsAny<ProjectFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<ProjectFilter, int, int, CancellationToken>((f, _, _, _) => captureFilter?.Invoke(f))
            .ReturnsAsync(new QueryPage<ProjectSummary>([], 0));

        return await GetProjectsProviderAdminEndpoint.HandleAsync(
            HttpContext(), caller, new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);
    }

    [Fact]
    public async Task Should_NotScopeFilter_When_CallerIsAdmin()
    {
        ProjectFilter? captured = null;

        await InvokeAsync(AdminCaller(), captureFilter: f => captured = f);

        Assert.NotNull(captured);
        Assert.Null(captured!.ProviderName);
        Assert.Null(captured.ScopeShortNames);
    }

    [Fact]
    public async Task Should_ScopeToOwnProvider_When_CallerIsProviderIdentity()
    {
        ProjectFilter? captured = null;

        await InvokeAsync(ProviderCaller(), captureFilter: f => captured = f);

        Assert.NotNull(captured);
        Assert.Equal("acme", captured!.ProviderName?.Value);
        Assert.Null(captured.ScopeShortNames);
    }

    [Fact]
    public async Task Should_ScopeToCallerProjects_When_CallerIsNeitherAdminNorProvider()
    {
        ProjectFilter? captured = null;

        await InvokeAsync(ScopedCaller(), captureFilter: f => captured = f);

        Assert.NotNull(captured);
        Assert.Null(captured!.ProviderName);
        Assert.NotNull(captured.ScopeShortNames);
        Assert.Equal(["proj-a"], captured.ScopeShortNames!.Select(s => s.Value));
    }
}
