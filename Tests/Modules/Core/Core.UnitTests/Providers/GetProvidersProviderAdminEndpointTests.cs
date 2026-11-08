using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Provider.GetProvider;
using Core.Features.Provider.GetProvidersProviderAdmin;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Providers;

public sealed class GetProvidersProviderAdminEndpointTests
{
    private static readonly WellKnownRoles Roles = new();

    private static ProviderDetail Detail(string name, bool isDeleted = false, params string[] locationNames) => new(
        ProviderName.From(name),
        null,
        null,
        null,
        EOperationMode.Editable,
        ServiceName.Default,
        IsPrivate: false,
        "CZ",
        isDeleted,
        [.. locationNames.Select(l => new ProviderLocationDetail(LocationName.From(l), $"https://{l}", null, false, true))]);

    private static Caller AdminCaller() => new(
        new IdpId("a"), Roles.DefaultAdminRoleName, null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller ProviderCaller() => new(
        new IdpId("p"), "ProviderAdmin", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(), ProviderName.Create("acme"));

    private static Caller InteractiveProviderAdminWithProjects() => new(
        new IdpId("pa"), "ProviderAdmin", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>
        {
            ["Member"] = new HashSet<ProjectShortName> { ProjectShortName.Create("proj-x") }
        },
        ProviderName.Create("acme"));

    private static Caller UserWithProjects() => new(
        new IdpId("u"), "User", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>
        {
            ["Member"] = new HashSet<ProjectShortName> { ProjectShortName.Create("proj-x") }
        });

    private static IOptions<WellKnownRoles> RoleOptions() => Options.Create(Roles);

    private static IOptions<PaginationOptions> Pagination() => Options.Create(new PaginationOptions());

    private static DefaultHttpContext HttpContext(string queryString = "")
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/ProviderAdmin/Providers";
        http.Request.QueryString = new QueryString(queryString);
        return http;
    }

    private static Mock<IProviderQuery> Query(List<ProviderDetail>? items = null, int? total = null, Action<AdministeredProviderFilter>? capture = null)
    {
        var page = items ?? [];
        var query = new Mock<IProviderQuery>(MockBehavior.Strict);
        query.Setup(q => q.GetAdministeredProvidersAsync(It.IsAny<AdministeredProviderFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<AdministeredProviderFilter, int, int, CancellationToken>((f, _, _, _) => capture?.Invoke(f))
            .ReturnsAsync(new QueryPage<ProviderDetail>(page, total ?? page.Count));
        return query;
    }

    private static Task<IResult> InvokeAsync(
        Caller caller, IProviderQuery query, bool includeInactive = false, HttpContext? http = null, PageRequest? page = null) =>
        GetProvidersProviderAdminEndpoint.HandleAsync(
            http ?? HttpContext(), caller, page ?? new PageRequest(null, null), query, RoleOptions(), Pagination(), CancellationToken.None, includeInactive);

    private static IReadOnlyList<GetProviderResponse> Body(IResult result) =>
        Assert.IsAssignableFrom<IReadOnlyList<GetProviderResponse>>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);

    [Fact]
    public async Task Should_ListEveryProvider_When_CallerIsAdmin()
    {
        AdministeredProviderFilter? captured = null;

        await InvokeAsync(AdminCaller(), Query(capture: f => captured = f).Object);

        Assert.NotNull(captured);
        Assert.Null(captured.Names);
        Assert.False(captured.IncludeInactive);
    }

    [Fact]
    public async Task Should_ListOwnProviderOnly_When_CallerIsProviderIdentity()
    {
        AdministeredProviderFilter? captured = null;

        await InvokeAsync(ProviderCaller(), Query(capture: f => captured = f).Object);

        Assert.NotNull(captured);
        Assert.NotNull(captured.Names);
        Assert.Equal(["acme"], captured.Names.Select(n => n.Value));
        Assert.False(captured.IncludeInactive);
    }

    [Fact]
    public async Task Should_ListOwnProviderOnly_When_CallerIsInteractiveProviderAdminWithProjects()
    {
        AdministeredProviderFilter? captured = null;

        await InvokeAsync(InteractiveProviderAdminWithProjects(), Query(capture: f => captured = f).Object);

        Assert.NotNull(captured);
        Assert.NotNull(captured.Names);
        Assert.Equal(["acme"], captured.Names.Select(n => n.Value));
    }

    [Fact]
    public async Task Should_ReturnEmptyPageWithoutQuerying_When_CallerIsScopedUser()
    {
        var query = new Mock<IProviderQuery>(MockBehavior.Strict);
        var http = HttpContext();

        var result = await InvokeAsync(UserWithProjects(), query.Object, http: http);

        Assert.Empty(Body(result));
        Assert.Equal("0", http.Response.Headers["X-Total-Count"].ToString());
        query.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_PassIncludeInactive_When_AdminAsksForInactive()
    {
        AdministeredProviderFilter? captured = null;

        await InvokeAsync(AdminCaller(), Query(capture: f => captured = f).Object, includeInactive: true);

        Assert.NotNull(captured);
        Assert.Null(captured.Names);
        Assert.True(captured.IncludeInactive);
    }

    [Fact]
    public async Task Should_KeepOwnProviderAsNames_When_ProviderIdentityAsksForInactive()
    {
        AdministeredProviderFilter? captured = null;

        await InvokeAsync(ProviderCaller(), Query(capture: f => captured = f).Object, includeInactive: true);

        Assert.NotNull(captured);
        Assert.NotNull(captured.Names);
        Assert.Equal(["acme"], captured.Names.Select(n => n.Value));
        Assert.True(captured.IncludeInactive);
    }

    [Fact]
    public async Task Should_ReturnEmptyPageWithoutQuerying_When_ScopedUserAsksForInactive()
    {
        var query = new Mock<IProviderQuery>(MockBehavior.Strict);
        var http = HttpContext("?includeInactive=true");

        var result = await InvokeAsync(UserWithProjects(), query.Object, includeInactive: true, http: http);

        Assert.Empty(Body(result));
        Assert.Equal("0", http.Response.Headers["X-Total-Count"].ToString());
        query.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_MapProvidersWithLocationsAndDeletedFlag_When_Listing()
    {
        var http = HttpContext();
        var query = Query([Detail("acme", false, "gpu", "hpc"), Detail("retired-co", true)]);

        var result = await InvokeAsync(AdminCaller(), query.Object, includeInactive: true, http: http);

        var body = Body(result);
        Assert.Equal([("acme", false), ("retired-co", true)], body.Select(p => (p.Name, p.IsDeleted)));
        Assert.Equal(["gpu", "hpc"], body[0].Locations.Select(l => l.Name));
        Assert.Equal("https://gpu", body[0].Locations[0].Endpoint);
        Assert.Empty(body[1].Locations);
        Assert.Equal("2", http.Response.Headers["X-Total-Count"].ToString());
    }

    [Fact]
    public async Task Should_KeepIncludeInactiveInNextLink_When_MorePagesRemain()
    {
        var http = HttpContext("?includeInactive=true&limit=1");
        var query = Query([Detail("acme")], total: 3);

        await InvokeAsync(AdminCaller(), query.Object, includeInactive: true, http: http, page: new PageRequest(null, 1));

        var next = http.Response.Headers.Link.ToString().Split(", ").Single(l => l.EndsWith("rel=\"next\"", StringComparison.Ordinal));
        Assert.Contains("includeInactive=true", next, StringComparison.Ordinal);
        Assert.Contains("page=2", next, StringComparison.Ordinal);
        Assert.Equal("3", http.Response.Headers["X-Total-Count"].ToString());
    }
}