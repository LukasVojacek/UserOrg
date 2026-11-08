using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Core.DataAccess.Contexts;
using Core.DataAccess.Events;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Shared;
using Core.Features.Location.GetLocationsProviderAdmin;
using Core.Features.Provider.GetProvider;
using Core.Features.Provider.GetProvidersProviderAdmin;
using Core.Features.Provider.UpdateProvider;
using Core.Shared.Paging;
using Core.UnitTests.Shared;

using Forge.Core.Features.Role.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using UserOrg.Shared;

using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Auth;

/// <summary>
/// Drives the claim chain D5 completes, end to end on this side of it: the claims the Gateway mints for an
/// interactive human ProviderAdmin are bound by <see cref="Caller.BindAsync"/> and run through a real endpoint
/// over a real query, rather than a <see cref="Caller"/> handed a <c>ProviderName</c> by the test. The two
/// halves are symmetric in what they are keyed on (ADR-0105): the mint buys the provider read tier and write
/// authority over the caller's own provider, and nothing beyond it — a write to any other provider is refused
/// on the same claim that grants the one write it does allow.
/// </summary>
public sealed class HumanProviderAdminClaimChainTests : IAsyncDisposable
{
    private const string AdministeredProvider = "acme";
    private const string ForeignProvider = "other";
    private const string WriteForbiddenDetail = "This identity may not write to this provider.";

    private readonly ServiceProvider _serviceProvider;

    public HumanProviderAdminClaimChainTests()
    {
        // The database name must be captured outside the options lambda, which runs per DbContext instance —
        // otherwise every scope gets its own empty database and nothing seeded is ever read back.
        var databaseName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DomainEventChannel>();
        services.AddScoped<IRoleService, UserDatabaseAccessTests.TestRoleService>();
        services.Configure<WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped(typeof(IDataQuery<>), typeof(DataQuery<>));
        services.AddScoped<ILocationQuery, LocationDetailsQuery>();
        services.AddScoped<IProviderQuery, ProviderDetailsQuery>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_BindProviderIdentity_When_ClaimsAreTheOnesMintedForAHumanProviderAdmin()
    {
        var caller = await BindAsync(MintedClaims(AdministeredProvider));

        Assert.NotNull(caller);
        Assert.Equal(ProviderName.From(AdministeredProvider), caller.ProviderName);
        Assert.False(caller.IsServiceKeyIdentity);
        Assert.Equal(RoleInfoConstants.ProviderAdministratorRoleName, caller.Role);
    }

    [Fact]
    public async Task Should_ReturnRealLocations_When_HumanProviderAdmin_ViaActualClaimChain()
    {
        await SeedProviderWithLocationAsync(AdministeredProvider, "gpu");
        await SeedProviderWithLocationAsync(ForeignProvider, "cpu");
        var caller = await BindAsync(MintedClaims(AdministeredProvider));

        var locations = await ListProviderAdminLocationsAsync(caller!);

        var only = Assert.Single(locations);
        Assert.Equal("gpu", only.Name);
    }

    /// <summary>
    /// The same caller before D5, i.e. the claim set the Gateway minted yesterday: no <c>provider_name</c>, so
    /// the read tier falls back to a project scope this administrator does not have and the route answers with
    /// nothing. This is the behaviour the mint exists to fix, and the contrast is what stops the test above from
    /// passing against an endpoint that ignores the caller entirely.
    /// </summary>
    [Fact]
    public async Task Should_ReturnNoLocations_When_HumanProviderAdminHasNoProviderNameClaim()
    {
        await SeedProviderWithLocationAsync(AdministeredProvider, "gpu");
        var caller = await BindAsync(MintedClaims(administeredProvider: null));

        var locations = await ListProviderAdminLocationsAsync(caller!);

        Assert.Empty(locations);
    }

    [Fact]
    public async Task Should_ListOnlyTheAdministeredProvider_When_HumanProviderAdmin_ViaActualClaimChain()
    {
        await SeedProviderWithLocationAsync(AdministeredProvider, "gpu");
        await SeedProviderWithLocationAsync(ForeignProvider, "cpu");
        var caller = await BindAsync(MintedClaims(AdministeredProvider));

        var providers = await ListProviderAdminProvidersAsync(caller!);

        var only = Assert.Single(providers);
        Assert.Equal(AdministeredProvider, only.Name);
        Assert.Equal(["gpu"], only.Locations.Select(l => l.Name));
    }

    [Fact]
    public async Task Should_ListNoProviders_When_HumanProviderAdminHasNoProviderNameClaim()
    {
        await SeedProviderWithLocationAsync(AdministeredProvider, "gpu");
        var caller = await BindAsync(MintedClaims(administeredProvider: null));

        var providers = await ListProviderAdminProvidersAsync(caller!);

        Assert.Empty(providers);
    }

    /// <summary>
    /// The write-side twin of the read above, on the caller shape D5 creates. An interactive identity whose
    /// <c>provider_name</c> claim equals the route's provider is authorized by
    /// <see cref="ProviderWriteAuthorization"/> on that claim (ADR-0105 rule 2) — the authentication method is
    /// not consulted, so the mint alone carries the write.
    /// </summary>
    [Fact]
    public async Task Should_AllowProviderWrite_When_HumanProviderAdminWritesOwnProvider_ViaActualClaimChain()
    {
        var caller = await BindAsync(MintedClaims(AdministeredProvider));

        var result = await UpdateProviderAsync(caller!, AdministeredProvider);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsType<NoContent>(result).StatusCode);
    }

