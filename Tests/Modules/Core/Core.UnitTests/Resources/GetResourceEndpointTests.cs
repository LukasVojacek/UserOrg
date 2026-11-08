using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Resource;
using Core.Features.Resource.GetResource;
using Core.Features.Resource.GetResources;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Resources;

public sealed class GetResourceEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly WellKnownRoles Roles = new();

    private static Caller InScopeCaller() => new(
        new IdpId("u"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private static Caller AdminCaller() => new(
        new IdpId("a"), Roles.DefaultAdminRoleName, null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller ProviderCaller() => new(
        new IdpId("p"), "ProviderAdmin", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(), ProviderName.Create("acme"));

    private static Caller ServiceKeyProviderCaller() => new(
        new IdpId("k"), "ProviderAdmin", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(), ProviderName.Create("acme"),
        IsServiceKeyIdentity: true);

    private static Caller InteractiveProviderAdminCaller() => new(
        new IdpId("h"), "ProviderAdmin", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } },
        ProviderName.Create("acme"));

    private const string ResName = "hpc-1";

    private static IOptions<PaginationOptions> Pagination() => Options.Create(new PaginationOptions());
    private static IOptions<WellKnownRoles> RoleOptions() => Options.Create(Roles);

    private static DefaultHttpContext HttpContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/resources";
        return http;
    }

    private const string ProviderQualifiedName = "acme|hpc-1";

    private static ResourceSummary Sample() => new(
        ResName, ProviderQualifiedName, "desc", "pi@example.org",
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
        EOperationMode.Editable,
        "https://api.test/api/v2/resources/hpc-1",
        EProjectResourceState.Draft,
        Email.Create("no@example.org"),
        [AssignmentSummary()]);

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static ResourceAssignmentSummary AssignmentSummary() =>
        JsonSerializer.Deserialize<ResourceAssignmentSummary>(
            JsonSerializer.Serialize(new
            {
                Slug = "asg-res-1-abc123",
                LocationType = nameof(ELocationType.HPC),
                IsLocationShared = true,
                LocationName = "loc-1",
                AllocationAmount = 100,
                AllocationUnitType = "NodeHours",
                AggregationName = "agg",
                OperationMode = nameof(EOperationMode.Editable),
                Specifications = new[] { new { Name = "k", Value = "v" } },
            }),
            WireJson)!;

    [Fact]
    public async Task Should_SendAllocationUnitTypeByName_When_FoundByName()
    {
        var query = new Mock<IResourceQuery>();
        query.Setup(q => q.GetResourceByProviderAndNameAsync(It.IsAny<ProviderName>(), It.IsAny<ResourceName>(), It.IsAny<IReadOnlyList<ProjectShortName>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sample());

        var result = await GetResourceEndpoint.HandleAsync(new GetResourceEndpoint.Request("acme", ResName), InScopeCaller(), query.Object, RoleOptions(), CancellationToken.None);

        var ok = Assert.IsType<Ok<ResourceResponse>>(result);
        var assignment = JsonNode.Parse(JsonSerializer.Serialize(ok.Value, WireJson))!["Assignments"]![0]!.AsObject();
        Assert.Equal("NodeHours", assignment["AllocationUnitType"]?.GetValue<string>());
        Assert.Equal(["AllocationUnitType"], assignment.Select(p => p.Key).Where(k => k.StartsWith("AllocationUnitType", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Should_ReturnOk_And_MapOperationModeToString_When_FoundByName()
    {
        var query = new Mock<IResourceQuery>();
        query.Setup(q => q.GetResourceByProviderAndNameAsync(ProviderName.Create("acme"), ResourceName.Create(ResName), It.IsAny<IReadOnlyList<ProjectShortName>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sample());

        var result = await GetResourceEndpoint.HandleAsync(new GetResourceEndpoint.Request("acme", ResName), InScopeCaller(), query.Object, RoleOptions(), CancellationToken.None);

        var ok = Assert.IsType<Ok<ResourceResponse>>(result);
        Assert.Equal(EOperationMode.Editable.ToString(), ok.Value!.OperationMode);
        Assert.Single(ok.Value.Assignments);
        Assert.Equal("asg-res-1-abc123", ok.Value.Assignments[0].Slug);
        Assert.Equal(ELocationType.HPC, ok.Value.Assignments[0].LocationType);
        Assert.Equal(EOperationMode.Editable.ToString(), ok.Value.Assignments[0].OperationMode);
        Assert.Equal("v", ok.Value.Assignments[0].Specifications[0].Value);
    }

    [Fact]
    public async Task Should_CarryProviderQualifiedName_When_FoundByName()
    {
        // The provider-qualified natural key (ADR-0044 §1) reaches the wire as its own field: ResourceName
        // alone is not unique across providers (ADR-0026), so a reporting client needs the qualifier.
        var query = new Mock<IResourceQuery>();
        query.Setup(q => q.GetResourceByProviderAndNameAsync(ProviderName.Create("acme"), ResourceName.Create(ResName), It.IsAny<IReadOnlyList<ProjectShortName>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sample());

        var result = await GetResourceEndpoint.HandleAsync(new GetResourceEndpoint.Request("acme", ResName), InScopeCaller(), query.Object, RoleOptions(), CancellationToken.None);

        var ok = Assert.IsType<Ok<ResourceResponse>>(result);
        Assert.Equal(ProviderQualifiedName, ok.Value!.ProviderResourceName);
        Assert.Equal(ResName, ok.Value.Name);
    }

    [Fact]
    public async Task Should_CarryNullProviderQualifiedName_When_ResourceIsLegacyStandaloneRow()
    {
        // A legacy standalone resource has no owning provider and therefore no qualified key; the field stays
        // null instead of being invented, and the rest of the response is unaffected.
        var query = new Mock<IResourceQuery>();
        query.Setup(q => q.GetResourceByProviderAndNameAsync(It.IsAny<ProviderName>(), It.IsAny<ResourceName>(), It.IsAny<IReadOnlyList<ProjectShortName>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sample() with { ProviderResourceName = null });

        var result = await GetResourceEndpoint.HandleAsync(new GetResourceEndpoint.Request("acme", ResName), InScopeCaller(), query.Object, RoleOptions(), CancellationToken.None);

        var ok = Assert.IsType<Ok<ResourceResponse>>(result);
        Assert.Null(ok.Value!.ProviderResourceName);
        Assert.Equal(ResName, ok.Value.Name);
    }

    [Fact]
    public async Task Should_CarryProviderQualifiedName_When_Listing()
    {
        // The list endpoint maps through the same ResourceResponse.From, so the field has to survive there too.
        var query = new Mock<IResourceQuery>();
        query.Setup(q => q.GetResourcesAsync(It.IsAny<ResourceFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryPage<ResourceSummary>([Sample()], 1));

        var result = await GetResourcesEndpoint.HandleAsync(HttpContext(), "acme", null, null, null, null, InScopeCaller(), new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);

        var ok = Assert.IsType<Ok<IReadOnlyList<ResourceResponse>>>(result);
        Assert.Equal(ProviderQualifiedName, Assert.Single(ok.Value!).ProviderResourceName);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_ResourceNameInvalid()
    {
        var query = new Mock<IResourceQuery>();

        // A whitespace-only name fails ResourceName.TryParse → 400, query never hit.
        var result = await GetResourceEndpoint.HandleAsync(new GetResourceEndpoint.Request("acme", "   "), InScopeCaller(), query.Object, RoleOptions(), CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("resource.invalid_name", problem.ProblemDetails.Extensions["code"]);
        query.Verify(q => q.GetResourceByProviderAndNameAsync(It.IsAny<ProviderName>(), It.IsAny<ResourceName>(), It.IsAny<IReadOnlyList<ProjectShortName>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnIdenticalNotFound_When_NameMissingOrOutOfScope()
    {
        // 404-hiding (ADR-0018 rider): the scoped query returns null both for a genuinely-unknown name and a
        // name that exists but is outside the caller's read scope. Both must emit the identical ProblemDetails
        // (status + code + detail) so addressing by name leaks no existence.
        var query = new Mock<IResourceQuery>();
        query.Setup(q => q.GetResourceByProviderAndNameAsync(It.IsAny<ProviderName>(), It.IsAny<ResourceName>(), It.IsAny<IReadOnlyList<ProjectShortName>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResourceSummary?)null);

        var missing = Assert.IsType<ProblemHttpResult>(
            await GetResourceEndpoint.HandleAsync(new GetResourceEndpoint.Request("acme", "does-not-exist"), InScopeCaller(), query.Object, RoleOptions(), CancellationToken.None).ConfigureAwait(false));
        var outOfScope = Assert.IsType<ProblemHttpResult>(
            await GetResourceEndpoint.HandleAsync(new GetResourceEndpoint.Request("acme", "hidden-resource"), InScopeCaller(), query.Object, RoleOptions(), CancellationToken.None).ConfigureAwait(false));

        Assert.Equal(StatusCodes.Status404NotFound, missing.StatusCode);
        Assert.Equal(missing.StatusCode, outOfScope.StatusCode);
        Assert.Equal("resource.not_found", missing.ProblemDetails.Extensions["code"]);
        Assert.Equal(missing.ProblemDetails.Extensions["code"], outOfScope.ProblemDetails.Extensions["code"]);
        Assert.Equal(missing.ProblemDetails.Detail, outOfScope.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_PassNullScope_When_CallerIsAdmin_OnSingleGet()
    {
        IReadOnlyList<ProjectShortName>? captured = null;
        var seen = false;
        await CaptureByIdScopeAsync(AdminCaller(), s => { captured = s; seen = true; });

        // Admin resolves any active resource — passing caller.Scope (empty) would have 404'd it.
        Assert.True(seen);
        Assert.Null(captured);
    }

    [Fact]
    public async Task Should_PassNullScope_When_CallerIsProviderIdentity_OnSingleGet()
    {
        IReadOnlyList<ProjectShortName>? captured = null;
        var seen = false;
        await CaptureByIdScopeAsync(ProviderCaller(), s => { captured = s; seen = true; });

        Assert.True(seen);
        Assert.Null(captured);
    }

    [Fact]
    public async Task Should_PassCallerProjects_When_CallerIsUser_OnSingleGet()
    {
        IReadOnlyList<ProjectShortName>? captured = null;
        await CaptureByIdScopeAsync(InScopeCaller(), s => captured = s);

        Assert.NotNull(captured);
        Assert.Contains(ShortName, captured!);
    }

    [Fact]
    public async Task Should_AllowAnyActive_When_CallerIsServiceKey_OnSingleGet()
    {
        IReadOnlyList<ProjectShortName>? captured = null;
        var seen = false;
        await CaptureByIdScopeAsync(ServiceKeyProviderCaller(), s => { captured = s; seen = true; }, providerName: "other");

        // A service key resolves any active resource, including one outside its own provider (ADR-0018).
        Assert.True(seen);
        Assert.Null(captured);
    }

    [Fact]
    public async Task Should_PassNullScope_When_InteractiveProviderAdminReadsOwnProvider_OnSingleGet()
    {
        IReadOnlyList<ProjectShortName>? captured = null;
        var seen = false;
        await CaptureByIdScopeAsync(InteractiveProviderAdminCaller(), s => { captured = s; seen = true; });

        // Additive widening: their own provider's resources resolve without a project link.
        Assert.True(seen);
        Assert.Null(captured);
    }

    [Fact]
    public async Task Should_PassCallerProjects_When_InteractiveProviderAdminReadsAnotherProvider_OnSingleGet()
    {
        IReadOnlyList<ProjectShortName>? captured = null;
        await CaptureByIdScopeAsync(InteractiveProviderAdminCaller(), s => captured = s, providerName: "other");

        // Containment: outside their own provider the project-assignment gate still applies.
        Assert.NotNull(captured);
        Assert.Contains(ShortName, captured!);
    }

    private static async Task CaptureByIdScopeAsync(
        Caller caller, Action<IReadOnlyList<ProjectShortName>?> capture, string providerName = "acme")
    {
        var query = new Mock<IResourceQuery>();
        query.Setup(q => q.GetResourceByProviderAndNameAsync(It.IsAny<ProviderName>(), It.IsAny<ResourceName>(), It.IsAny<IReadOnlyList<ProjectShortName>?>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderName, ResourceName, IReadOnlyList<ProjectShortName>?, CancellationToken>((_, _, s, _) => capture(s))
            .ReturnsAsync(Sample());

        await GetResourceEndpoint.HandleAsync(
            new GetResourceEndpoint.Request(providerName, ResName), caller, query.Object, RoleOptions(), CancellationToken.None);
    }

    [Fact]
    public async Task Should_ReturnBareArray_AndPassCallerScopeToFilter_When_Listing()
    {
        ResourceFilter? captured = null;
        var http = HttpContext();
        var query = new Mock<IResourceQuery>();
        query.Setup(q => q.GetResourcesAsync(It.IsAny<ResourceFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<ResourceFilter, int, int, CancellationToken>((f, _, _, _) => captured = f)
            .ReturnsAsync(new QueryPage<ResourceSummary>([], 0));

        var result = await GetResourcesEndpoint.HandleAsync(http, "acme", null, null, null, null, InScopeCaller(), new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);

        var ok = Assert.IsType<Ok<IReadOnlyList<ResourceResponse>>>(result);
        Assert.Empty(ok.Value!);
        Assert.Equal("0", http.Response.Headers["X-Total-Count"]);
        Assert.NotNull(captured);
        Assert.Contains(ShortName, captured!.ScopeShortNames!);
        Assert.Equal(ProviderName.Create("acme"), captured.FilterProviderName);
    }

    [Fact]
    public async Task Should_PassLocationTypeToFilter_When_Listing()
    {
        ResourceFilter? captured = null;
        var query = new Mock<IResourceQuery>();
        query.Setup(q => q.GetResourcesAsync(It.IsAny<ResourceFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<ResourceFilter, int, int, CancellationToken>((f, _, _, _) => captured = f)
            .ReturnsAsync(new QueryPage<ResourceSummary>([], 0));

        await GetResourcesEndpoint.HandleAsync(HttpContext(), "acme", null, ELocationType.HPC, null, null, AdminCaller(), new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(ELocationType.HPC, captured!.LocationType);
    }

    [Fact]
    public async Task Should_NotScopeFilter_When_CallerIsAdmin_OnList()
    {
        ResourceFilter? captured = null;
        var query = new Mock<IResourceQuery>();
        query.Setup(q => q.GetResourcesAsync(It.IsAny<ResourceFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<ResourceFilter, int, int, CancellationToken>((f, _, _, _) => captured = f)
            .ReturnsAsync(new QueryPage<ResourceSummary>([], 0));

        await GetResourcesEndpoint.HandleAsync(HttpContext(), "acme", null, null, null, null, AdminCaller(), new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);

        // Admin: neither project scope nor provider scope — only the route provider narrows the set.
        Assert.NotNull(captured);
        Assert.Null(captured!.ScopeShortNames);
        Assert.Null(captured.ScopeProviderName);
        Assert.Equal(ProviderName.Create("acme"), captured.FilterProviderName);
    }

    [Fact]
    public async Task Should_IgnoreProviderClaim_When_ProviderIdentityWithoutProjectsListsUserRoute()
    {
        ResourceFilter? captured = null;
        var query = new Mock<IResourceQuery>();
        query.Setup(q => q.GetResourcesAsync(It.IsAny<ResourceFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<ResourceFilter, int, int, CancellationToken>((f, _, _, _) => captured = f)
            .ReturnsAsync(new QueryPage<ResourceSummary>([], 0));

        await GetResourcesEndpoint.HandleAsync(HttpContext(), "acme", null, null, null, null, ProviderCaller(), new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);

        // The user route has no provider tier: a provider identity sees only its own projects (none here);
        // its provider's resources are read through ProviderAdmin/Providers/{providerName}/Resources.
        Assert.NotNull(captured);
        Assert.Null(captured.ScopeProviderName);
        Assert.NotNull(captured.ScopeShortNames);
        Assert.Empty(captured.ScopeShortNames);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Should_ScopeToProjectsOnly_When_ProviderIdentityListsUserRoute(bool isServiceKeyIdentity)
    {
        ResourceFilter? captured = null;
        var query = new Mock<IResourceQuery>();
        query.Setup(q => q.GetResourcesAsync(It.IsAny<ResourceFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<ResourceFilter, int, int, CancellationToken>((f, _, _, _) => captured = f)
            .ReturnsAsync(new QueryPage<ResourceSummary>([], 0));

        var caller = new Caller(
            new IdpId("t"), "ProviderAdmin", null,
            new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } },
            ProviderName.Create("acme"), IsServiceKeyIdentity: isServiceKeyIdentity);

        await GetResourcesEndpoint.HandleAsync(HttpContext(), "acme", null, null, null, null, caller, new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Null(captured.ScopeProviderName);
        Assert.Equal([ShortName], captured.ScopeShortNames!);
    }

    [Fact]
    public async Task Should_SetTotalCount_AndNextLink_When_MoreResourcesExist()
    {
        var http = HttpContext();
        var query = new Mock<IResourceQuery>();
        query.Setup(q => q.GetResourcesAsync(It.IsAny<ResourceFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryPage<ResourceSummary>([Sample()], 5));

        var result = await GetResourcesEndpoint.HandleAsync(http, "acme", null, null, null, null, InScopeCaller(), new PageRequest(1, 1), query.Object, RoleOptions(), Pagination(), CancellationToken.None);

        var ok = Assert.IsType<Ok<IReadOnlyList<ResourceResponse>>>(result);
        Assert.Single(ok.Value!);
        Assert.Equal("5", http.Response.Headers["X-Total-Count"]);
        Assert.Contains("rel=\"next\"", http.Response.Headers.Link.ToString());
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_ProjectShortNameInvalid()
    {
        var query = new Mock<IResourceQuery>();

        var result = await GetResourcesEndpoint.HandleAsync(HttpContext(), "acme", "x", null, null, null, InScopeCaller(), new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        query.Verify(q => q.GetResourcesAsync(It.IsAny<ResourceFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_ProviderNameInvalid_OnSingleGet()
    {
        var query = new Mock<IResourceQuery>();

        // A whitespace-only provider fails ProviderName.TryCreate → 400, query never hit.
        var result = await GetResourceEndpoint.HandleAsync(new GetResourceEndpoint.Request("   ", ResName), InScopeCaller(), query.Object, RoleOptions(), CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("provider.invalid_name", problem.ProblemDetails.Extensions["code"]);
        query.Verify(q => q.GetResourceByProviderAndNameAsync(It.IsAny<ProviderName>(), It.IsAny<ResourceName>(), It.IsAny<IReadOnlyList<ProjectShortName>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_ProviderNameInvalid_OnList()
    {
        var query = new Mock<IResourceQuery>();

        var result = await GetResourcesEndpoint.HandleAsync(HttpContext(), "   ", null, null, null, null, InScopeCaller(), new PageRequest(null, null), query.Object, RoleOptions(), Pagination(), CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("provider.invalid_name", problem.ProblemDetails.Extensions["code"]);
        query.Verify(q => q.GetResourcesAsync(It.IsAny<ResourceFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
