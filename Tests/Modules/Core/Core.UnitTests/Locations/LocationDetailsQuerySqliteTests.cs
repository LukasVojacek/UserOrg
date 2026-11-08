using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace Core.UnitTests.Locations;

/// <summary>
/// Characterization tests for the paged <see cref="LocationDetailsQuery.GetLocationsAsync(LocationFilter, int, int, CancellationToken)"/>
/// on SQLite: each optional filter, the user scope tier and its union with the caller's own provider, the
/// exclusion of deleted locations and deactivated providers, the projection, and paging. The existing
/// <c>LocationDetailsQueryTests</c> run on EF InMemory, which does not translate the query to SQL.
/// </summary>
public sealed class LocationDetailsQuerySqliteTests : SqliteQueryHarness
{
    private SqliteCoreSeed Seed => new(ServiceProvider);

    protected override void AddServices(IServiceCollection services) =>
        services.AddScoped<ILocationQuery, LocationDetailsQuery>();

    [Fact]
    public async Task Without_filters_lists_the_live_locations_of_active_providers_by_name_with_their_provider()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var result = await QueryAsync(new LocationFilter(null, null), ct);

        Assert.Equal(["a-cloud", "a-hpc", "b-hpc"], Names(result));
        Assert.Equal(3, result.TotalItems);
        Assert.Equal(
            new LocationSummary(
                "a-hpc", "a-hpc description", "https://a-hpc.example.org", false, true, ELocationType.HPC, "acme", EOperationMode.Unspecified, null),
            result.Items[1]);
        Assert.Equal(["acme", "acme", "beta"], result.Items.Select(l => l.ProviderName));
    }

    [Fact]
    public async Task A_provider_name_keeps_only_that_providers_locations_and_an_unknown_one_yields_an_empty_page()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var acme = await QueryAsync(new LocationFilter(ProviderName.From("acme"), null), ct);
        var ghost = await QueryAsync(new LocationFilter(ProviderName.From("ghost"), null), ct);

        Assert.Equal(["a-cloud", "a-hpc"], Names(acme));
        Assert.Equal(2, acme.TotalItems);
        Assert.Empty(ghost.Items);
        Assert.Equal(0, ghost.TotalItems);
    }

    [Fact]
    public async Task A_location_type_keeps_only_locations_of_that_type()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var hpc = await QueryAsync(new LocationFilter(null, ELocationType.HPC), ct);
        var acmeHpc = await QueryAsync(new LocationFilter(ProviderName.From("acme"), ELocationType.HPC), ct);

        Assert.Equal(["a-hpc", "b-hpc"], Names(hpc));
        Assert.Equal(["a-hpc"], Names(acmeHpc));
    }

    [Fact]
    public async Task A_scope_keeps_only_locations_reached_through_live_assignments_and_project_links()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var scoped = await QueryAsync(new LocationFilter(null, null, [new ProjectShortName("proj-a")]), ct);
        var empty = await QueryAsync(new LocationFilter(null, null, []), ct);
        var unreached = await QueryAsync(new LocationFilter(null, null, [new ProjectShortName("proj-none")]), ct);

        Assert.Equal(["b-hpc"], Names(scoped));
        Assert.Equal(1, scoped.TotalItems);
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalItems);
        Assert.Empty(unreached.Items);
    }

    [Fact]
    public async Task The_callers_own_provider_is_added_to_the_scope_rather_than_narrowing_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();
        var acme = ProviderName.From("acme");

        var ownOnly = await QueryAsync(new LocationFilter(null, null, [], acme), ct);
        var union = await QueryAsync(new LocationFilter(null, null, [new ProjectShortName("proj-a")], acme), ct);
        var unscoped = await QueryAsync(new LocationFilter(null, null, null, acme), ct);
        var unknownOwn = await QueryAsync(new LocationFilter(null, null, [], ProviderName.From("ghost")), ct);

        Assert.Equal(["a-cloud", "a-hpc"], Names(ownOnly));
        Assert.Equal(["a-cloud", "a-hpc", "b-hpc"], Names(union));
        Assert.Equal(3, union.TotalItems);
        Assert.Equal(["a-cloud", "a-hpc", "b-hpc"], Names(unscoped));
        Assert.Empty(unknownOwn.Items);
    }

    [Fact]
    public async Task Paging_slices_the_ordered_filtered_set_and_keeps_its_total()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();
        var filter = new LocationFilter(null, null);

        var page1 = await QueryAsync(filter, ct, page: 1, limit: 2);
        var page2 = await QueryAsync(filter, ct, page: 2, limit: 2);
        var beyond = await QueryAsync(filter, ct, page: 3, limit: 2);

        Assert.Equal(["a-cloud", "a-hpc"], Names(page1));
        Assert.Equal(["b-hpc"], Names(page2));
        Assert.Empty(beyond.Items);
        Assert.Equal(3, beyond.TotalItems);
    }

    private async Task SeedGraphAsync()
    {
        var acme = await Seed.ProviderAsync("acme");
        var beta = await Seed.ProviderAsync("beta");
        var retired = await Seed.ProviderAsync("retired", deactivated: true);

        var aHpc = await Seed.LocationAsync(acme, "a-hpc", ELocationType.HPC);
        var aCloud = await Seed.LocationAsync(acme, "a-cloud", ELocationType.CLOUD);
        await Seed.LocationAsync(acme, "a-gone", ELocationType.HPC, deleted: true);
        var bHpc = await Seed.LocationAsync(beta, "b-hpc", ELocationType.HPC);
        var rHpc = await Seed.LocationAsync(retired, "r-hpc", ELocationType.HPC);

        var project = await Seed.ProjectAsync("proj-a");

        var reached = await Seed.ResourceAsync(beta, "reached");
        await Seed.AssignAsync(reached, bHpc);
        await Seed.LinkAsync(project, reached);

        var retiredAssignment = await Seed.ResourceAsync(acme, "retired-assignment");
        await Seed.AssignAsync(retiredAssignment, aCloud, deleted: true);
        await Seed.LinkAsync(project, retiredAssignment);

        var retiredLink = await Seed.ResourceAsync(acme, "retired-link");
        await Seed.AssignAsync(retiredLink, aHpc);
        await Seed.LinkAsync(project, retiredLink, deleted: true);

        var onRetiredProvider = await Seed.ResourceAsync(retired, "on-retired-provider");
        await Seed.AssignAsync(onRetiredProvider, rHpc);
        await Seed.LinkAsync(project, onRetiredProvider);
    }

    private static IEnumerable<string> Names(QueryPage<LocationSummary> page) => page.Items.Select(l => l.Name);

    private async Task<QueryPage<LocationSummary>> QueryAsync(LocationFilter filter, CancellationToken ct, int page = 1, int limit = 50)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ILocationQuery>().GetLocationsAsync(filter, page, limit, ct);
    }
}
