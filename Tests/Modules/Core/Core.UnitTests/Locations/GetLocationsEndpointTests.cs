using System.Text.Json;
using System.Text.Json.Serialization;

using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Location.GetLocations;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Locations;

public sealed class GetLocationsEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly WellKnownRoles Roles = new();

    private static LocationSummary Sample() => new(
        "loc-1", "desc", "https://hpc.example.org", false, true, ELocationType.HPC, "acme", EOperationMode.Editable, "https://svc.example.org");

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

    private static IOptions<WellKnownRoles> RoleOptions() => Options.Create(Roles);

    private static DefaultHttpContext HttpContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/Locations";
        return http;
    }

    private static async Task<IResult> InvokeAsync(
        string? providerName = null,
        ELocationType? locationType = null,
        List<LocationSummary>? summaries = null,
        int? totalItems = null,
        PageRequest? page = null,
        Action<LocationFilter>? captureFilter = null,
        Caller? caller = null)
    {
        var items = summaries ?? [];
        var query = new Mock<ILocationQuery>();
        query.Setup(q => q.GetLocationsAsync(It.IsAny<LocationFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<LocationFilter, int, int, CancellationToken>((f, _, _, _) => captureFilter?.Invoke(f))
            .ReturnsAsync(new QueryPage<LocationSummary>(items, totalItems ?? items.Count));

        return await GetLocationsEndpoint.HandleAsync(
            HttpContext(), providerName, locationType, caller ?? UserCaller(), page ?? new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);
    }

    private static IOptions<PaginationOptions> Pagination() => Options.Create(new PaginationOptions());

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Should_PassProviderNameAndLocationTypeToFilter()
    {
        LocationFilter? captured = null;

        await InvokeAsync(providerName: "acme", locationType: ELocationType.HPC, captureFilter: f => captured = f);

        Assert.NotNull(captured);
        Assert.Equal("acme", captured!.ProviderName?.Value);
        Assert.Equal(ELocationType.HPC, captured.LocationType);
    }

    [Fact]
    public async Task Should_ReturnBadRequestWithInvalidNameCode_When_ProviderNameFilterExceedsMaxLength()
    {
        // Unvalidated, this query parameter reached the throwing factory inside LocationDetailsQuery — a 500.
        var result = await InvokeAsync(providerName: new string('a', 101), caller: AdminCaller());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("provider.invalid_name", problem.ProblemDetails.Extensions["code"]);
        Assert.False(string.IsNullOrWhiteSpace(problem.ProblemDetails.Detail));
    }

    [Fact]
    public async Task Should_NotScopeFilter_When_CallerIsAdmin()
    {
        LocationFilter? captured = null;

        await InvokeAsync(providerName: "acme", captureFilter: f => captured = f, caller: AdminCaller());

        // Admin: no project scope; the user-facing providerName filter is preserved.
        Assert.NotNull(captured);
        Assert.Null(captured!.ScopeShortNames);
        Assert.Equal("acme", captured.ProviderName?.Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Should_NotShrinkBelowExistingScope_When_CallerIsProviderTier(bool isServiceKeyIdentity)
    {
        LocationFilter? captured = null;

        await InvokeAsync(captureFilter: f => captured = f, caller: ProviderTierCaller(isServiceKeyIdentity));

        // The provider tier is added to the caller's project scope, never substituted for it: pinning the
        // filter to the own provider alone would drop the locations they already reach through a project.
        Assert.NotNull(captured);
        Assert.Equal("acme", captured!.OwnProviderName?.Value);
        Assert.NotNull(captured.ScopeShortNames);
        Assert.Contains(ShortName, captured.ScopeShortNames!);
    }

    [Fact]
    public async Task Should_KeepProviderNameFilter_When_CallerIsProviderTier()
    {
        LocationFilter? captured = null;

        await InvokeAsync(providerName: "other", captureFilter: f => captured = f, caller: ProviderTierCaller(isServiceKeyIdentity: false));

        // The user-facing providerName filter survives the tier: it narrows the visible set, it is not a tier pin.
        Assert.NotNull(captured);
        Assert.Equal("other", captured!.ProviderName?.Value);
        Assert.Equal("acme", captured.OwnProviderName?.Value);
    }

    [Fact]
    public async Task Should_ScopeToCallerProjects_When_CallerIsUser()
    {
        LocationFilter? captured = null;

        await InvokeAsync(captureFilter: f => captured = f, caller: UserCaller());

        // User tier: narrowed to locations linked to the caller's projects (fixes the pre-F0 private leak).
        Assert.NotNull(captured);
        Assert.NotNull(captured!.ScopeShortNames);
        Assert.Contains(ShortName, captured.ScopeShortNames!);
    }

    [Fact]
    public async Task Should_ReturnEmptyBareArray_When_NoMatches()
    {
        var result = await InvokeAsync();

        var ok = Assert.IsType<Ok<IReadOnlyList<GetLocationsEndpoint.Response>>>(result);
        Assert.Empty(ok.Value!);
    }

    [Fact]
    public async Task Should_MapSummaryToResponse_When_ResultsExist()
    {
        var result = await InvokeAsync(summaries: [Sample()]);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetLocationsEndpoint.Response>>>(result);
        var item = Assert.Single(ok.Value!);
        Assert.Equal("loc-1", item.Name);
        Assert.Equal(ELocationType.HPC, item.LocationType);
        Assert.True(item.IsShared);
    }

    [Fact]
    public async Task Should_EmitModeByName_When_ResultsExist()
    {
        var result = await InvokeAsync(summaries: [Sample()]);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetLocationsEndpoint.Response>>>(result);
        var item = JsonSerializer.SerializeToElement(Assert.Single(ok.Value!), WireJson);
        Assert.Equal("\"Editable\"", item.GetProperty("Mode").GetRawText());
    }

    [Fact]
    public async Task Should_SetTotalCount_AndNextLink_When_MoreResultsExist()
    {
        var http = HttpContext();
        var query = new Mock<ILocationQuery>();
        query.Setup(q => q.GetLocationsAsync(It.IsAny<LocationFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryPage<LocationSummary>([Sample(), Sample()], 5));

        var result = await GetLocationsEndpoint.HandleAsync(
            http, null, null, UserCaller(), new PageRequest(1, 2), query.Object, RoleOptions(), Pagination(), CancellationToken.None);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetLocationsEndpoint.Response>>>(result);
        Assert.Equal(2, ok.Value!.Count);
        Assert.Equal("5", http.Response.Headers["X-Total-Count"]);
        Assert.Contains("rel=\"next\"", http.Response.Headers.Link.ToString());
    }

    [Fact]
    public async Task Should_CapLimit_When_LimitExceedsMax()
    {
        int? capturedLimit = null;
        var query = new Mock<ILocationQuery>();
        query.Setup(q => q.GetLocationsAsync(It.IsAny<LocationFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<LocationFilter, int, int, CancellationToken>((_, _, limit, _) => capturedLimit = limit)
            .ReturnsAsync(new QueryPage<LocationSummary>([], 0));

        await GetLocationsEndpoint.HandleAsync(
            HttpContext(), null, null, UserCaller(), new PageRequest(1, PageRequest.MaxLimit + 1000), query.Object, RoleOptions(), Pagination(), CancellationToken.None);

        Assert.Equal(PageRequest.MaxLimit, capturedLimit);
    }
}
