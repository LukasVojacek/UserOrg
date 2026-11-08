using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Provider.GetProviders;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Providers;

public sealed class GetProvidersEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly WellKnownRoles Roles = new();

    private static IOptions<WellKnownRoles> RoleOptions() => Options.Create(Roles);

    private static Caller UserCaller() => new(
        new IdpId("u"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private static Caller AdminCaller() => new(
        new IdpId("a"), Roles.DefaultAdminRoleName, null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller ProviderTierCaller(bool isServiceKeyIdentity) => new(
        new IdpId("p"), "ProviderAdmin", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } },
        ProviderName.Create("acme"), IsServiceKeyIdentity: isServiceKeyIdentity);

    private static ProviderSummary SampleSummary(string name) => new(
        ProviderName.From(name), Description.Create("d"), Email.Create("s@example.org"), null,
        EOperationMode.Editable, ServiceName.Default, false, "CZ");

    private static DefaultHttpContext HttpContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/Providers";
        return http;
    }

    private static async Task<IResult> InvokeAsync(
        GetProvidersEndpoint.Request request,
        List<ProviderSummary>? summaries = null,
        int? totalItems = null,
        PageRequest? page = null,
        Action<ProviderFilter>? captureFilter = null,
        Caller? caller = null)
    {
        var items = summaries ?? [];
        var query = new Mock<IProviderQuery>();
        query.Setup(q => q.GetProvidersAsync(It.IsAny<ProviderFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderFilter, int, int, CancellationToken>((f, _, _, _) => captureFilter?.Invoke(f))
            .ReturnsAsync(new QueryPage<ProviderSummary>(items, totalItems ?? items.Count));

        // Default to the admin caller so mapping/paging tests see the request mapped without tier noise.
        return await GetProvidersEndpoint.HandleAsync(
            HttpContext(), request, caller ?? AdminCaller(), page ?? new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);
    }

    private static IOptions<PaginationOptions> Pagination() => Options.Create(new PaginationOptions());

    [Fact]
    public async Task Should_PassNameAndCountryToFilter_When_Provided()
    {
        ProviderFilter? captured = null;

        await InvokeAsync(new GetProvidersEndpoint.Request("acme", "CZ"), captureFilter: f => captured = f);

        Assert.NotNull(captured);
        Assert.NotNull(captured!.Name);
        Assert.Equal("CZ", captured.CountryCode);
    }

    [Fact]
    public async Task Should_NotSetNameFilter_When_NameIsWhitespace()
    {
        ProviderFilter? captured = null;

        await InvokeAsync(new GetProvidersEndpoint.Request("   ", null), captureFilter: f => captured = f);

        Assert.NotNull(captured);
        Assert.Null(captured!.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Should_NotSetCountryFilter_When_CountryCodeIsBlank(string countryCode)
    {
        ProviderFilter? captured = null;

        await InvokeAsync(new GetProvidersEndpoint.Request(null, countryCode), captureFilter: f => captured = f);

        Assert.NotNull(captured);
        Assert.Null(captured!.CountryCode);
    }

    [Fact]
    public async Task Should_TrimCountryFilter_When_CountryCodeHasSurroundingWhitespace()
    {
        ProviderFilter? captured = null;

        await InvokeAsync(new GetProvidersEndpoint.Request(null, " CZ "), captureFilter: f => captured = f);

        Assert.NotNull(captured);
        Assert.Equal("CZ", captured!.CountryCode);
    }

    [Fact]
    public async Task Should_ReturnBadRequestWithInvalidNameCode_When_NameFilterExceedsMaxLength()
    {
        // The IsNullOrWhiteSpace guard let this through to the throwing factory, which surfaced as a 500.
        var result = await InvokeAsync(new GetProvidersEndpoint.Request(new string('a', 101), null));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("provider.invalid_name", problem.ProblemDetails.Extensions["code"]);
        Assert.False(string.IsNullOrWhiteSpace(problem.ProblemDetails.Detail));
    }

    [Fact]
    public async Task Should_NotScopeFilter_When_CallerIsAdmin()
    {
        ProviderFilter? captured = null;

        await InvokeAsync(new GetProvidersEndpoint.Request("acme", null), captureFilter: f => captured = f, caller: AdminCaller());

        // Admin: no scope, no public-only narrowing; the user-facing name filter is preserved.
        Assert.NotNull(captured);
        Assert.False(captured!.OnlyPublic);
        Assert.Null(captured.ScopeShortNames);
        Assert.Equal("acme", captured.Name?.Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Should_ReturnOwnPlusPublicPlusScopeReachable_When_CallerIsProviderTier(bool isServiceKeyIdentity)
    {
        ProviderFilter? captured = null;

        await InvokeAsync(
            new GetProvidersEndpoint.Request(null, null),
            captureFilter: f => captured = f,
            caller: ProviderTierCaller(isServiceKeyIdentity));

        // The provider tier no longer replaces the caller's reach with an own-provider pin: it keeps the
        // public set and the private providers reachable through the caller's projects, and adds its own.
        Assert.NotNull(captured);
        Assert.True(captured!.OnlyPublic);
        Assert.Equal("acme", captured.OwnProviderName?.Value);
        Assert.NotNull(captured.ScopeShortNames);
        Assert.Contains(ShortName, captured.ScopeShortNames!);
    }

    [Fact]
    public async Task Should_KeepNameFilter_When_CallerIsProviderTier()
    {
        ProviderFilter? captured = null;

        await InvokeAsync(
            new GetProvidersEndpoint.Request("other", null),
            captureFilter: f => captured = f,
            caller: ProviderTierCaller(isServiceKeyIdentity: false));

        // The user-facing name filter survives the tier: it narrows the visible set, it is not a tier pin.
        Assert.NotNull(captured);
        Assert.Equal("other", captured!.Name?.Value);
        Assert.Equal("acme", captured.OwnProviderName?.Value);
    }

    [Fact]
    public async Task Should_ScopeToPublicOrCallerProjects_When_CallerIsUser()
    {
        ProviderFilter? captured = null;

        await InvokeAsync(new GetProvidersEndpoint.Request(null, null), captureFilter: f => captured = f, caller: UserCaller());

        // User tier: public providers ∪ private providers reachable through the caller's projects.
        Assert.NotNull(captured);
        Assert.True(captured!.OnlyPublic);
        Assert.NotNull(captured.ScopeShortNames);
        Assert.Contains(ShortName, captured.ScopeShortNames!);
    }

    [Fact]
    public async Task Should_ReturnEmptyArray_AndTotalCountHeader_When_NoMatches()
    {
        var http = HttpContext();
        var query = new Mock<IProviderQuery>();
        query.Setup(q => q.GetProvidersAsync(It.IsAny<ProviderFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryPage<ProviderSummary>([], 0));

        var result = await GetProvidersEndpoint.HandleAsync(
            http, new GetProvidersEndpoint.Request(null, null), AdminCaller(), new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None)
            ;

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProvidersEndpoint.Response>>>(result);
        Assert.Empty(ok.Value!);
        Assert.Equal("0", http.Response.Headers["X-Total-Count"]);
    }

    [Fact]
    public async Task Should_ReturnBareArray_AndTotalCountHeader_When_ResultsExist()
    {
        var http = HttpContext();
        var query = new Mock<IProviderQuery>();
        query.Setup(q => q.GetProvidersAsync(It.IsAny<ProviderFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryPage<ProviderSummary>([SampleSummary("acme")], 1));

        var result = await GetProvidersEndpoint.HandleAsync(
            http, new GetProvidersEndpoint.Request(null, null), AdminCaller(), new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None)
            ;

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProvidersEndpoint.Response>>>(result);
        Assert.Single(ok.Value!);
        Assert.Equal("1", http.Response.Headers["X-Total-Count"]);
        Assert.False(http.Response.Headers.ContainsKey("Link")); // single page
    }

    [Fact]
    public async Task Should_SetTotalCount_AndNextLink_When_MoreResultsExist()
    {
        var http = HttpContext();
        List<ProviderSummary> pageItems = [SampleSummary("acme"), SampleSummary("beta")];
        var query = new Mock<IProviderQuery>();
        query.Setup(q => q.GetProvidersAsync(It.IsAny<ProviderFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryPage<ProviderSummary>(pageItems, 5));

        var result = await GetProvidersEndpoint.HandleAsync(
            http, new GetProvidersEndpoint.Request(null, null), AdminCaller(), new PageRequest(1, 2), query.Object, RoleOptions(), Pagination(), CancellationToken.None)
            ;

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProvidersEndpoint.Response>>>(result);
        Assert.Equal(2, ok.Value!.Count);
        Assert.Equal("5", http.Response.Headers["X-Total-Count"]);
        Assert.Contains("rel=\"next\"", http.Response.Headers.Link.ToString());
    }

    [Fact]
    public async Task Should_CapLimit_When_LimitExceedsMax()
    {
        int? capturedLimit = null;
        var query = new Mock<IProviderQuery>();
        query.Setup(q => q.GetProvidersAsync(It.IsAny<ProviderFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderFilter, int, int, CancellationToken>((_, _, limit, _) => capturedLimit = limit)
            .ReturnsAsync(new QueryPage<ProviderSummary>([], 0));

        await GetProvidersEndpoint.HandleAsync(
            HttpContext(), new GetProvidersEndpoint.Request(null, null), AdminCaller(), new PageRequest(1, PageRequest.MaxLimit + 1000), query.Object, RoleOptions(), Pagination(), CancellationToken.None)
            ;

        Assert.Equal(PageRequest.MaxLimit, capturedLimit);
    }

    [Fact]
    public async Task Should_MapSummary_And_NullifyEmptyDescriptionAndEmail()
    {
        var summary = new ProviderSummary(
            ProviderName.From("acme"), Description.Create(""), null, null,
            EOperationMode.Editable, ServiceName.Default, false, "CZ");

        var result = await InvokeAsync(new GetProvidersEndpoint.Request(null, null), summaries: [summary]);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProvidersEndpoint.Response>>>(result);
        var item = Assert.Single(ok.Value!);
        Assert.Equal("acme", item.Name);
        Assert.Null(item.Description);
        Assert.Null(item.SupportEmail);
        Assert.Equal("Editable", item.Mode);
        Assert.Equal("CZ", item.CountryCode);
    }
}
