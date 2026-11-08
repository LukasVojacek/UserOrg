using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Location.GetLocationsProviderAdmin;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Locations;

public sealed class GetLocationsProviderAdminEndpointTests
{
    private static readonly WellKnownRoles Roles = new();

    private static LocationSummary Sample() => new(
        "loc-1", "desc", "https://hpc.example.org", false, true, ELocationType.HPC, "acme", EOperationMode.Editable, "https://svc.example.org");

    private static Caller AdminCaller() => new(
        new IdpId("a"), Roles.DefaultAdminRoleName, null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller ProviderCaller() => new(
        new IdpId("p"), "ProviderAdmin", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(), ProviderName.Create("acme"));

    // The caller shape D5 creates: an interactive ProviderAdmin who also sits in projects of their own.
    private static Caller InteractiveProviderAdminWithProjects() => new(
        new IdpId("pa"), "ProviderAdmin", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>
        {
            ["Member"] = new HashSet<ProjectShortName> { ProjectShortName.Create("proj-x") }
        },
        ProviderName.Create("acme"));

    private static IOptions<WellKnownRoles> RoleOptions() => Options.Create(Roles);

    private static IOptions<PaginationOptions> Pagination() => Options.Create(new PaginationOptions());

    private static DefaultHttpContext HttpContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/ProviderAdmin/Locations";
        return http;
    }

    private static async Task<IResult> InvokeAsync(
        Caller caller,
        Action<LocationFilter>? captureFilter = null,
        List<LocationSummary>? summaries = null)
    {
        var items = summaries ?? [];
        var query = new Mock<ILocationQuery>();
        query.Setup(q => q.GetLocationsAsync(It.IsAny<LocationFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<LocationFilter, int, int, CancellationToken>((f, _, _, _) => captureFilter?.Invoke(f))
            .ReturnsAsync(new QueryPage<LocationSummary>(items, items.Count));

        return await GetLocationsProviderAdminEndpoint.HandleAsync(
            HttpContext(), caller, new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);
    }

    [Fact]
    public async Task Should_NotScopeFilter_When_CallerIsAdmin()
    {
        LocationFilter? captured = null;

        await InvokeAsync(AdminCaller(), captureFilter: f => captured = f);

        Assert.NotNull(captured);
        Assert.Null(captured!.ProviderName);
        Assert.Null(captured.ScopeShortNames);
    }

    [Fact]
    public async Task Should_ScopeToOwnProvider_When_CallerIsProviderIdentity()
    {
        LocationFilter? captured = null;

        await InvokeAsync(ProviderCaller(), captureFilter: f => captured = f);

        Assert.NotNull(captured);
        Assert.Equal("acme", captured!.ProviderName?.Value);
        Assert.Null(captured.ScopeShortNames);
    }

    /// <summary>
    /// This route is own-provider <b>by contract</b>: it is the provider administrator's view of the provider
    /// they administer, which is why it exists next to <c>GET /locations</c> instead of inside it. So the
    /// narrowing D5 introduces here is intended, not the regression D11 forbids on the general listings — the
    /// same administrator still reads every project-reachable location from <c>GET /locations</c>, which does
    /// union own provider with caller scope. Pinned so a later "fix" cannot quietly widen this route into a
    /// second, differently-shaped general listing, and so it keeps matching its sibling
    /// <c>ProviderAdmin/Projects</c>.
    /// </summary>
    [Fact]
    public async Task Should_ScopeToOwnProviderOnly_When_CallerIsInteractiveProviderAdminWithProjects()
    {
        LocationFilter? captured = null;

        await InvokeAsync(InteractiveProviderAdminWithProjects(), captureFilter: f => captured = f);

        Assert.NotNull(captured);
        Assert.Equal("acme", captured!.ProviderName?.Value);
        Assert.Null(captured.ScopeShortNames);
    }

    [Fact]
    public async Task Should_CallFilteredQuery_And_NotUnfilteredOverload()
    {
        var query = new Mock<ILocationQuery>(MockBehavior.Strict);
        query.Setup(q => q.GetLocationsAsync(It.IsAny<LocationFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryPage<LocationSummary>([Sample()], 1));

        var result = await GetLocationsProviderAdminEndpoint.HandleAsync(
            HttpContext(), AdminCaller(), new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);

        Assert.NotNull(result);
        query.Verify(q => q.GetLocationsAsync(It.IsAny<LocationFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        query.Verify(q => q.GetLocationsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
