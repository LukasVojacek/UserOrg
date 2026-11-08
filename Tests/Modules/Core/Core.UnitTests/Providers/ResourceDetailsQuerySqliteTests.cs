using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Shared;

using Microsoft.Extensions.DependencyInjection;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace Core.UnitTests.Providers;

/// <summary>
/// Characterization tests for <see cref="ResourceDetailsQuery"/> on SQLite: the read-scope tier of
/// <c>ResolveScopeAsync</c> (caller projects, own provider, their union, admin), its intersection with the
/// project and provider filters, the location-type and validity-date filters and paging of
/// <see cref="ResourceDetailsQuery.GetResourcesAsync"/>, and the single-resource reads that go through
/// <c>LoadAsync</c>. The existing <c>ResourceDetailsQueryTests</c> run on EF InMemory, which does not translate
/// the query to SQL.
/// </summary>
public sealed class ResourceDetailsQuerySqliteTests : SqliteQueryHarness
{
    private static readonly DateTimeOffset OldStart = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OldEnd = new(2025, 6, 30, 0, 0, 0, TimeSpan.Zero);

    private static readonly ProjectShortName ProjA = new("proj-a");
    private static readonly ProjectShortName ProjB = new("proj-b");
    private static readonly ProviderName Acme = ProviderName.From("acme");
    private static readonly ProviderName Beta = ProviderName.From("beta");

    private SqliteCoreSeed Seed => new(ServiceProvider);

    protected override void AddServices(IServiceCollection services) =>
        services.AddScoped<IResourceQuery, ResourceDetailsQuery>();

    [Fact]
    public async Task The_admin_tier_lists_every_resource_with_its_assignments_and_specifications()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var result = await ListAsync(Filter(), ct);

        Assert.Equal(["r-alpha", "r-beta", "r-gamma", "r-legacy", "r-old"], Names(result));
        Assert.Equal(5, result.TotalItems);

        var alpha = result.Items[0];
        Assert.Equal(
            ("r-alpha", "acme|r-alpha", "r-alpha description", "pi@example.org", SqliteCoreSeed.Start, SqliteCoreSeed.End, EOperationMode.Unspecified, (string?)null, EProjectResourceState.Draft),
            (alpha.Name, alpha.ProviderResourceName, alpha.Description, alpha.PrimaryInvestigatorEmail, alpha.StartDate, alpha.EndDate, alpha.OperationMode, alpha.LicenseUrl, alpha.EProjectResourceState));
        Assert.Equal(
            [
                new ResourceAssignmentSummary("asg-alpha-cloud", ELocationType.CLOUD, true, "a-cloud", 10, (EAllocationUnitType)1, null, EOperationMode.Unspecified, []),
                new ResourceAssignmentSummary("asg-alpha-hpc", ELocationType.HPC, true, "a-hpc", 10, (EAllocationUnitType)1, null, EOperationMode.Unspecified, []),
            ],
            alpha.Assignments.OrderBy(a => a.LocationName).Select(a => a with { Specifications = [] }));
        Assert.Equal(
            [new SpecificationSummary("cores", "64")],
            alpha.Assignments.Single(a => a.LocationName == "a-hpc").Specifications);

