using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Provider.GetProvider;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

using DomainProviderName = Core.Domain.Provider.ProviderName;

namespace Core.UnitTests.Providers;

public sealed class GetProviderEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly WellKnownRoles Roles = new();

    private static IOptions<WellKnownRoles> RoleOptions() => Options.Create(Roles);

    private static Caller UserCaller() => new(
        new IdpId("u"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private static Caller UserCallerWithNoProjects() => new(
        new IdpId("u2"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller AdminCaller() => new(
        new IdpId("a"), Roles.DefaultAdminRoleName, null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller ProviderCaller() => new(
        new IdpId("p"), "ProviderAdmin", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(), DomainProviderName.Create("acme"));

    private static Caller ServiceKeyProviderCaller() => new(
        new IdpId("k"), "ProviderAdmin", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(), DomainProviderName.Create("acme"),
        IsServiceKeyIdentity: true);

    private static Caller InteractiveProviderAdminCaller() => new(
        new IdpId("h"), "ProviderAdmin", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } },
        DomainProviderName.Create("acme"));

    private static ProviderDetail SampleDetail() => new(
        DomainProviderName.From("acme"), Description: null, SupportEmail: null, ExternalId: null,
        Mode: EOperationMode.Editable, ServiceName: ServiceName.Default, IsPrivate: false,
        CountryCode: "CZ", IsDeleted: false, Locations: []);

    private static async Task<IResult> InvokeAsync(
        ProviderDetail? provider, Caller? caller = null, Action<IReadOnlyList<ProjectShortName>?>? captureScope = null, string providerName = "acme")
    {
        var query = new Mock<IProviderQuery>();
        query.Setup(q => q.GetProviderDetailsAsync(It.IsAny<DomainProviderName>(), It.IsAny<IReadOnlyList<ProjectShortName>?>(), It.IsAny<CancellationToken>()))
            .Callback<DomainProviderName, IReadOnlyList<ProjectShortName>?, CancellationToken>((_, s, _) => captureScope?.Invoke(s))
            .ReturnsAsync(provider);

        return await GetProviderEndpoint.HandleAsync(
            new GetProviderEndpoint.Request(providerName), caller ?? AdminCaller(), query.Object, RoleOptions(), CancellationToken.None);
    }

    [Fact]
    public async Task Should_ReturnNotFoundProblemWithCode_When_ProviderNotFound()
    {
        var result = await InvokeAsync(provider: null);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("provider.not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Theory]
    [InlineData(0)] // blank — ProviderName.cs:21's IsNullOrWhiteSpace branch
    [InlineData(101)] // exceeds the 100-character limit — ProviderName.cs:28's length branch
    public async Task Should_ReturnBadRequestWithInvalidNameCode_When_ProviderNameIsInvalid(int nameLength)
    {
        var result = await InvokeAsync(provider: null, providerName: new string('a', nameLength));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("provider.invalid_name", problem.ProblemDetails.Extensions["code"]);
        Assert.False(string.IsNullOrWhiteSpace(problem.ProblemDetails.Detail));
    }

    [Fact]
    public async Task Should_ReturnOk_And_MapProvider_When_Found()
    {
        var result = await InvokeAsync(SampleDetail());

        var ok = Assert.IsType<Ok<GetProviderResponse>>(result);
        Assert.Equal("acme", ok.Value!.Name);
        Assert.Equal("CZ", ok.Value.CountryCode);
        Assert.Empty(ok.Value.Locations);
    }

    [Fact]
    public async Task Should_PassNullScope_When_CallerIsAdmin()
    {
        IReadOnlyList<ProjectShortName>? captured = null;
        var seen = false;

        await InvokeAsync(SampleDetail(), AdminCaller(), s => { captured = s; seen = true; });

        // Admin resolves any active provider — no project scope passed to the query.
        Assert.True(seen);
        Assert.Null(captured);
    }

    [Fact]
    public async Task Should_PassNullScope_When_CallerIsProviderIdentity()
    {
        IReadOnlyList<ProjectShortName>? captured = null;
        var seen = false;

        await InvokeAsync(SampleDetail(), ProviderCaller(), s => { captured = s; seen = true; });

        // Own provider ⇒ unrestricted (D10): this caller's providerName claim matches the route's target,
        // not "any provider identity resolves unscoped" — an interactive provider identity reading another
        // provider's record falls back to its project scope (see ProviderReadContainmentTests). Only a
        // service key resolves any active provider by name, which the next test pins separately.
        Assert.True(seen);
        Assert.Null(captured);
    }

    [Fact]
    public async Task Should_AllowAnyActive_When_CallerIsServiceKey()
    {
        IReadOnlyList<ProjectShortName>? captured = null;
        var seen = false;

        await InvokeAsync(SampleDetail(), ServiceKeyProviderCaller(), s => { captured = s; seen = true; }, providerName: "other");

        // A service key resolves any active provider by name, including one it does not own (ADR-0018).
        Assert.True(seen);
        Assert.Null(captured);
    }

    [Fact]
    public async Task Should_PassNullScope_When_InteractiveProviderAdminReadsOwnProvider()
    {
        IReadOnlyList<ProjectShortName>? captured = null;
        var seen = false;

        await InvokeAsync(SampleDetail(), InteractiveProviderAdminCaller(), s => { captured = s; seen = true; }, providerName: "acme");

        Assert.True(seen);
        Assert.Null(captured);
    }

    [Fact]
    public async Task Should_PassCallerProjects_When_InteractiveProviderAdminReadsAnotherProvider()
    {
        IReadOnlyList<ProjectShortName>? captured = null;

        await InvokeAsync(SampleDetail(), InteractiveProviderAdminCaller(), s => captured = s, providerName: "other");

        // Containment: a human ProviderAdmin reading outside their own provider is back on the user tier,
        // so a private provider resolves only when reachable through one of their projects.
        Assert.NotNull(captured);
        Assert.Contains(ShortName, captured!);
    }

    [Fact]
    public async Task Should_PassCallerProjects_When_CallerIsUser()
    {
        IReadOnlyList<ProjectShortName>? captured = null;

        await InvokeAsync(SampleDetail(), UserCaller(), s => captured = s);

        // User tier: the query gets the caller's projects so a private provider resolves only if reachable.
        Assert.NotNull(captured);
        Assert.Contains(ShortName, captured!);
    }

    [Fact]
    public async Task Should_PassEmptyScoped_When_CallerIsUserWithNoProjects()
    {
        IReadOnlyList<ProjectShortName>? captured = null;

        await InvokeAsync(SampleDetail(), UserCallerWithNoProjects(), s => captured = s);

        // Distinct from Should_PassNullScope_When_CallerIsProviderIdentity above: a user with no project
        // roles still resolves to Scoped(empty) — an empty LIST, never the provider identity's null.
        Assert.NotNull(captured);
        Assert.Empty(captured!);
    }
}
