using Core.DataAccess.Contexts;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Core.UnitTests.Providers;

/// <summary>
/// Characterization tests for the user read-scope branch of <see cref="ProviderDetailsQuery.GetProvidersAsync"/>
/// on SQLite: a private provider stays visible under <see cref="ProviderFilter.OnlyPublic"/> only when one of the
/// caller's projects reaches it through a live project link and a live assignment, and the scope composes with the
/// name and country filters before paging. <c>ProviderDetailsQueryTests</c> covers the other branches on SQLite.
/// </summary>
public sealed class ProviderListScopeSqliteTests : SqliteQueryHarness
{
    private SqliteCoreSeed Seed => new(ServiceProvider);

    protected override void AddServices(IServiceCollection services) =>
        services.AddScoped<IProviderQuery, ProviderDetailsQuery>();

    private protected override Task SeedAsync(DatabaseContext context) =>
        context.Database.ExecuteSqlRawAsync("""INSERT INTO "Country" ("Id", "Name", "Description") VALUES (1, 'CZ', 'Czechia');""");

    [Fact]
    public async Task A_private_provider_reached_through_a_live_link_and_assignment_stays_visible_to_the_scope()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var scoped = await QueryAsync(Scoped([new ProjectShortName("proj-a")]), ct);

        Assert.Equal(["public-co", "reached-co"], Names(scoped));
        Assert.Equal(2, scoped.TotalItems);
    }

    [Fact]
    public async Task An_empty_or_unreaching_scope_sees_only_public_providers()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var empty = await QueryAsync(Scoped([]), ct);
        var unreaching = await QueryAsync(Scoped([new ProjectShortName("proj-none")]), ct);

        Assert.Equal(["public-co"], Names(empty));
        Assert.Equal(["public-co"], Names(unreaching));
    }

    [Fact]
    public async Task The_scope_composes_with_the_name_and_country_filters()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();
        var scope = new[] { new ProjectShortName("proj-a") };

        var byName = await QueryAsync(Scoped(scope) with { Name = ProviderName.From("reached-co") }, ct);
        var hiddenByName = await QueryAsync(Scoped(scope) with { Name = ProviderName.From("unlinked-co") }, ct);
        var byCountry = await QueryAsync(Scoped(scope) with { CountryCode = "CZ" }, ct);
        var otherCountry = await QueryAsync(Scoped(scope) with { CountryCode = "SK" }, ct);

        Assert.Equal(["reached-co"], Names(byName));
        Assert.Empty(hiddenByName.Items);
        Assert.Equal(["public-co", "reached-co"], Names(byCountry));
        Assert.Empty(otherCountry.Items);
        Assert.Equal(0, otherCountry.TotalItems);
    }

    private static ProviderFilter Scoped(IReadOnlyList<ProjectShortName> scope) =>
        new(null, null, OnlyPublic: true, ScopeShortNames: scope);

    private async Task SeedGraphAsync()
    {
        var project = await Seed.ProjectAsync("proj-a");
        await Seed.ProviderAsync("public-co");
        await Seed.ProviderAsync("unreached-co", isPrivate: true);

        await PrivateProviderAsync("reached-co", project, deletedLink: false, deletedAssignment: false);
        await PrivateProviderAsync("unlinked-co", project, deletedLink: true, deletedAssignment: false);
        await PrivateProviderAsync("unassigned-co", project, deletedLink: false, deletedAssignment: true);
    }

    private async Task PrivateProviderAsync(string name, ProjectId project, bool deletedLink, bool deletedAssignment)
    {
        var provider = await Seed.ProviderAsync(name, isPrivate: true);
        var location = await Seed.LocationAsync(provider, $"{name}-loc");
        var resource = await Seed.ResourceAsync(provider, $"{name}-res");
        await Seed.AssignAsync(resource, location, deleted: deletedAssignment);
        await Seed.LinkAsync(project, resource, deleted: deletedLink);
    }

    private static IEnumerable<string> Names(QueryPage<ProviderSummary> page) => page.Items.Select(p => p.Name.Value);

    private async Task<QueryPage<ProviderSummary>> QueryAsync(ProviderFilter filter, CancellationToken ct)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IProviderQuery>().GetProvidersAsync(filter, 1, 50, ct);
    }
}