    /// <summary>
    /// The live cross-tenant write of 2026-09-12, driven through the real claim chain: the same minted claim
    /// set, the same endpoint, a provider the claim does not name. ADR-0105 rule 2 bounds the mint to one
    /// tenant, so this is 403 <c>provider.write_forbidden</c>. The gate runs before the repository lookup, so
    /// the aggregate handed to the mock is never reached and the assertion does not depend on lookup order.
    /// </summary>
    [Fact]
    public async Task Should_RefuseProviderWrite_When_HumanProviderAdminWritesForeignProvider_ViaActualClaimChain()
    {
        var caller = await BindAsync(MintedClaims(AdministeredProvider));

        var result = await UpdateProviderAsync(caller!, ForeignProvider);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal("provider.write_forbidden", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(WriteForbiddenDetail, problem.ProblemDetails.Detail);
    }

    private static Task<IResult> UpdateProviderAsync(Caller caller, string routeProvider) =>
        UpdateProviderEndpoint.HandleAsync(
            routeProvider,
            new UpdateProviderEndpoint.Request("CZ", Description: null, SupportEmail: null, IsPrivate: false),
            caller,
            ProviderRepository(ProviderAggregate.Create(ProviderName.From(routeProvider), countryId: 1)),
            CountryQuery(),
            Options.Create(new WellKnownRoles()),
            SavingUnitOfWork(),
            new RecordingAuditLogger<UpdateProviderEndpoint.AuditCategory>(),
            NullLogger<UpdateProviderEndpoint.AuditCategory>.Instance,
            TestContext.Current.CancellationToken);

    /// <summary>
    /// The claims <c>TokenService.GenerateTokenAsync</c> writes onto the internal token for an interactive
    /// human ProviderAdmin: the effective role from the permissions snapshot, the Gateway's own authorization
    /// method, the additive provider name D5 mints, and the always-emitted, always-empty <c>provider_id</c> —
    /// empty because kill-switch authority is derived from that claim and stays service-key-only.
    /// <para>This is the Core half of a chain no single test project spans (Core does not reference the
    /// Gateway). The Gateway half —
    /// <c>UserOrg.Gateway.Tests.InternalTokenTransformProviderNameMintTests.Should_CarryProviderNameOnTheMintedToken_When_HumanProviderAdmin</c>
    /// — decodes the JWT the real <c>TokenService</c> puts on the wire and asserts these same three claims by
    /// their literal names. The halves meet at <see cref="AuthConstants"/>/<see cref="EAuthorizationMethod"/>,
    /// which both productions read: change what is minted here without changing that test and the seam is
    /// open.</para>
    /// </summary>
    private static Claim[] MintedClaims(string? administeredProvider)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "human-1"),
            new(JwtRegisteredClaimNames.PreferredUsername, "human-1"),
            new(ClaimTypes.Role, RoleInfoConstants.ProviderAdministratorRoleName),
            new(EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.JwtBearerToken)),
            new(AuthConstants.ProviderIdClaimType, string.Empty)
        };
        if (administeredProvider is not null)
        {
            claims.Add(new Claim(AuthConstants.ProviderNameClaimType, administeredProvider));
        }
        return [.. claims];
    }

    private static ValueTask<Caller?> BindAsync(Claim[] claims) =>
        Caller.BindAsync(new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "GatewayJwt")) });

    private async Task<IReadOnlyList<GetLocationsProviderAdminEndpoint.Response>> ListProviderAdminLocationsAsync(Caller caller)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/ProviderAdmin/Locations";

        var result = await GetLocationsProviderAdminEndpoint.HandleAsync(
            http,
            caller,
            new PageRequest(null, null),
            scope.ServiceProvider.GetRequiredService<ILocationQuery>(),
            Options.Create(new WellKnownRoles()),
            Options.Create(new PaginationOptions()),
            TestContext.Current.CancellationToken);

        return Assert.IsAssignableFrom<IReadOnlyList<GetLocationsProviderAdminEndpoint.Response>>(
            Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
    }

    private async Task<IReadOnlyList<GetProviderResponse>> ListProviderAdminProvidersAsync(Caller caller)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/ProviderAdmin/Providers";

        var result = await GetProvidersProviderAdminEndpoint.HandleAsync(
            http,
            caller,
            new PageRequest(null, null),
            scope.ServiceProvider.GetRequiredService<IProviderQuery>(),
            Options.Create(new WellKnownRoles()),
            Options.Create(new PaginationOptions()),
            TestContext.Current.CancellationToken);

        return Assert.IsAssignableFrom<IReadOnlyList<GetProviderResponse>>(
            Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
    }

    private async Task SeedProviderWithLocationAsync(string providerName, string locationName)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        await EnsureLocationTypeAsync(context, ELocationType.HPC, ct);
        await EnsureCountryAsync(context, countryId: 1, ct);

        var provider = ProviderAggregate.Create(ProviderName.From(providerName), countryId: 1);
        Assert.True(provider.AddLocation(LocationName.From(locationName), $"https://{locationName}", ELocationType.HPC, isShared: true).IsSuccess);
        context.Add(provider);

        // The in-memory provider does not run SQL defaults, so the persistence-only shadow "ServiceName"
        // (HasDefaultValueSql('LEXIS') on Location) stays null and trips its required check.
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added
                && entry.Metadata.FindProperty("ServiceName") is not null
                && entry.Property("ServiceName").CurrentValue is null)
            {
                entry.Property("ServiceName").CurrentValue = "LEXIS";
            }
        }

        await context.SaveChangesAsync(ct);
    }

    private static async Task EnsureLocationTypeAsync(DatabaseContext context, ELocationType id, CancellationToken ct)
    {
        if (await context.Set<LocationType>().AnyAsync(t => t.Id == id, ct))
            return;

        // LocationType is seeded reference data with no public factory; instantiate it directly.
        var locationType = (LocationType)Activator.CreateInstance(typeof(LocationType), nonPublic: true)!;
        typeof(LocationType).GetProperty(nameof(LocationType.Id))!.SetValue(locationType, id);
        typeof(LocationType).GetProperty(nameof(LocationType.Name))!.SetValue(locationType, "HPC");
        context.Add(locationType);
        await context.SaveChangesAsync(ct);
    }

    private static async Task EnsureCountryAsync(DatabaseContext context, int countryId, CancellationToken ct)
    {
        if (await context.Set<Country>().AnyAsync(c => c.Id == countryId, ct))
            return;

        context.Add(TestCountry.Create(countryId));
        await context.SaveChangesAsync(ct);
    }

    private static IProviderRepository ProviderRepository(ProviderAggregate provider)
    {
        var repository = new Mock<IProviderRepository>();
        repository.Setup(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProviderAggregate?>(provider));
        return repository.Object;
    }

    private static ICountryQuery CountryQuery()
    {
        var query = new Mock<ICountryQuery>();
        query.Setup(q => q.FindIdByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return query.Object;
    }

    private static IUnitOfWork SavingUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork.Object;
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();
}
