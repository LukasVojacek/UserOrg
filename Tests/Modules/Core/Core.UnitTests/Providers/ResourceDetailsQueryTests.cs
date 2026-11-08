using Core.DataAccess.Contexts;
using Core.DataAccess.Events;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Shared;

using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using DomainProviderName = Core.Domain.Provider.ProviderName;
using ProjectAggregate = Core.Domain.Project.Project;
using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Providers;

/// <summary>
/// Tests for <see cref="ResourceDetailsQuery"/> over the EF Core in-memory provider, covering the
/// read-scope tiers added in F0.2: admin (no scope) sees every resource including project-less ones;
/// the provider tier sees resources on its own locations (also project-less); the user tier stays
/// constrained to the caller's projects. Mirrors <see cref="ResourceAssignmentQueryTests"/>'s harness.
/// </summary>
public sealed class ResourceDetailsQueryTests : IAsyncDisposable
{
    private static readonly (string Name, string Value)[] NoSpecs = [];
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _serviceProvider;
    private int _providerIdsCallCount;
    private int _domainTypeKeySeed = 2000;

    public ResourceDetailsQueryTests()
    {
        var dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DomainEventChannel>();
        services.AddScoped<IRoleService, UserDatabaseAccessTests.TestRoleService>();
        services.Configure<WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped(typeof(IDataQuery<>), typeof(DataQuery<>));
        // A closed registration takes precedence over the open generic for this one type. It only counts, so it
        // stays invisible to every test that does not read _providerIdsCallCount.
        services.AddScoped<IDataQuery<AssignedLocation>>(sp => new ProviderIdsCallCountingDataQuery(
            new DataQuery<AssignedLocation>(sp.GetRequiredService<DatabaseContext>()),
            () => _providerIdsCallCount++));
        services.AddScoped<IResourceQuery, ResourceDetailsQuery>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_ReturnAllIncludingProjectLess_When_AdminScope()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        // Admin tier: ScopeShortNames null and ScopeProviderName null.
        var result = await QueryAsync(q => q.GetResourcesAsync(new ResourceFilter(null, null, null, null, null), 1, 50, ct));

        Assert.Equal(3, result.TotalItems);
        Assert.Equal(3, result.Items.Count);
        Assert.Contains(result.Items, r => r.Name == "orphan-res"); // project-less resource is still visible
    }

