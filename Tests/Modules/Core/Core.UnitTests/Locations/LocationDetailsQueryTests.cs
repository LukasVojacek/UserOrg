using Core.DataAccess.Contexts;
using Core.DataAccess.Events;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Shared;

using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ProjectAggregate = Core.Domain.Project.Project;
using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Locations;

/// <summary>
/// Tests for <see cref="LocationDetailsQuery"/> over the EF Core in-memory provider, covering the
/// <see cref="LocationFilter.ScopeShortNames"/> read-scope tier added in F0.2: the user tier returns
/// only locations reachable from the caller's projects (project → assigned resource → assigned
/// location), while the admin tier (null scope) returns all and the provider tier filters by name.
/// Mirrors <see cref="Providers.ResourceAssignmentQueryTests"/>'s harness.
/// </summary>
public sealed class LocationDetailsQueryTests : IAsyncDisposable
{
    private static readonly (string Name, string Value)[] NoSpecs = [];
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _serviceProvider;
    private int _domainTypeKeySeed = 1000;

    public LocationDetailsQueryTests()
    {
        var dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DomainEventChannel>();
        services.AddScoped<IRoleService, UserDatabaseAccessTests.TestRoleService>();
        services.Configure<WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped(typeof(IDataQuery<>), typeof(DataQuery<>));
        services.AddScoped<ILocationQuery, LocationDetailsQuery>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_ReturnAllLocations_When_ScopeIsNull()
    {
        // Admin tier: no scope filter.
        var ct = TestContext.Current.CancellationToken;
        await SeedChainAsync("acme", "gpu", "proj-a");
        await SeedChainAsync("other", "cpu", "proj-b");

        var result = await QueryAsync(q => q.GetLocationsAsync(new LocationFilter(null, null), 1, 50, ct));

        Assert.Equal(2, result.TotalItems);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task Should_ReportLocationTypeExists_When_TypeHasRow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            await EnsureLocationTypeAsync(scope.ServiceProvider.GetRequiredService<DatabaseContext>(), ELocationType.Quantum, ct);
        }

        await using var queryScope = _serviceProvider.CreateAsyncScope();
        var query = queryScope.ServiceProvider.GetRequiredService<ILocationQuery>();

        Assert.True(await query.LocationTypeExistsAsync(ELocationType.Quantum, ct));
    }

