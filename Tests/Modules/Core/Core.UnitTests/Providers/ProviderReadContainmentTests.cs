using Core.DataAccess.Contexts;
using Core.DataAccess.Events;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Features.Provider.GetProvider;
using Core.UnitTests.Shared;

using Forge.Core.Features.Role.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using DomainProviderName = Core.Domain.Provider.ProviderName;
using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Providers;

/// <summary>
/// The by-name provider read driven end to end — <see cref="GetProviderEndpoint"/> over the real
/// <see cref="ProviderDetailsQuery"/> on the EF Core in-memory provider — so the tier the endpoint picks
/// and the reachability rule the query applies are asserted together as one containment property.
/// </summary>
public sealed class ProviderReadContainmentTests : IAsyncDisposable
{
    private static readonly WellKnownRoles Roles = new();

    private readonly ServiceProvider _serviceProvider;

    public ProviderReadContainmentTests()
    {
        var dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DomainEventChannel>();
        services.AddScoped<IRoleService, UserDatabaseAccessTests.TestRoleService>();
        services.Configure<WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped(typeof(IDataQuery<>), typeof(DataQuery<>));
        services.AddScoped<IProviderQuery, ProviderDetailsQuery>();

        _serviceProvider = services.BuildServiceProvider();
        SeedCountryOfEveryProvider();
    }

    private void SeedCountryOfEveryProvider()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        context.Add(TestCountry.Create());
        context.SaveChanges();
    }

    // A human ProviderAdmin with no project roles at all — the strictest shape of the interactive tier.
    private static Caller InteractiveProviderAdminCaller() => new(
        new IdpId("h"), "ProviderAdmin", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(), DomainProviderName.Create("acme"));

    private static Caller ServiceKeyCaller() => new(
        new IdpId("k"), "ProviderAdmin", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(), DomainProviderName.Create("acme"),
        IsServiceKeyIdentity: true);

    [Fact]
    public async Task Should_Deny_PrivateOtherProvider_When_CallerIsInteractive()
    {
        await SeedProviderAsync("other-co", isPrivate: true);

        var result = await GetAsync("other-co", InteractiveProviderAdminCaller());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task Should_Allow_PublicProvider_When_CallerIsInteractive()
    {
        await SeedProviderAsync("public-co", isPrivate: false);

        var result = await GetAsync("public-co", InteractiveProviderAdminCaller());

        // The interactive rule is additive: a public provider stays resolvable by name even for a
        // ProviderAdmin with an empty project scope. Narrowing the tier to "own provider only" would
        // take away a read they have today.
        var ok = Assert.IsType<Ok<GetProviderResponse>>(result);
        Assert.Equal("public-co", ok.Value!.Name);
    }

    [Fact]
    public async Task Should_Allow_OwnPrivateProvider_When_CallerIsInteractive()
    {
        await SeedProviderAsync("acme", isPrivate: true);

        var result = await GetAsync("acme", InteractiveProviderAdminCaller());

        var ok = Assert.IsType<Ok<GetProviderResponse>>(result);
        Assert.Equal("acme", ok.Value!.Name);
    }

    [Fact]
    public async Task Should_AllowAnyActive_When_CallerIsServiceKey()
    {
        await SeedProviderAsync("other-co", isPrivate: true);

        var result = await GetAsync("other-co", ServiceKeyCaller());

        // Unchanged by containment: a provider service key resolves any active provider (ADR-0018).
        var ok = Assert.IsType<Ok<GetProviderResponse>>(result);
        Assert.Equal("other-co", ok.Value!.Name);
    }

    private async Task<IResult> GetAsync(string providerName, Caller caller)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IProviderQuery>();

        return await GetProviderEndpoint.HandleAsync(
            new GetProviderEndpoint.Request(providerName), caller, query, Options.Create(Roles),
            TestContext.Current.CancellationToken);
    }

    private async Task SeedProviderAsync(string name, bool isPrivate)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        context.Add(ProviderAggregate.Create(DomainProviderName.From(name), countryId: 1, isPrivate: isPrivate));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();
}
