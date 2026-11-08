using Core.DataAccess.Contexts;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Providers;

/// <summary>
/// Tests for <see cref="ProviderDetailsQuery"/>, covering the <see cref="ProviderFilter.OnlyPublic"/> read-scope
/// tier added in F0.2 (the user tier hides private providers, the admin/provider tiers see them) and the
/// <see cref="ProviderFilter.CountryCode"/> filter and projection. Runs on SQLite rather than EF InMemory: the
/// country code lives on the <c>Country</c> row, which <c>Provider</c> reaches only through its <c>CountryId</c>
/// column, and a comparison on a value-converted column that InMemory evaluates happily can have no SQL
/// translation (see <c>CountryQuery.FindIdByCodeAsync</c>).
/// </summary>
public sealed class ProviderDetailsQueryTests : SqliteQueryHarness
{
    private const int CzechiaId = 1;
    private const int SlovakiaId = 2;

    protected override void AddServices(IServiceCollection services) =>
        services.AddScoped<IProviderQuery, ProviderDetailsQuery>();

    private protected override Task SeedAsync(DatabaseContext context) =>
        context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Country" ("Id", "Name", "Description") VALUES ({0}, 'CZ', 'Czechia'), ({1}, 'SK', 'Slovakia');
            INSERT INTO "LocationType" ("Id", "Name", "Description") VALUES ({2}, 'HPC', NULL);
            """,
            CzechiaId, SlovakiaId, (int)ELocationType.HPC);

    [Fact]
    public async Task Should_ReturnBothPublicAndPrivate_When_NotOnlyPublic()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("public-co", isPrivate: false);
        await SeedProviderAsync("private-co", isPrivate: true);

        var result = await QueryAsync(q => q.GetProvidersAsync(new ProviderFilter(null, null), 1, 50, ct));

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalItems);
    }

    [Fact]
    public async Task Should_HidePrivate_When_OnlyPublic()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("public-co", isPrivate: false);
        await SeedProviderAsync("private-co", isPrivate: true);

        var result = await QueryAsync(q => q.GetProvidersAsync(new ProviderFilter(null, null, OnlyPublic: true), 1, 50, ct));

        // Scope filter must compose before paging: TotalItems reflects the scoped (public-only) set.
        Assert.Equal(1, result.TotalItems);
        var only = Assert.Single(result.Items);
        Assert.Equal("public-co", only.Name.Value);
        Assert.False(only.IsPrivate);
    }

    [Fact]
    public async Task Should_ReturnNamedProvider_RegardlessOfPrivate_When_FilteredByName()
    {
        // The provider-identity tier pins Name to its own provider; a private provider must still resolve,
        // and a second (non-matching) provider must be excluded — so Single actually proves the filter.
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("private-co", isPrivate: true);
        await SeedProviderAsync("public-co", isPrivate: false);

        var result = await QueryAsync(q => q.GetProvidersAsync(new ProviderFilter(ProviderName.From("private-co"), null), 1, 50, ct));

        var only = Assert.Single(result.Items);
        Assert.Equal("private-co", only.Name.Value);
    }

    [Fact]
    public async Task Should_ReturnOwnPlusPublic_When_OwnProviderNameSet_AndScopeEmpty()
    {
        // The provider tier of the list read: the caller's own provider stays visible even though it is
        // private and no project reaches it, and the public set it can already see comes along.
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("acme", isPrivate: true);
        await SeedProviderAsync("public-co", isPrivate: false);
        await SeedProviderAsync("other-private", isPrivate: true);

        var result = await QueryAsync(q => q.GetProvidersAsync(
            new ProviderFilter(null, null, OnlyPublic: true, ScopeShortNames: [], OwnProviderName: ProviderName.From("acme")),
            1, 50, ct));

        Assert.Equal(["acme", "public-co"], result.Items.Select(p => p.Name.Value));
        Assert.Equal(2, result.TotalItems);
    }

    [Fact]
    public async Task Should_PageWithoutGapsOrDuplicates_When_Paging()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("alpha", isPrivate: false);
        await SeedProviderAsync("bravo", isPrivate: false);
        await SeedProviderAsync("charlie", isPrivate: false);

        var page1 = await QueryAsync(q => q.GetProvidersAsync(new ProviderFilter(null, null), 1, 2, ct));
        var page2 = await QueryAsync(q => q.GetProvidersAsync(new ProviderFilter(null, null), 2, 2, ct));

        Assert.Equal(3, page1.TotalItems);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2.Items);

        // Stable order with a unique tie-breaker: no row appears on both pages, all three are covered.
        var names = page1.Items.Concat(page2.Items).Select(p => p.Name.Value).ToList();
        Assert.Equal(3, names.Distinct().Count());
        Assert.Equal(["alpha", "bravo", "charlie"], names);
    }

    [Fact]
    public async Task Should_ReturnCountryCode_When_ListingProviders()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("cz-co", isPrivate: false, CzechiaId);
        await SeedProviderAsync("sk-co", isPrivate: false, SlovakiaId);

        var result = await QueryAsync(q => q.GetProvidersAsync(new ProviderFilter(null, null), 1, 50, ct));

        Assert.Equal([("cz-co", "CZ"), ("sk-co", "SK")], result.Items.Select(p => (p.Name.Value, p.CountryCode)));
    }

    [Fact]
    public async Task Should_ReturnOnlyProvidersOfTheCountry_When_FilteredByCountryCode()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("cz-co", isPrivate: false, CzechiaId);
        await SeedProviderAsync("sk-co", isPrivate: false, SlovakiaId);

        var result = await QueryAsync(q => q.GetProvidersAsync(new ProviderFilter(null, CountryCode: "SK"), 1, 50, ct));

        var only = Assert.Single(result.Items);
        Assert.Equal("sk-co", only.Name.Value);
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task Should_ReturnEmptyPage_When_CountryCodeUnknown()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("cz-co", isPrivate: false, CzechiaId);

        var result = await QueryAsync(q => q.GetProvidersAsync(new ProviderFilter(null, CountryCode: "XX"), 1, 50, ct));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalItems);

        var unfiltered = await QueryAsync(q => q.GetProvidersAsync(new ProviderFilter(null, null), 1, 50, ct));
        Assert.Single(unfiltered.Items);
    }

    [Fact]
    public async Task Should_ReturnCountryCode_When_GettingDetails()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("cz-co", isPrivate: false, CzechiaId);
        await SeedProviderWithLocationAsync("sk-co", "gpu", SlovakiaId);

        var result = await DetailAsync(q => q.GetProviderDetailsAsync(ProviderName.From("sk-co"), null, ct));

        Assert.NotNull(result);
        Assert.Equal("SK", result.CountryCode);
        Assert.Single(result.Locations);
    }

    [Fact]
    public async Task Should_ReturnProviderWithLocationHeaders_When_GettingDetails()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderWithLocationAsync("acme", "gpu");

        var result = await DetailAsync(q => q.GetProviderDetailsAsync(ProviderName.From("acme"), null, ct));

        Assert.NotNull(result);
        Assert.Equal("acme", result.Name.Value);
        var location = Assert.Single(result.Locations);
        Assert.Equal("gpu", location.Name.Value);
    }

    [Fact]
    public async Task Should_ReturnNull_When_ProviderDetailsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await DetailAsync(q => q.GetProviderDetailsAsync(ProviderName.From("missing"), null, ct));

        Assert.Null(result);
    }

    [Fact]
    public async Task Should_FindPublicProvider_When_CallerScopeIsEmpty()
    {
        // Baseline for D10: only IsPrivate gates the reachability check, so an empty scope must still
        // resolve a public provider once the interactive-vs-service-key narrowing lands on top of this.
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("public-co", isPrivate: false);

        var result = await DetailAsync(q => q.GetProviderDetailsAsync(ProviderName.From("public-co"), [], ct));

        Assert.NotNull(result);
        Assert.Equal("public-co", result.Name.Value);
    }

    [Fact]
    public async Task Should_NotFindPrivateProvider_When_CallerScopeIsEmpty()
    {
        // Baseline for D10: an empty scope can never reach a private provider today — this reachability
        // rule must survive the interactive-vs-service-key narrowing untouched.
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("private-co", isPrivate: true);

        var result = await DetailAsync(q => q.GetProviderDetailsAsync(ProviderName.From("private-co"), [], ct));

        Assert.Null(result);

        // Control: unscoped (scope == null) the very same row resolves, so the null above is the reachability
        // gate rejecting it — not a seed that never landed, which would make this test pass for free.
        var unscoped = await DetailAsync(q => q.GetProviderDetailsAsync(ProviderName.From("private-co"), null, ct));
        Assert.NotNull(unscoped);
    }

    [Fact]
    public async Task Should_ReturnEveryActiveProviderWithLocations_When_AdministeredNamesUnset()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderWithLocationAsync("acme", "gpu");
        await SeedProviderAsync("private-co", isPrivate: true);

        var result = await AdministeredAsync(q => q.GetAdministeredProvidersAsync(new AdministeredProviderFilter(null), 1, 50, ct));

        Assert.Equal(2, result.TotalItems);
        Assert.Equal(["acme", "private-co"], result.Items.Select(p => p.Name.Value));
        Assert.Equal(["gpu"], result.Items[0].Locations.Select(l => l.Name.Value));
        Assert.True(result.Items[1].IsPrivate);
        Assert.Empty(result.Items[1].Locations);
    }

    [Fact]
    public async Task Should_ReturnOnlyNamedProviders_When_AdministeredNamesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("acme", isPrivate: true);
        await SeedProviderAsync("globex", isPrivate: false);
        await SeedProviderAsync("other-co", isPrivate: false);

        var result = await AdministeredAsync(q => q.GetAdministeredProvidersAsync(
            new AdministeredProviderFilter([ProviderName.From("acme"), ProviderName.From("globex")]), 1, 50, ct));

        Assert.Equal(2, result.TotalItems);
        Assert.Equal(["acme", "globex"], result.Items.Select(p => p.Name.Value));
    }

    [Fact]
    public async Task Should_ReturnEmptyPage_When_AdministeredNamesEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("acme", isPrivate: false);

        var result = await AdministeredAsync(q => q.GetAdministeredProvidersAsync(new AdministeredProviderFilter([]), 1, 50, ct));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalItems);
    }

    [Fact]
    public async Task Should_HideDeactivatedProvider_When_IncludeInactiveNotSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("acme", isPrivate: false);
        await SeedProviderWithLocationAsync("retired-co", "gpu", deactivated: true);

        var result = await AdministeredAsync(q => q.GetAdministeredProvidersAsync(new AdministeredProviderFilter(null), 1, 50, ct));

        Assert.Equal(1, result.TotalItems);
        var only = Assert.Single(result.Items);
        Assert.Equal("acme", only.Name.Value);
    }

    [Fact]
    public async Task Should_ReturnDeactivatedProviderWithLiveLocations_When_IncludeInactive()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("acme", isPrivate: false);
        await SeedProviderWithLocationAsync("retired-co", "gpu", deactivated: true, removedLocationName: "old");

        var result = await AdministeredAsync(q => q.GetAdministeredProvidersAsync(
            new AdministeredProviderFilter(null, IncludeInactive: true), 1, 50, ct));

        Assert.Equal(2, result.TotalItems);
        Assert.Equal([("acme", false), ("retired-co", true)], result.Items.Select(p => (p.Name.Value, p.IsDeleted)));
        Assert.Equal(["gpu"], result.Items[1].Locations.Select(l => l.Name.Value));
    }

    [Fact]
    public async Task Should_ListBothNameTwinsActiveFirst_When_IncludeInactive()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedNameTwinAsync("twin-co", Guid.Parse("00000000-0000-0000-0000-000000000001"), deactivated: true);
        await SeedNameTwinAsync("twin-co", Guid.Parse("00000000-0000-0000-0000-000000000002"), deactivated: false);

        var result = await AdministeredAsync(q => q.GetAdministeredProvidersAsync(
            new AdministeredProviderFilter(null, IncludeInactive: true), 1, 50, ct));

        Assert.Equal(2, result.TotalItems);
        Assert.Equal([("twin-co", false), ("twin-co", true)], result.Items.Select(p => (p.Name.Value, p.IsDeleted)));
    }

    [Fact]
    public async Task Should_PageAdministeredProvidersWithoutGapsOrDuplicates_When_Paging()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("charlie", isPrivate: false);
        await SeedProviderAsync("alpha", isPrivate: false);
        await SeedProviderAsync("bravo", isPrivate: false);

        var page1 = await AdministeredAsync(q => q.GetAdministeredProvidersAsync(new AdministeredProviderFilter(null), 1, 2, ct));
        var page2 = await AdministeredAsync(q => q.GetAdministeredProvidersAsync(new AdministeredProviderFilter(null), 2, 2, ct));

        Assert.Equal(3, page1.TotalItems);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2.Items);
        Assert.Equal(["alpha", "bravo", "charlie"], page1.Items.Concat(page2.Items).Select(p => p.Name.Value));
    }

    [Fact]
    public async Task Should_NotReturnDeactivatedProviderOutsideNames_When_IncludeInactiveAndNamesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("acme", isPrivate: false);
        await SeedProviderAsync("other-co", isPrivate: false, deactivated: true);

        var result = await AdministeredAsync(q => q.GetAdministeredProvidersAsync(
            new AdministeredProviderFilter([ProviderName.From("acme")], IncludeInactive: true), 1, 50, ct));

        Assert.Equal(1, result.TotalItems);
        var only = Assert.Single(result.Items);
        Assert.Equal("acme", only.Name.Value);
    }

    [Fact]
    public async Task Should_ReturnNull_When_ProviderDeactivated()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderWithLocationAsync("retired-co", "gpu", deactivated: true);

        var result = await DetailAsync(q => q.GetProviderDetailsAsync(ProviderName.From("retired-co"), null, ct));

        Assert.Null(result);
    }

    [Fact]
    public async Task Should_OrderLocationsByName_When_ReadingAdministeredProvidersAndDetails()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderWithLocationsAddedOneByOneAsync("acme", "zeta", "alpha", "mid");

        var listed = await AdministeredAsync(q => q.GetAdministeredProvidersAsync(new AdministeredProviderFilter(null), 1, 50, ct));
        var detail = await DetailAsync(q => q.GetProviderDetailsAsync(ProviderName.From("acme"), null, ct));

        Assert.Equal(["alpha", "mid", "zeta"], Assert.Single(listed.Items).Locations.Select(l => l.Name.Value));
        Assert.NotNull(detail);
        Assert.Equal(["alpha", "mid", "zeta"], detail.Locations.Select(l => l.Name.Value));
    }

    private async Task<QueryPage<ProviderSummary>> QueryAsync(Func<IProviderQuery, Task<QueryPage<ProviderSummary>>> act)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IProviderQuery>();
        return await act(query);
    }

    private async Task<QueryPage<ProviderDetail>> AdministeredAsync(Func<IProviderQuery, Task<QueryPage<ProviderDetail>>> act)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IProviderQuery>();
        return await act(query);
    }

    private async Task<ProviderDetail?> DetailAsync(Func<IProviderQuery, Task<ProviderDetail?>> act)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IProviderQuery>();
        return await act(query);
    }

    private async Task SeedProviderWithLocationAsync(
        string name, string locationName, int countryId = CzechiaId, bool deactivated = false, string? removedLocationName = null)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(ProviderName.From(name), countryId);
        Assert.True(provider.AddLocation(LocationName.From(locationName), $"https://{locationName}", ELocationType.HPC, isShared: false).IsSuccess);
        if (removedLocationName is not null)
        {
            Assert.True(provider.AddLocation(LocationName.From(removedLocationName), $"https://{removedLocationName}", ELocationType.HPC, isShared: false).IsSuccess);
            Assert.True(provider.RemoveLocation(LocationName.From(removedLocationName)).IsSuccess);
        }
        if (deactivated)
        {
            Assert.True(provider.Deactivate().IsSuccess);
        }
        context.Add(provider);

        await context.SaveChangesAsync(ct);
    }

    private async Task SeedProviderWithLocationsAddedOneByOneAsync(string name, params string[] locationNames)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(ProviderName.From(name), CzechiaId);
        context.Add(provider);
        foreach (var locationName in locationNames)
        {
            Assert.True(provider.AddLocation(LocationName.From(locationName), $"https://{locationName}", ELocationType.HPC, isShared: false).IsSuccess);
            await context.SaveChangesAsync(ct);
        }
    }

    private async Task SeedNameTwinAsync(string name, Guid id, bool deactivated)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Provider" ("Id", "Name", "ServiceName", "CountryId", "IsDeleted") VALUES ({0}, {1}, 'LEXIS', {2}, {3});
            """,
            [id.ToString().ToUpperInvariant(), name, CzechiaId, deactivated],
            TestContext.Current.CancellationToken);
    }

    private async Task SeedProviderAsync(string name, bool isPrivate, int countryId = CzechiaId, bool deactivated = false)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(ProviderName.From(name), countryId, isPrivate: isPrivate);
        if (deactivated)
        {
            Assert.True(provider.Deactivate().IsSuccess);
        }
        context.Add(provider);
        await context.SaveChangesAsync(ct);
    }
}