    [Fact]
    public async Task Should_ReportLocationTypeMissing_When_TypeHasNoRow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            await EnsureLocationTypeAsync(scope.ServiceProvider.GetRequiredService<DatabaseContext>(), ELocationType.HPC, ct);
        }

        await using var queryScope = _serviceProvider.CreateAsyncScope();
        var query = queryScope.ServiceProvider.GetRequiredService<ILocationQuery>();

        Assert.False(await query.LocationTypeExistsAsync(ELocationType.CLOUD, ct));
    }

    [Fact]
    public async Task Should_ReturnOnlyReachableLocations_When_ScopeGiven()
    {
        // User tier: only the location reachable from the in-scope project.
        var ct = TestContext.Current.CancellationToken;
        await SeedChainAsync("acme", "gpu", "proj-in");
        await SeedChainAsync("other", "cpu", "proj-out");

        var result = await QueryAsync(q => q.GetLocationsAsync(
            new LocationFilter(null, null, ScopeShortNames: [ProjectShortName.Create("proj-in")]), 1, 50, ct));

        var only = Assert.Single(result.Items);
        Assert.Equal("gpu", only.Name);
        // TotalItems reflects the scoped set, not the whole table.
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task Should_ReturnEmpty_When_ScopeIsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedChainAsync("acme", "gpu", "proj-in");

        var result = await QueryAsync(q => q.GetLocationsAsync(
            new LocationFilter(null, null, ScopeShortNames: []), 1, 50, ct));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalItems);
    }

    [Fact]
    public async Task Should_ReturnOwnProviderLocations_When_ScopeIsEmpty_AndOwnProviderSet()
    {
        // Provider tier with no project roles (a service key): its own provider's locations resolve without
        // any project link, so the empty scope must not short-circuit the read to nothing.
        var ct = TestContext.Current.CancellationToken;
        await SeedChainAsync("acme", "gpu", "proj-a");
        await SeedChainAsync("other", "cpu", "proj-b");

        var result = await QueryAsync(q => q.GetLocationsAsync(
            new LocationFilter(null, null, ScopeShortNames: [], OwnProviderName: ProviderName.Create("acme")), 1, 50, ct));

        var only = Assert.Single(result.Items);
        Assert.Equal("gpu", only.Name);
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task Should_ReturnOwnProviderAndReachableLocations_When_BothScopeAndOwnProviderSet()
    {
        // Provider tier for a human ProviderAdmin: own-provider locations PLUS the ones already reachable
        // through their projects. Pinning the read to the own provider alone would drop "cpu".
        var ct = TestContext.Current.CancellationToken;
        await SeedChainAsync("acme", "gpu", "proj-a");
        await SeedChainAsync("other", "cpu", "proj-in");
        await SeedChainAsync("third", "fpga", "proj-out");

        var result = await QueryAsync(q => q.GetLocationsAsync(
            new LocationFilter(
                null, null,
                ScopeShortNames: [ProjectShortName.Create("proj-in")],
                OwnProviderName: ProviderName.Create("acme")),
            1, 50, ct));

        Assert.Equal(["cpu", "gpu"], result.Items.Select(l => l.Name));
        Assert.Equal(2, result.TotalItems);
    }

    [Fact]
    public async Task Should_FilterByProviderName_When_ProviderNameGiven()
    {
        // Provider tier: only the named provider's locations.
        var ct = TestContext.Current.CancellationToken;
        await SeedChainAsync("acme", "gpu", "proj-a");
        await SeedChainAsync("other", "cpu", "proj-b");

        var result = await QueryAsync(q => q.GetLocationsAsync(new LocationFilter(ProviderName.Create("acme"), null), 1, 50, ct));

        var only = Assert.Single(result.Items);
        Assert.Equal("gpu", only.Name);
        Assert.Equal("acme", only.ProviderName);
    }

    [Fact]
    public async Task Should_PageWithStableOrder_When_MoreThanOnePage()
    {
        // Two pages over a deterministic order (name asc, Id tie-breaker) return disjoint rows, no gaps.
        var ct = TestContext.Current.CancellationToken;
        await SeedChainAsync("acme", "aaa", "proj-a");
        await SeedChainAsync("acme", "bbb", "proj-b");
        await SeedChainAsync("acme", "ccc", "proj-c");

        var pageOne = await QueryAsync(q => q.GetLocationsAsync(new LocationFilter(null, null), 1, 2, ct));
        var pageTwo = await QueryAsync(q => q.GetLocationsAsync(new LocationFilter(null, null), 2, 2, ct));

        Assert.Equal(3, pageOne.TotalItems);
        Assert.Equal(["aaa", "bbb"], pageOne.Items.Select(l => l.Name));
        Assert.Equal(["ccc"], pageTwo.Items.Select(l => l.Name));
    }

    [Fact]
    public async Task Should_PageWithoutOverlapOrGaps_When_ScopedAcrossThreePages()
    {
        // Walk every page of a scoped read and assert the slices are the contiguous partition of the
        // single-page baseline order — no row appears twice, none is missing. This is the invariant
        // root-first SQL paging must preserve, exercised through the scope tier (the staged child read
        // constrains the page). The order is whatever the one-page read yields; paging only slices it.
        var ct = TestContext.Current.CancellationToken;
        var names = new[] { "alpha", "bravo", "charlie", "delta", "echo" };
        var scope = new List<ProjectShortName>();
        foreach (var n in names)
        {
            await SeedChainAsync("acme", n, $"proj-{n}");
            scope.Add(ProjectShortName.Create($"proj-{n}"));
        }

        var filter = new LocationFilter(null, null, ScopeShortNames: scope);

        // Baseline: the full ordered set in a single page.
        var baseline = await QueryAsync(q => q.GetLocationsAsync(filter, 1, 50, ct));
        Assert.Equal(5, baseline.TotalItems);
        var expectedOrder = baseline.Items.Select(l => l.Name).ToList();
        Assert.Equal(5, expectedOrder.Count);

        var page1 = await QueryAsync(q => q.GetLocationsAsync(filter, 1, 2, ct));
        var page2 = await QueryAsync(q => q.GetLocationsAsync(filter, 2, 2, ct));
        var page3 = await QueryAsync(q => q.GetLocationsAsync(filter, 3, 2, ct));

        // TotalItems is the scoped total on every page.
        Assert.Equal(5, page1.TotalItems);
        Assert.Equal(5, page2.TotalItems);
        Assert.Equal(5, page3.TotalItems);

        // Page sizes follow Skip/Take.
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.Single(page3.Items);

        // Concatenating the pages reproduces the baseline order exactly: no overlap, no gaps.
        var paged = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(l => l.Name).ToList();
        Assert.Equal(expectedOrder, paged);
        Assert.Equal(5, paged.Distinct().Count());
    }

    [Fact]
    public async Task Should_AssembleNestedFields_When_RowLandsOnSecondPage()
    {
        // The staged child stitch (LocationType name/id, provider name, endpoint) must survive paging:
        // assert the full nested LocationSummary for a row that only appears on page 2.
        var ct = TestContext.Current.CancellationToken;
        await SeedChainAsync("acme", "aaa", "proj-a");
        await SeedChainAsync("acme", "bbb", "proj-b");
        await SeedChainAsync("acme", "zzz", "proj-z");

        var pageTwo = await QueryAsync(q => q.GetLocationsAsync(new LocationFilter(null, null), 2, 2, ct));

        var row = Assert.Single(pageTwo.Items);
        Assert.Equal("zzz", row.Name);
        Assert.Equal("acme", row.ProviderName);
        Assert.Equal(ELocationType.HPC, row.LocationType);
        Assert.Equal("https://zzz", row.Endpoint);
        Assert.True(row.IsShared);
    }

    [Fact]
    public async Task Should_ReturnOnlyLocationsOfThatType_When_FilteredByLocationType()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedChainAsync("acme", "gpu", "proj-a", locationType: ELocationType.HPC);
        await SeedChainAsync("acme", "vm", "proj-b", locationType: ELocationType.CLOUD);

        var result = await QueryAsync(q => q.GetLocationsAsync(new LocationFilter(null, ELocationType.HPC), 1, 50, ct));

        var only = Assert.Single(result.Items);
        Assert.Equal("gpu", only.Name);
        Assert.Equal(ELocationType.HPC, only.LocationType);
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task Should_ReturnEmptyPage_When_FilteredByUnspecifiedLocationType()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedChainAsync("acme", "gpu", "proj-a", locationType: ELocationType.HPC);

        var all = await QueryAsync(q => q.GetLocationsAsync(new LocationFilter(null, null), 1, 50, ct));
        var unspecified = await QueryAsync(q => q.GetLocationsAsync(new LocationFilter(null, ELocationType.Unspecified), 1, 50, ct));

        Assert.Single(all.Items);
        Assert.Empty(unspecified.Items);
        Assert.Equal(0, unspecified.TotalItems);
    }

    [Fact]
    public async Task Should_ExcludeLocation_When_ProjectLinkSoftDeleted()
    {
        // User-tier reachability ignores soft-deleted project links (LocationDetailsQuery: !ap.IsDeleted).
        var ct = TestContext.Current.CancellationToken;
        await SeedChainAsync("acme", "gpu", "proj-in", deleteLink: true);

        var result = await QueryAsync(q => q.GetLocationsAsync(
            new LocationFilter(null, null, ScopeShortNames: [ProjectShortName.Create("proj-in")]), 1, 50, ct));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Should_ExcludeLocation_When_AssignmentSoftDeleted()
    {
        // User-tier reachability ignores soft-deleted assignments (LocationDetailsQuery: !al.IsDeleted).
        var ct = TestContext.Current.CancellationToken;
        await SeedChainAsync("acme", "gpu", "proj-in", deleteAssignment: true);

        var result = await QueryAsync(q => q.GetLocationsAsync(
            new LocationFilter(null, null, ScopeShortNames: [ProjectShortName.Create("proj-in")]), 1, 50, ct));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Should_ExcludeLocation_When_ProviderDeactivated()
    {
        // A deactivated provider's locations are NOT soft-deleted (no cascade). Before the root EXISTS over
        // the active-provider set, such a location was counted+paged and then threw KeyNotFoundException on
        // the provider-name stitch (a 500). It is now excluded from count and page (ADR-0018
        // status-observability rule), matching the pre-refactor inner-join result set.
        var ct = TestContext.Current.CancellationToken;
        await SeedChainAsync("acme", "gpu", "proj-a");      // active provider
        await SeedChainAsync("ghost", "cpu", "proj-b");     // about to be deactivated
        await DeactivateProviderAsync("ghost");

        var result = await QueryAsync(q => q.GetLocationsAsync(new LocationFilter(null, null), 1, 50, ct));

        var only = Assert.Single(result.Items);             // no KeyNotFoundException; ghost excluded
        Assert.Equal("gpu", only.Name);
        Assert.Equal("acme", only.ProviderName);
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task Should_ReturnLocation_When_ProviderAndNameMatch_AndScopeNull()
    {
        // The natural-key by-id read (ADR-0022, ADR-0029) returns the row; scope == null is the admin / provider
        // tier (resolve any active location).
        var ct = TestContext.Current.CancellationToken;
        await SeedChainAsync("acme", "gpu", "proj-a");

        var row = await ScalarAsync(q => q.GetLocationByProviderAndNameAsync(ProviderName.From("acme"), LocationName.From("gpu"), null, ct));

        Assert.NotNull(row);
        Assert.Equal("gpu", row!.Name);
        Assert.Equal("acme", row.ProviderName);
    }

    [Fact]
    public async Task Should_CarryStoredMode_When_ListedAndReadByProviderAndName()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedChainAsync("acme", "gpu", "proj-a", mode: EOperationMode.Editable);

        var page = await QueryAsync(q => q.GetLocationsAsync(new LocationFilter(null, null), 1, 50, ct));
        var row = await ScalarAsync(q => q.GetLocationByProviderAndNameAsync(ProviderName.From("acme"), LocationName.From("gpu"), null, ct));

        Assert.Equal(EOperationMode.Editable, Assert.Single(page.Items).Mode);
        Assert.Equal(EOperationMode.Editable, row!.Mode);
    }

    [Fact]
    public async Task Should_ReturnNull_When_ProviderAndNameDoNotMatch()
    {
        // An unknown name (or wrong provider for an existing name) resolves to null — the endpoint turns this
        // into the identical 404 used for out-of-scope rows.
        var ct = TestContext.Current.CancellationToken;
        await SeedChainAsync("acme", "gpu", "proj-a");

        var unknownName = await ScalarAsync(q => q.GetLocationByProviderAndNameAsync(ProviderName.From("acme"), LocationName.From("nope"), null, ct));
        var wrongProvider = await ScalarAsync(q => q.GetLocationByProviderAndNameAsync(ProviderName.From("other"), LocationName.From("gpu"), null, ct));

        Assert.Null(unknownName);
        Assert.Null(wrongProvider);
    }

    [Fact]
    public async Task Should_ReturnLocation_When_ReachableAndUserScoped()
    {
        // User tier: a location reachable through an in-scope project resolves by-id.
        var ct = TestContext.Current.CancellationToken;
        await SeedChainAsync("acme", "gpu", "proj-in");

        var row = await ScalarAsync(q => q.GetLocationByProviderAndNameAsync(
            ProviderName.From("acme"), LocationName.From("gpu"), [ProjectShortName.Create("proj-in")], ct));

        Assert.NotNull(row);
        Assert.Equal("gpu", row!.Name);
    }

    [Fact]
    public async Task Should_ReturnNull_When_NotReachableAndUserScoped()
    {
        // User tier: a location reachable only from another project resolves to null (identical 404).
        var ct = TestContext.Current.CancellationToken;
        await SeedChainAsync("acme", "gpu", "proj-other");

        var row = await ScalarAsync(q => q.GetLocationByProviderAndNameAsync(
            ProviderName.From("acme"), LocationName.From("gpu"), [ProjectShortName.Create("proj-in")], ct));

        Assert.Null(row);
    }

    private async Task<T> ScalarAsync<T>(Func<ILocationQuery, Task<T>> act)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<ILocationQuery>();
        return await act(query);
    }

    private async Task<QueryPage<LocationSummary>> QueryAsync(Func<ILocationQuery, Task<QueryPage<LocationSummary>>> act)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<ILocationQuery>();
        return await act(query);
    }

    private async Task DeactivateProviderAsync(string providerName)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var name = ProviderName.From(providerName);
        var provider = await context.Set<ProviderAggregate>().SingleAsync(p => p.Name == name, ct);
        Assert.True(provider.Deactivate().IsSuccess);
        await SaveAsync(context, ct);
    }

    // Seeds the full chain a user-scope read traverses: project → assigned resource → assigned location.
    // Optionally soft-deletes the assignment (AssignedLocation) or the project link to prove exclusion.
    private async Task SeedChainAsync(string providerName, string locationName, string projectShortName, bool deleteAssignment = false, bool deleteLink = false, ELocationType locationType = ELocationType.HPC, EOperationMode mode = EOperationMode.Unspecified)
    {
        var resourceId = ProjectResourceId.New();
        await SeedResourceAsync(resourceId);
        await SeedProviderWithAssignmentAsync(providerName, locationName, resourceId, deleteAssignment, locationType, mode);
        var projectId = await SeedProjectAsync(projectShortName);
        await SeedResourceToProjectAsync(resourceId, projectId, deleteLink);
    }

    // The in-memory provider does not run SQL defaults, so the persistence-only shadow "ServiceName"
    // (HasDefaultValueSql('LEXIS') on Location/AssignedLocation) stays null and trips its required check.
    private static async Task SaveAsync(DatabaseContext context, CancellationToken ct)
    {
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

    private async Task SeedResourceAsync(ProjectResourceId id)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var owner = ProviderAggregate.Create(ProviderName.From($"owner-{Guid.NewGuid():N}"), countryId: 1);
        context.Add(owner);
        var resource = ProjectResource.Create(ResourceName.Create("res"), owner, UserId.From(Guid.NewGuid()), Start, End, operationMode: EOperationMode.Unspecified);
        context.Add(resource);
        context.Entry(resource).Property("Id").CurrentValue = id;
        await SaveAsync(context, ct);
    }

    private async Task SeedProviderWithAssignmentAsync(string providerName, string locationName, ProjectResourceId resourceId, bool deleteAssignment = false, ELocationType locationType = ELocationType.HPC, EOperationMode mode = EOperationMode.Unspecified)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        await EnsureLocationTypeAsync(context, locationType, ct);

        var location = LocationName.From(locationName);
        var provider = ProviderAggregate.Create(ProviderName.From(providerName), countryId: 1);
        Assert.True(provider.AddLocation(location, $"https://{locationName}", locationType, isShared: true).IsSuccess);

        // AssignResource enforces invariant #5 (resource owned by this provider, ADR-0041 L2.3), so assign a
        // same-provider placeholder, then re-point the row at the pre-seeded resource id via the shadow FK.
        var placeholder = ProjectResource.Create(ResourceName.Create($"placeholder-{locationName}"), provider, UserId.From(Guid.NewGuid()), Start, End);
        var assignment = provider.AssignResource(
            location, placeholder, allocationUnitTypeId: 1, allocationUnitCount: null,
            description: null, aggregationName: null, specifications: NoSpecs,
            resourceHasNonSharedLocation: false, slug: $"asg-loc-{resourceId.Value:N}");
        Assert.True(assignment.IsSuccess);

        if (deleteAssignment)
        {
            Assert.True(provider.RemoveAssignment(location, assignment.Value.Slug!).IsSuccess);
        }

        context.Add(provider);
        context.ChangeTracker.Entries<Location>().Single(e => e.Entity.Name == location).Property(l => l.Mode).CurrentValue = mode;
        // Re-point the assignment at the pre-seeded resource by loading it and setting the _resource navigation
        // (EF rejects a severed required navigation), then detach the placeholder.
        var real = await context.Set<ProjectResource>()
            .IgnoreQueryFilters()
            .FirstAsync(pr => EF.Property<ProjectResourceId>(pr, "Id") == resourceId, ct);
        context.Entry(assignment.Value).Reference("_resource").CurrentValue = real;
        context.Entry(placeholder).State = EntityState.Detached;
        await SaveAsync(context, ct);
    }

    private static async Task EnsureLocationTypeAsync(DatabaseContext context, ELocationType id, CancellationToken ct)
    {
        if (await context.Set<LocationType>().AnyAsync(t => t.Id == id, ct).ConfigureAwait(false))
            return;

        // LocationType is seeded reference data with no public factory; instantiate it directly.
        var entity = (LocationType)Activator.CreateInstance(typeof(LocationType), nonPublic: true)!;
        typeof(LocationType).GetProperty(nameof(LocationType.Id))!.SetValue(entity, id);
        typeof(LocationType).GetProperty(nameof(LocationType.Name))!.SetValue(entity, id.ToString());
        context.Add(entity);
        await context.SaveChangesAsync(ct);
    }

    private async Task<ProjectId> SeedProjectAsync(string shortName)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var projectId = ProjectId.From(Guid.NewGuid());
        var project = ProjectAggregate.Create(
            new ProjectShortName(shortName),
            ProjectName.Create(shortName),
            ProjectDomainType.FromName(DomainTypeName.Create($"domain-{shortName}")),
            UserId.New(),
            Start,
            End,
            ServiceName.Default);
        context.Add(project);
        context.Entry(project).Property("Id").CurrentValue = projectId;

        // FromName yields an unresolved ProjectDomainType with the sentinel key -1; give each a distinct
        // key so two seeded projects do not collide (the FK is required, so detaching is not an option).
        foreach (var domainTypeEntry in context.ChangeTracker.Entries<ProjectDomainType>().ToList())
        {
            var key = domainTypeEntry.Metadata.FindPrimaryKey()!.Properties[0].Name;
            domainTypeEntry.Property(key).CurrentValue = new ProjectDomainTypeId(++_domainTypeKeySeed);
        }

        await SaveAsync(context, ct);
        return projectId;
    }

    private async Task SeedResourceToProjectAsync(ProjectResourceId resourceId, ProjectId projectId, bool deleted = false)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var link = AssignedProjectResourceToProject.Create();
        if (deleted)
        {
            link.Delete();
        }

        context.Add(link);
        context.Entry(link).Property("ProjectId").CurrentValue = projectId;
        context.Entry(link).Property("ProjectResourceId").CurrentValue = resourceId;
        await SaveAsync(context, ct);
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();
}