    [Fact]
    public async Task Should_ReturnProviderResourcesIncludingProjectLess_When_ProviderScope()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var result = await QueryAsync(q => q.GetResourcesAsync(
            new ResourceFilter(null, null, null, null, null, ScopeProviderName: DomainProviderName.From("acme")), 1, 50, ct));

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, r => r.Name == "gpu-res");
        Assert.Contains(result.Items, r => r.Name == "orphan-res");
        Assert.DoesNotContain(result.Items, r => r.Name == "cpu-res"); // other provider's resource excluded
    }

    [Fact]
    public async Task Should_ReturnProviderResources_When_ProviderScopeSet_AndProjectScopeEmpty()
    {
        // Provider tier with no project roles (a service key): its own provider's resources resolve without
        // any project link, so the empty project scope must not short-circuit the read to nothing.
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var result = await QueryAsync(q => q.GetResourcesAsync(
            new ResourceFilter([], null, null, null, null, ScopeProviderName: DomainProviderName.From("acme")), 1, 50, ct));

        Assert.Equal(["gpu-res", "orphan-res"], result.Items.Select(r => r.Name).Order());
        Assert.Equal(2, result.TotalItems);
    }

    [Fact]
    public async Task Should_UnionProviderAndProjectScope_When_BothSet()
    {
        // Provider tier for a human ProviderAdmin: own-provider resources PLUS the ones already reachable
        // through their projects. Substituting the provider tier for the project scope would drop "cpu-res".
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var result = await QueryAsync(q => q.GetResourcesAsync(
            new ResourceFilter(
                [ProjectShortName.Create("proj-b")], null, null, null, null,
                ScopeProviderName: DomainProviderName.From("acme")),
            1, 50, ct));

        Assert.Equal(["cpu-res", "gpu-res", "orphan-res"], result.Items.Select(r => r.Name).Order());
        Assert.Equal(3, result.TotalItems);
    }

    [Fact]
    public async Task Should_ScopeToCallerProjects_When_UserScope()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var result = await QueryAsync(q => q.GetResourcesAsync(
            new ResourceFilter([ProjectShortName.Create("proj-a")], null, null, null, null), 1, 50, ct));

        var only = Assert.Single(result.Items);
        Assert.Equal("gpu-res", only.Name); // proj-a uses gpu-res; orphan-res has no project, so it is hidden
        // TotalItems reflects the scoped set.
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task Should_ReturnEmpty_When_UserScopeEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var result = await QueryAsync(q => q.GetResourcesAsync(new ResourceFilter([], null, null, null, null), 1, 50, ct));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalItems);
    }

    [Fact]
    public async Task Should_IntersectProjectShortName_When_AdminFiltersByProject()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var result = await QueryAsync(q => q.GetResourcesAsync(
            new ResourceFilter(null, ProjectShortName.Create("proj-b"), null, null, null), 1, 50, ct));

        var only = Assert.Single(result.Items);
        Assert.Equal("cpu-res", only.Name);
    }

    [Fact]
    public async Task Should_PageWithStableOrder_When_MoreThanOnePage()
    {
        // Two pages over the deterministic order (name asc, Id tie-breaker) return disjoint rows.
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var pageOne = await QueryAsync(q => q.GetResourcesAsync(new ResourceFilter(null, null, null, null, null), 1, 2, ct));
        var pageTwo = await QueryAsync(q => q.GetResourcesAsync(new ResourceFilter(null, null, null, null, null), 2, 2, ct));

        Assert.Equal(3, pageOne.TotalItems);
        Assert.Equal(["cpu-res", "gpu-res"], pageOne.Items.Select(r => r.Name));
        Assert.Equal(["orphan-res"], pageTwo.Items.Select(r => r.Name));
    }

    [Fact]
    public async Task Should_PageWithoutOverlapOrGaps_When_ScopeSpansMultiplePages()
    {
        // Characterization of the paged contract: 5 in-scope resources, limit 2 → pages of 2, 2, 1.
        // Across the three pages every resource appears exactly once (no overlap, no gaps), in the
        // deterministic order (name asc, Id tie-breaker), and TotalItems is the scoped count on each page.
        var ct = TestContext.Current.CancellationToken;
        await SeedManyResourcesOnProviderAsync("acme", "loc", ["res-a", "res-b", "res-c", "res-d", "res-e"]);

        var filter = new ResourceFilter(null, null, null, null, null, ScopeProviderName: DomainProviderName.From("acme"));
        var pageOne = await QueryAsync(q => q.GetResourcesAsync(filter, 1, 2, ct));
        var pageTwo = await QueryAsync(q => q.GetResourcesAsync(filter, 2, 2, ct));
        var pageThree = await QueryAsync(q => q.GetResourcesAsync(filter, 3, 2, ct));

        Assert.Equal(5, pageOne.TotalItems);
        Assert.Equal(5, pageTwo.TotalItems);
        Assert.Equal(5, pageThree.TotalItems);

        Assert.Equal(["res-a", "res-b"], pageOne.Items.Select(r => r.Name));
        Assert.Equal(["res-c", "res-d"], pageTwo.Items.Select(r => r.Name));
        Assert.Equal(["res-e"], pageThree.Items.Select(r => r.Name));

        // No overlap, no gaps: the union of the page slices is exactly the scoped set.
        var seen = pageOne.Items.Concat(pageTwo.Items).Concat(pageThree.Items).Select(r => r.Name).ToList();
        Assert.Equal(5, seen.Count);
        Assert.Equal(5, seen.Distinct().Count());
    }

    [Fact]
    public async Task Should_PreserveNestedAssignmentAndSpecs_When_RowReturnedOnSecondPage()
    {
        // The assembled graph (assignments + specification values) must survive paging for a row that
        // lands on a non-first page. res-c sits on page 2 (limit 2, name-ordered) and carries a spec.
        var ct = TestContext.Current.CancellationToken;
        var targetId = ProjectResourceId.New();
        await SeedProviderWithSpecAsync(
            "acme",
            [
                ("res-a", ProjectResourceId.New(), NoSpecs),
                ("res-b", ProjectResourceId.New(), NoSpecs),
                ("res-c", targetId, [("CORES", "128")]),
            ]);

        var filter = new ResourceFilter(null, null, null, null, null, ScopeProviderName: DomainProviderName.From("acme"));
        var pageTwo = await QueryAsync(q => q.GetResourcesAsync(filter, 2, 2, ct));

        Assert.Equal(3, pageTwo.TotalItems);
        var resC = Assert.Single(pageTwo.Items);
        Assert.Equal("res-c", resC.Name);

        var assignment = Assert.Single(resC.Assignments);
        // The allocation is addressed by its slug (ADR-0024); end-to-end check that al.Slug projects through
        // the two-pass assembly. The resource linkage is proved by the assignment being nested under res-c.
        Assert.Equal("asg-res-c", assignment.Slug);
        Assert.Equal("res-c-loc", assignment.LocationName);
        var spec = Assert.Single(assignment.Specifications);
        Assert.Equal("CORES", spec.Name);
        Assert.Equal("128", spec.Value);
    }

    [Fact]
    public async Task Should_ExcludeResource_When_ProviderAssignmentSoftDeleted()
    {
        // Provider tier scopes via active assignments only (ResourceIdsForProviderAsync: !al.IsDeleted).
        var ct = TestContext.Current.CancellationToken;
        var res = ProjectResourceId.New();
        await SeedResourceAsync(res, "gpu-res");
        await SeedProviderWithAssignmentAsync("acme", "gpu", res, deleteAssignment: true);

        var result = await QueryAsync(q => q.GetResourcesAsync(
            new ResourceFilter(null, null, null, null, null, ScopeProviderName: DomainProviderName.From("acme")), 1, 50, ct));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Should_ExcludeResource_When_ProjectLinkSoftDeleted()
    {
        // User tier scopes via active project links only (ResourceIdsForProjectsAsync: !ap.IsDeleted).
        var ct = TestContext.Current.CancellationToken;
        var res = ProjectResourceId.New();
        await SeedResourceAsync(res, "gpu-res");
        await SeedProviderWithAssignmentAsync("acme", "gpu", res, deleteAssignment: false);
        var proj = await SeedProjectAsync("proj-a");
        await SeedResourceToProjectAsync(res, proj, deleted: true);

        var result = await QueryAsync(q => q.GetResourcesAsync(
            new ResourceFilter([ProjectShortName.Create("proj-a")], null, null, null, null), 1, 50, ct));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Should_ReturnOnlyResourcesAssignedToThatType_When_FilteredByLocationType()
    {
        var ct = TestContext.Current.CancellationToken;
        var hpcResource = ProjectResourceId.New();
        var cloudResource = ProjectResourceId.New();
        await SeedResourceAsync(hpcResource, "gpu-res");
        await SeedResourceAsync(cloudResource, "vm-res");
        await SeedProviderWithAssignmentAsync("acme", "gpu", hpcResource, deleteAssignment: false, ELocationType.HPC);
        await SeedProviderWithAssignmentAsync("other", "vm", cloudResource, deleteAssignment: false, ELocationType.CLOUD);

        var all = await QueryAsync(q => q.GetResourcesAsync(new ResourceFilter(null, null, null, null, null), 1, 50, ct));
        var hpc = await QueryAsync(q => q.GetResourcesAsync(new ResourceFilter(null, null, ELocationType.HPC, null, null), 1, 50, ct));

        Assert.Equal(2, all.TotalItems);
        var only = Assert.Single(hpc.Items);
        Assert.Equal("gpu-res", only.Name);
        Assert.Equal(ELocationType.HPC, Assert.Single(only.Assignments).LocationType);
        Assert.Equal(1, hpc.TotalItems);
    }

    [Fact]
    public async Task Should_ReturnEmptyPage_When_FilteredByUnspecifiedLocationType()
    {
        var ct = TestContext.Current.CancellationToken;
        var res = ProjectResourceId.New();
        await SeedResourceAsync(res, "gpu-res");
        await SeedProviderWithAssignmentAsync("acme", "gpu", res, deleteAssignment: false);

        var all = await QueryAsync(q => q.GetResourcesAsync(new ResourceFilter(null, null, null, null, null), 1, 50, ct));
        var result = await QueryAsync(q => q.GetResourcesAsync(new ResourceFilter(null, null, ELocationType.Unspecified, null, null), 1, 50, ct));

        Assert.Single(all.Items);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalItems);
    }

    [Fact]
    public async Task Should_ExcludeSoftDeletedAssignment_FromAssignmentsList()
    {
        // Stage B excludes soft-deleted assignments (AssignedLocation has no global filter): a resource
        // with one active and one deleted assignment lists only the active one.
        var ct = TestContext.Current.CancellationToken;
        var res = ProjectResourceId.New();
        await SeedResourceAsync(res, "gpu-res");
        await SeedProviderWithActiveAndDeletedAssignmentAsync("acme", res);

        var result = await QueryAsync(q => q.GetResourcesAsync(new ResourceFilter(null, null, null, null, null), 1, 50, ct));

        var only = Assert.Single(result.Items);
        var assignment = Assert.Single(only.Assignments);
        Assert.Equal("gpu", assignment.LocationName);
    }

    [Fact]
    public async Task Should_ReturnResource_When_FoundByNameInScope()
    {
        // Natural-key read (ADR-0022): GetResourceByNameAsync resolves the row by name within the caller's
        // scope. proj-a is in scope and uses gpu-res.
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var result = await ResourceAsync(q => q.GetResourceByNameAsync(
            ResourceName.Create("gpu-res"), [ProjectShortName.Create("proj-a")], ct));

        Assert.NotNull(result);
        Assert.Equal("gpu-res", result!.Name);
    }

    [Fact]
    public async Task Should_ReturnNull_When_NameMissingOrOutOfScope()
    {
        // 404-hiding at the query layer: a name that exists but is outside scope (cpu-res, on proj-b) and a
        // name that does not exist at all both resolve to null, so the endpoint emits the identical 404.
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var outOfScope = await ResourceAsync(q => q.GetResourceByNameAsync(
            ResourceName.Create("cpu-res"), [ProjectShortName.Create("proj-a")], ct));
        var missing = await ResourceAsync(q => q.GetResourceByNameAsync(
            ResourceName.Create("nope"), [ProjectShortName.Create("proj-a")], ct));

        Assert.Null(outOfScope);
        Assert.Null(missing);
    }

    [Fact]
    public async Task Should_ReturnResource_When_FoundByProviderAndNameInScope()
    {
        // Full natural-key read (ADR-0022): resolve by (providerName, resourceName) within the caller's scope.
        // gpu-res is on provider acme and proj-a is in scope.
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var result = await ResourceAsync(q => q.GetResourceByProviderAndNameAsync(
            DomainProviderName.From("acme"), ResourceName.Create("gpu-res"), [ProjectShortName.Create("proj-a")], ct));

        Assert.NotNull(result);
        Assert.Equal("gpu-res", result!.Name);
    }

    [Fact]
    public async Task Should_ReturnNull_When_NameExistsButUnderDifferentProvider()
    {
        // The name alone is not unique across providers, so the provider qualifies it: gpu-res is in scope
        // (proj-a) but lives on provider acme, so addressing it under provider "other" resolves to null —
        // the endpoint then emits the identical 404 as a genuinely-missing name.
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var wrongProvider = await ResourceAsync(q => q.GetResourceByProviderAndNameAsync(
            DomainProviderName.From("other"), ResourceName.Create("gpu-res"), [ProjectShortName.Create("proj-a")], ct));

        Assert.Null(wrongProvider);
    }

    // ── Name-taken pre-check (F4 Phase B; ADR-0026 per-provider uniqueness) ──────

    [Fact]
    public async Task Should_ReportNameNotTaken_When_NameFreeUnderProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAndResourceAsync("acme", ProviderId.New(), "existing-res");

        var taken = await NameTakenAsync(q => q.IsResourceNameTakenAsync(
            DomainProviderName.From("acme"), ResourceName.Create("new-res"), ct));

        Assert.False(taken);
    }

    [Fact]
    public async Task Should_ReportNameTaken_When_ActiveResourceExistsUnderProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAndResourceAsync("acme", ProviderId.New(), "existing-res");

        var taken = await NameTakenAsync(q => q.IsResourceNameTakenAsync(
            DomainProviderName.From("acme"), ResourceName.Create("existing-res"), ct));

        Assert.True(taken);
    }

    [Fact]
    public async Task Should_ReportNameNotTaken_When_ProviderUnknown()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAndResourceAsync("acme", ProviderId.New(), "existing-res");

        var taken = await NameTakenAsync(q => q.IsResourceNameTakenAsync(
            DomainProviderName.From("ghost"), ResourceName.Create("x"), ct));

        Assert.False(taken);
    }

    [Fact]
    public async Task Should_TreatSameNameUnderDifferentProvidersAsDistinct_When_CheckingNameTaken()
    {
        // The regression that proves ADR-0026: a name taken under one provider is still free under another.
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAndResourceAsync("acme", ProviderId.New(), "shared-name");
        await SeedProviderAndResourceAsync("beta", ProviderId.New(), resourceName: null);

        var acme = await NameTakenAsync(q => q.IsResourceNameTakenAsync(
            DomainProviderName.From("acme"), ResourceName.Create("shared-name"), ct));
        var beta = await NameTakenAsync(q => q.IsResourceNameTakenAsync(
            DomainProviderName.From("beta"), ResourceName.Create("shared-name"), ct));

        Assert.True(acme);
        Assert.False(beta);
    }

    // (The (provider,name) → surrogate write resolver moved to IResourceAssignmentQuery.GetResourceSurrogateAsync
    // under ADR-0041 L2.4/L2.6; its per-provider / soft-delete semantics are covered by ResourceAssignmentQueryTests.)

    // ── Route provider == caller's own provider ──────────────────────────────────

    [Fact]
    public async Task Should_ReturnOwnProviderResources_When_RouteProviderMatchesCallerProviderScope()
    {
        // providers/{providerName}/resources always sets FilterProviderName from the route, and a ProviderAdmin
        // browsing their own provider (with proj-b, which reaches "other"-owned cpu-res, in scope) carries that
        // same name as ScopeProviderName — the read must stay narrowed to the caller's own-provider set, never
        // widen to the whole tier because the two collapsed into one name. If narrowing were lost, cpu-res would
        // leak in via the proj-b half of the tier.
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        var result = await QueryAsync(q => q.GetResourcesAsync(
            new ResourceFilter(
                [ProjectShortName.Create("proj-b")], null, null, null, null,
                ScopeProviderName: DomainProviderName.From("acme"), FilterProviderName: DomainProviderName.From("acme")),
            1, 50, ct));

        Assert.Equal(["gpu-res", "orphan-res"], result.Items.Select(r => r.Name).Order());
        Assert.Equal(2, result.TotalItems);
    }

    [Fact]
    public async Task Should_CallResourceIdsForProviderOnce_When_RouteProviderMatchesCallerProviderScope()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedGraphAsync();

        await QueryAsync(q => q.GetResourcesAsync(
            new ResourceFilter(
                [], null, null, null, null,
                ScopeProviderName: DomainProviderName.From("acme"), FilterProviderName: DomainProviderName.From("acme")),
            1, 50, ct));

        Assert.Equal(1, _providerIdsCallCount);
    }

    private async Task<bool> NameTakenAsync(Func<IResourceQuery, Task<bool>> act)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IResourceQuery>();
        return await act(query);
    }

    private async Task<ResourceSummary?> ResourceAsync(Func<IResourceQuery, Task<ResourceSummary?>> act)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IResourceQuery>();
        return await act(query);
    }

    // Re-points an assignment (built against a same-provider placeholder to satisfy invariant #5) at the real
    // pre-seeded resource by loading it and setting the _resource navigation, then detaching the placeholder.
    // This keeps the required FK pointing at a tracked principal (EF rejects a severed required navigation).
    private static async Task RepointAsync(DatabaseContext context, AssignedLocation assignment, ProjectResourceId resourceId, CancellationToken ct)
    {
        var placeholder = context.Entry(assignment).Reference("_resource").CurrentValue;
        var real = await context.Set<ProjectResource>()
            .IgnoreQueryFilters()
            .FirstAsync(pr => EF.Property<ProjectResourceId>(pr, "Id") == resourceId, ct);
        context.Entry(assignment).Reference("_resource").CurrentValue = real;
        if (placeholder is not null)
        {
            context.Entry(placeholder).State = EntityState.Detached;
        }
    }

    // R1 (gpu-res): on provider acme, linked to proj-a.
    // R2 (cpu-res): on provider other, linked to proj-b.
    // R3 (orphan-res): on provider acme, NOT linked to any project (project-less).
    private async Task SeedGraphAsync()
    {
        var gpuRes = ProjectResourceId.New();
        var cpuRes = ProjectResourceId.New();
        var orphanRes = ProjectResourceId.New();

        await SeedResourceAsync(gpuRes, "gpu-res");
        await SeedResourceAsync(cpuRes, "cpu-res");
        await SeedResourceAsync(orphanRes, "orphan-res");

        await SeedProviderWithResourcesAsync("acme", "gpu", [gpuRes, orphanRes]);
        await SeedProviderWithResourcesAsync("other", "cpu", [cpuRes]);

        var projA = await SeedProjectAsync("proj-a");
        var projB = await SeedProjectAsync("proj-b");
        await SeedResourceToProjectAsync(gpuRes, projA);
        await SeedResourceToProjectAsync(cpuRes, projB);
    }

    private async Task<QueryPage<ResourceSummary>> QueryAsync(Func<IResourceQuery, Task<QueryPage<ResourceSummary>>> act)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IResourceQuery>();
        return await act(query);
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

    private async Task SeedResourceAsync(ProjectResourceId id, string resourceName)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var owner = ProviderAggregate.Create(DomainProviderName.From($"owner-{Guid.NewGuid():N}"), countryId: 1);
        context.Add(owner);
        var resource = ProjectResource.Create(ResourceName.Create(resourceName), owner, UserId.From(Guid.NewGuid()), Start, End, operationMode: EOperationMode.Unspecified);
        context.Add(resource);
        context.Entry(resource).Property("Id").CurrentValue = id;
        await SaveAsync(context, ct);
    }

    private async Task SeedProviderWithResourcesAsync(string providerName, string locationName, ProjectResourceId[] resourceIds)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var location = LocationName.From(locationName);
        var provider = ProviderAggregate.Create(DomainProviderName.From(providerName), countryId: 1);
        Assert.True(provider.AddLocation(location, $"https://{locationName}", ELocationType.HPC, isShared: true).IsSuccess);
        var assignments = new List<AssignedLocation>();
        var i = 0;
        foreach (var resourceId in resourceIds)
        {
            // AssignResource enforces invariant #5; assign a same-provider placeholder, then re-point the row
            // at the pre-seeded resource id via the shadow FK (ADR-0041 L2.1).
            var placeholder = ProjectResource.Create(ResourceName.Create($"placeholder-{locationName}-{i++}"), provider, UserId.From(Guid.NewGuid()), Start, End);
            var assignment = provider.AssignResource(
                location, placeholder, allocationUnitTypeId: 1, allocationUnitCount: null,
                description: null, aggregationName: null, specifications: NoSpecs,
                resourceHasNonSharedLocation: false);
            Assert.True(assignment.IsSuccess);
            assignments.Add(assignment.Value);
        }

        context.Add(provider);
        i = 0;
        foreach (var resourceId in resourceIds)
        {
            await RepointAsync(context, assignments[i], resourceId, ct);
            i++;
        }

        await SaveAsync(context, ct);
    }

    // Seeds a provider with an explicit surrogate id and (optionally) one owned resource carrying that
    // ProviderId — the create-context query resolves the provider by name → id, then checks (ProviderId, Name).
    private async Task SeedProviderAndResourceAsync(string providerName, ProviderId providerId, string? resourceName)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(DomainProviderName.From(providerName), countryId: 1);
        context.Add(provider);
        context.Entry(provider).Property("Id").CurrentValue = providerId;

        if (resourceName is not null)
        {
            var resource = ProjectResource.Create(ResourceName.Create(resourceName), provider, UserId.From(Guid.NewGuid()), Start, End);
            context.Add(resource);
        }

        await SaveAsync(context, ct);
    }

    // Seeds N named resources and assigns each to one location of a single provider — the provider tier
    // then scopes to exactly these resources, giving a deterministic multi-page set.
    private async Task SeedManyResourcesOnProviderAsync(string providerName, string locationName, string[] resourceNames)
    {
        var ids = new ProjectResourceId[resourceNames.Length];
        for (var i = 0; i < resourceNames.Length; i++)
        {
            ids[i] = ProjectResourceId.New();
            await SeedResourceAsync(ids[i], resourceNames[i]);
        }

        await SeedProviderWithResourcesAsync(providerName, locationName, ids);
    }

    // Seeds named resources, each assigned to its own location ("<name>-loc") on one provider, with the
    // given specification values — exercises the full assembled graph (assignment + specs) under paging.
    private async Task SeedProviderWithSpecAsync(
        string providerName,
        (string Name, ProjectResourceId Id, (string Name, string Value)[] Specs)[] resources)
    {
        foreach (var (name, id, _) in resources)
        {
            await SeedResourceAsync(id, name);
        }

        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(DomainProviderName.From(providerName), countryId: 1);
        var assignmentsById = new List<(ProjectResourceId Id, AssignedLocation Assignment)>();
        foreach (var (name, id, specs) in resources)
        {
            var location = LocationName.From($"{name}-loc");
            Assert.True(provider.AddLocation(location, $"https://{name}", ELocationType.HPC, isShared: true).IsSuccess);
            // AssignResource enforces invariant #5; assign a same-provider placeholder, then re-point the row
            // at the pre-seeded resource id via the shadow FK (ADR-0041 L2.1).
            var placeholder = ProjectResource.Create(ResourceName.Create($"placeholder-{name}"), provider, UserId.From(Guid.NewGuid()), Start, End);
            var assignment = provider.AssignResource(
                location, placeholder, allocationUnitTypeId: 1, allocationUnitCount: null,
                description: null, aggregationName: null, specifications: specs,
                resourceHasNonSharedLocation: false, slug: $"asg-{name}");
            Assert.True(assignment.IsSuccess);
            assignmentsById.Add((id, assignment.Value));
        }

        context.Add(provider);
        foreach (var (id, assignment) in assignmentsById)
        {
            await RepointAsync(context, assignment, id, ct);
        }

        await SaveAsync(context, ct);
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

    // One provider, one location, one assignment of the resource — optionally soft-deleted.
    private async Task SeedProviderWithAssignmentAsync(string providerName, string locationName, ProjectResourceId resourceId, bool deleteAssignment, ELocationType locationType = ELocationType.HPC)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var location = LocationName.From(locationName);
        var provider = ProviderAggregate.Create(DomainProviderName.From(providerName), countryId: 1);
        Assert.True(provider.AddLocation(location, $"https://{locationName}", locationType, isShared: true).IsSuccess);
        // AssignResource enforces invariant #5; assign a same-provider placeholder, then re-point the row at the
        // pre-seeded resource id via the shadow FK (ADR-0041 L2.1).
        var placeholder = ProjectResource.Create(ResourceName.Create($"placeholder-{providerName}-{locationName}"), provider, UserId.From(Guid.NewGuid()), Start, End);
        var assignment = provider.AssignResource(
            location, placeholder, allocationUnitTypeId: 1, allocationUnitCount: null,
            description: null, aggregationName: null, specifications: NoSpecs,
            resourceHasNonSharedLocation: false, slug: $"asg-{providerName}-{locationName}");
        Assert.True(assignment.IsSuccess);

        if (deleteAssignment)
        {
            Assert.True(provider.RemoveAssignment(location, assignment.Value.Slug!).IsSuccess);
        }

        context.Add(provider);
        await RepointAsync(context, assignment.Value, resourceId, ct);
        await SaveAsync(context, ct);
    }

    // One provider, the resource assigned to two locations — one active ("gpu"), one soft-deleted ("cpu").
    private async Task SeedProviderWithActiveAndDeletedAssignmentAsync(string providerName, ProjectResourceId resourceId)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(DomainProviderName.From(providerName), countryId: 1);
        var gpu = LocationName.From("gpu");
        var cpu = LocationName.From("cpu");
        Assert.True(provider.AddLocation(gpu, "https://gpu", ELocationType.HPC, isShared: true).IsSuccess);
        Assert.True(provider.AddLocation(cpu, "https://cpu", ELocationType.HPC, isShared: true).IsSuccess);
        // AssignResource enforces invariant #5; assign same-provider placeholders, then re-point each row at the
        // pre-seeded resource id via the shadow FK (ADR-0041 L2.1).
        var gpuPlaceholder = ProjectResource.Create(ResourceName.Create($"placeholder-{providerName}-gpu"), provider, UserId.From(Guid.NewGuid()), Start, End);
        var cpuPlaceholder = ProjectResource.Create(ResourceName.Create($"placeholder-{providerName}-cpu"), provider, UserId.From(Guid.NewGuid()), Start, End);
        var active = provider.AssignResource(gpu, gpuPlaceholder, 1, null, null, null, NoSpecs, false, slug: $"asg-{providerName}-gpu");
        Assert.True(active.IsSuccess);
        var deleted = provider.AssignResource(cpu, cpuPlaceholder, 1, null, null, null, NoSpecs, false, slug: $"asg-{providerName}-cpu");
        Assert.True(deleted.IsSuccess);
        Assert.True(provider.RemoveAssignment(cpu, deleted.Value.Slug!).IsSuccess);

        context.Add(provider);
        await RepointAsync(context, active.Value, resourceId, ct);
        await RepointAsync(context, deleted.Value, resourceId, ct);
        await SaveAsync(context, ct);
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();

    // Forwards every IDataQuery<AssignedLocation> call to inner unchanged; only the "GetResourceIdsForProvider"
    // tag (ResourceIdsForProviderAsync) is counted, which is the one observation point Moq cannot give us here
    // because every member of the interface is an open generic method.
    private sealed class ProviderIdsCallCountingDataQuery(IDataQuery<AssignedLocation> inner, Action onProviderIdsCall)
        : IDataQuery<AssignedLocation>
    {
        public Task<TResult?> FirstOrDefaultAsync<TResult>(Func<IQueryable<AssignedLocation>, IQueryable<TResult>> query, string tag, CancellationToken ct)
            => inner.FirstOrDefaultAsync(query, tag, ct);

        public Task<List<TResult>> ToListAsync<TResult>(Func<IQueryable<AssignedLocation>, IQueryable<TResult>> query, string tag, CancellationToken ct)
            => inner.ToListAsync(query, tag, ct);

        public Task<QueryPage<TResult>> ToPagedListAsync<TResult>(Func<IQueryable<AssignedLocation>, IQueryable<TResult>> query, int page, int limit, string tag, CancellationToken ct)
            => inner.ToPagedListAsync(query, page, limit, tag, ct);

        public Task<TResult?> FirstOrDefaultAsync<T2, TResult>(Func<IQueryable<AssignedLocation>, IQueryable<T2>, IQueryable<TResult>> query, string tag, CancellationToken ct)
            where T2 : class, IEntity
            => inner.FirstOrDefaultAsync(query, tag, ct);

        public Task<List<TResult>> ToListAsync<T2, TResult>(Func<IQueryable<AssignedLocation>, IQueryable<T2>, IQueryable<TResult>> query, string tag, CancellationToken ct)
            where T2 : class, IEntity
            => inner.ToListAsync(query, tag, ct);

        public Task<QueryPage<TResult>> ToPagedListAsync<T2, TResult>(Func<IQueryable<AssignedLocation>, IQueryable<T2>, IQueryable<TResult>> query, int page, int limit, string tag, CancellationToken ct)
            where T2 : class, IEntity
            => inner.ToPagedListAsync(query, page, limit, tag, ct);

        public Task<TResult?> FirstOrDefaultAsync<T2, T3, TResult>(Func<IQueryable<AssignedLocation>, IQueryable<T2>, IQueryable<T3>, IQueryable<TResult>> query, string tag, CancellationToken ct)
            where T2 : class, IEntity where T3 : class, IEntity
            => inner.FirstOrDefaultAsync(query, tag, ct);

        public Task<List<TResult>> ToListAsync<T2, T3, TResult>(Func<IQueryable<AssignedLocation>, IQueryable<T2>, IQueryable<T3>, IQueryable<TResult>> query, string tag, CancellationToken ct)
            where T2 : class, IEntity where T3 : class, IEntity
        {
            if (tag == "GetResourceIdsForProvider")
                onProviderIdsCall();

            return inner.ToListAsync(query, tag, ct);
        }

        public Task<QueryPage<TResult>> ToPagedListAsync<T2, T3, TResult>(Func<IQueryable<AssignedLocation>, IQueryable<T2>, IQueryable<T3>, IQueryable<TResult>> query, int page, int limit, string tag, CancellationToken ct)
            where T2 : class, IEntity where T3 : class, IEntity
            => inner.ToPagedListAsync(query, page, limit, tag, ct);

        public Task<List<TResult>> ToListAsync<T2, T3, T4, TResult>(Func<IQueryable<AssignedLocation>, IQueryable<T2>, IQueryable<T3>, IQueryable<T4>, IQueryable<TResult>> query, string tag, CancellationToken ct)
            where T2 : class, IEntity where T3 : class, IEntity where T4 : class, IEntity
            => inner.ToListAsync(query, tag, ct);
    }
}