        var legacy = result.Items[3];
        Assert.Null(legacy.ProviderResourceName);
        Assert.Null(legacy.PrimaryInvestigatorEmail);
        Assert.Empty(legacy.Assignments);
    }

    [Fact]
    public async Task The_user_tier_keeps_resources_linked_to_the_callers_projects_through_live_links()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var projA = await ListAsync(Filter(scope: [ProjA]), ct);
        var projB = await ListAsync(Filter(scope: [ProjB]), ct);
        var none = await ListAsync(Filter(scope: []), ct);

        Assert.Equal(["r-alpha", "r-legacy"], Names(projA));
        Assert.Equal(["r-beta"], Names(projB));
        Assert.Empty(none.Items);
        Assert.Equal(0, none.TotalItems);
    }

    [Fact]
    public async Task The_provider_tier_keeps_resources_with_a_live_assignment_at_its_locations_and_unions_with_the_user_tier()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var own = await ListAsync(Filter(scopeProvider: Acme), ct);
        var union = await ListAsync(Filter(scope: [ProjB], scopeProvider: Acme), ct);
        var unknown = await ListAsync(Filter(scope: [], scopeProvider: ProviderName.From("ghost")), ct);

        Assert.Equal(["r-alpha", "r-gamma"], Names(own));
        Assert.Equal(["r-alpha", "r-beta", "r-gamma"], Names(union));
        Assert.Equal(3, union.TotalItems);
        Assert.Empty(unknown.Items);
    }

    [Fact]
    public async Task The_project_filter_intersects_with_the_tier()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var admin = await ListAsync(Filter(project: ProjA), ct);
        var disjoint = await ListAsync(Filter(scope: [ProjB], project: ProjA), ct);
        var overlapping = await ListAsync(Filter(scope: [ProjA, ProjB], project: ProjA), ct);

        Assert.Equal(["r-alpha", "r-legacy"], Names(admin));
        Assert.Empty(disjoint.Items);
        Assert.Equal(0, disjoint.TotalItems);
        Assert.Equal(["r-alpha", "r-legacy"], Names(overlapping));
    }

    [Fact]
    public async Task The_provider_path_filter_intersects_with_the_tier_whether_or_not_it_repeats_the_own_provider()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var admin = await ListAsync(Filter(filterProvider: Acme), ct);
        var sameAsOwn = await ListAsync(Filter(scopeProvider: Acme, filterProvider: Acme), ct);
        var otherThanOwn = await ListAsync(Filter(scopeProvider: Beta, filterProvider: Acme), ct);
        var withProject = await ListAsync(Filter(project: ProjA, filterProvider: Acme), ct);

        Assert.Equal(["r-alpha", "r-gamma"], Names(admin));
        Assert.Equal(["r-alpha", "r-gamma"], Names(sameAsOwn));
        Assert.Empty(otherThanOwn.Items);
        Assert.Equal(["r-alpha"], Names(withProject));
    }

    [Fact]
    public async Task The_location_type_filter_keeps_a_resource_with_a_live_assignment_of_that_type_and_all_its_assignments()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var cloud = await ListAsync(Filter(locationType: ELocationType.CLOUD), ct);
        var scopedCloud = await ListAsync(Filter(scope: [ProjA], locationType: ELocationType.CLOUD), ct);
        var scopedMiss = await ListAsync(Filter(scope: [ProjB], locationType: ELocationType.CLOUD), ct);
        var quantum = await ListAsync(Filter(locationType: ELocationType.Quantum), ct);

        Assert.Equal(["r-alpha", "r-gamma"], Names(cloud));
        Assert.Equal(2, cloud.TotalItems);
        Assert.Equal(["a-cloud", "a-hpc"], cloud.Items[0].Assignments.Select(a => a.LocationName).Order());
        Assert.Equal(["r-alpha"], Names(scopedCloud));
        Assert.Empty(scopedMiss.Items);
        Assert.Equal(0, scopedMiss.TotalItems);
        Assert.Empty(quantum.Items);
    }

    [Fact]
    public async Task The_validity_date_filters_bound_the_start_from_below_and_the_end_from_above()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var current = await ListAsync(Filter(startDate: SqliteCoreSeed.Start), ct);
        var old = await ListAsync(Filter(endDate: OldEnd), ct);
        var both = await ListAsync(Filter(startDate: OldStart, endDate: OldEnd), ct);

        Assert.Equal(["r-alpha", "r-beta", "r-gamma", "r-legacy"], Names(current));
        Assert.Equal(["r-old"], Names(old));
        Assert.Equal(["r-old"], Names(both));
    }

    [Fact]
    public async Task Paging_slices_the_ordered_set_and_a_page_past_the_end_keeps_the_total()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var page3 = await ListAsync(Filter(), ct, page: 3, limit: 2);
        var page4 = await ListAsync(Filter(), ct, page: 4, limit: 2);

        Assert.Equal(["r-old"], Names(page3));
        Assert.Empty(page4.Items);
        Assert.Equal(5, page4.TotalItems);
    }

    [Fact]
    public async Task GetResourceAsync_resolves_a_resource_by_id_only_inside_the_callers_scope()
    {
        var ct = TestContext.Current.CancellationToken;
        var alpha = await SeedGraphAsync();

        var inScope = await SingleAsync(q => q.GetResourceAsync(alpha.Value, [ProjA], ct));
        var outOfScope = await SingleAsync(q => q.GetResourceAsync(alpha.Value, [ProjB], ct));
        var emptyScope = await SingleAsync(q => q.GetResourceAsync(alpha.Value, [], ct));
        var unknown = await SingleAsync(q => q.GetResourceAsync(Guid.NewGuid(), [ProjA], ct));

        Assert.NotNull(inScope);
        Assert.Equal("acme|r-alpha", inScope.ProviderResourceName);
        Assert.Equal(["a-cloud", "a-hpc"], inScope.Assignments.Select(a => a.LocationName).Order());
        Assert.Null(outOfScope);
        Assert.Null(emptyScope);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task A_soft_deleted_resource_is_listed_and_resolvable_because_the_provider_name_subquery_ignores_query_filters()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        var hpc = await Seed.LocationAsync(acme, "a-hpc", ELocationType.HPC);
        var project = await Seed.ProjectAsync("proj-a");
        var deleted = await Seed.ResourceAsync(acme, "r-deleted", deleted: true);
        await Seed.AssignAsync(deleted, hpc);
        await Seed.LinkAsync(project, deleted);

        var listed = await ListAsync(Filter(scope: [ProjA]), ct);
        var byId = await SingleAsync(q => q.GetResourceAsync(deleted.Value, [ProjA], ct));
        var byName = await SingleAsync(q => q.GetResourceByProviderAndNameAsync(Acme, ResourceName.Create("r-deleted"), null, ct));

        Assert.Equal(["r-deleted"], Names(listed));
        Assert.NotNull(byId);
        Assert.NotNull(byName);
    }

    [Fact]
    public async Task GetResourceByNameAsync_resolves_a_resource_by_name_only_inside_the_callers_scope()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var inScope = await SingleAsync(q => q.GetResourceByNameAsync(ResourceName.Create("r-beta"), [ProjB], ct));
        var outOfScope = await SingleAsync(q => q.GetResourceByNameAsync(ResourceName.Create("r-beta"), [ProjA], ct));
        var unknown = await SingleAsync(q => q.GetResourceByNameAsync(ResourceName.Create("r-none"), [ProjB], ct));

        Assert.NotNull(inScope);
        Assert.Equal("beta|r-beta", inScope.ProviderResourceName);
        Assert.Null(outOfScope);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task GetResourceByProviderAndNameAsync_resolves_the_name_only_under_a_provider_it_is_assigned_at()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var admin = await SingleAsync(q => q.GetResourceByProviderAndNameAsync(Acme, ResourceName.Create("r-gamma"), null, ct));
        var wrongProvider = await SingleAsync(q => q.GetResourceByProviderAndNameAsync(Beta, ResourceName.Create("r-gamma"), null, ct));
        var scoped = await SingleAsync(q => q.GetResourceByProviderAndNameAsync(Acme, ResourceName.Create("r-alpha"), [ProjA], ct));
        var scopedMiss = await SingleAsync(q => q.GetResourceByProviderAndNameAsync(Acme, ResourceName.Create("r-gamma"), [ProjA], ct));

        Assert.NotNull(admin);
        Assert.Equal("acme|r-gamma", admin.ProviderResourceName);
        Assert.Null(wrongProvider);
        Assert.NotNull(scoped);
        Assert.Equal("r-alpha", scoped.Name);
        Assert.Null(scopedMiss);
    }

    private async Task<ProjectResourceId> SeedGraphAsync()
    {
        var acme = await Seed.ProviderAsync("acme");
        var beta = await Seed.ProviderAsync("beta");
        var aHpc = await Seed.LocationAsync(acme, "a-hpc", ELocationType.HPC);
        var aCloud = await Seed.LocationAsync(acme, "a-cloud", ELocationType.CLOUD);
        var bHpc = await Seed.LocationAsync(beta, "b-hpc", ELocationType.HPC);
        var projA = await Seed.ProjectAsync("proj-a");
        var projB = await Seed.ProjectAsync("proj-b");

        var alpha = await Seed.ResourceAsync(acme, "r-alpha", principalInvestigatorEmail: "pi@example.org");
        await Seed.AssignAsync(alpha, aHpc, slug: "asg-alpha-hpc", values: [("cores", "64")]);
        await Seed.AssignAsync(alpha, aCloud, slug: "asg-alpha-cloud");
        await Seed.LinkAsync(projA, alpha);

        var rBeta = await Seed.ResourceAsync(beta, "r-beta");
        await Seed.AssignAsync(rBeta, bHpc);
        await Seed.LinkAsync(projB, rBeta);

        var gamma = await Seed.ResourceAsync(acme, "r-gamma");
        await Seed.AssignAsync(gamma, aCloud);

        var legacy = await Seed.ResourceAsync(null, "r-legacy");
        await Seed.LinkAsync(projA, legacy);

        var old = await Seed.ResourceAsync(acme, "r-old", OldStart, OldEnd);
        await Seed.AssignAsync(old, aHpc, deleted: true);
        await Seed.LinkAsync(projB, old, deleted: true);

        return alpha;
    }

    private static ResourceFilter Filter(
        IReadOnlyList<ProjectShortName>? scope = null,
        ProjectShortName? project = null,
        ELocationType? locationType = null,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        ProviderName? scopeProvider = null,
        ProviderName? filterProvider = null) =>
        new(scope, project, locationType, startDate, endDate, scopeProvider, filterProvider);

    private static IEnumerable<string> Names(QueryPage<ResourceSummary> page) => page.Items.Select(r => r.Name);

    private async Task<QueryPage<ResourceSummary>> ListAsync(ResourceFilter filter, CancellationToken ct, int page = 1, int limit = 50)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IResourceQuery>().GetResourcesAsync(filter, page, limit, ct);
    }

    private async Task<ResourceSummary?> SingleAsync(Func<IResourceQuery, Task<ResourceSummary?>> act)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        return await act(scope.ServiceProvider.GetRequiredService<IResourceQuery>());
    }
}
