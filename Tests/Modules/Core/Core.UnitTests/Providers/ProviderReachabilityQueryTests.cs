using Core.DataAccess;
using Core.DataAccess.Contexts;
using Core.DataAccess.Events;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Shared;
using Core.UnitTests.Shared;

using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProjectAggregate = Core.Domain.Project.Project;
using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Providers;

/// <summary>
/// Tests for the <see cref="ProviderFilter.ScopeShortNames"/> user read-scope tier (F0.3): the user tier
/// returns public providers PLUS any private provider reachable from the caller's projects via the inverse
/// chain (project → assigned resource → assigned location → location → provider), keeping the provider list
/// consistent with the locations a user can already see. Mirrors <see cref="Locations.LocationDetailsQueryTests"/>'s
/// chain-seeding harness; the chain is the same minus the final location → provider hop.
/// </summary>
public sealed class ProviderReachabilityQueryTests : IAsyncDisposable
{
    private static readonly (string Name, string Value)[] NoSpecs = [];
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _serviceProvider;
    private int _domainTypeKeySeed = 5000;

    public ProviderReachabilityQueryTests()
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

    [Fact]
    public async Task Should_ReturnReachablePrivateProvider_When_UserScopedAndReachable()
    {
        // A private provider the caller reaches through an in-scope project stays visible (the F0.3 gap:
        // otherwise the caller sees the location but not its provider).
        var ct = TestContext.Current.CancellationToken;
        await SeedReachableChainAsync("priv-co", "gpu", "proj-in", isPrivate: true);

        var result = await QueryAsync(q => q.GetProvidersAsync(
            new ProviderFilter(null, null, OnlyPublic: true, ScopeShortNames: [ProjectShortName.Create("proj-in")]), 1, 50, ct));

        var only = Assert.Single(result.Items);
        Assert.Equal("priv-co", only.Name.Value);
        Assert.True(only.IsPrivate);
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task Should_HidePrivateProvider_When_NotReachableFromScope()
    {
        // The same private provider is reachable only from proj-other; a caller scoped to proj-in must not see it.
        var ct = TestContext.Current.CancellationToken;
        await SeedReachableChainAsync("priv-co", "gpu", "proj-other", isPrivate: true);

        var result = await QueryAsync(q => q.GetProvidersAsync(
            new ProviderFilter(null, null, OnlyPublic: true, ScopeShortNames: [ProjectShortName.Create("proj-in")]), 1, 50, ct));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalItems);
    }

    [Fact]
    public async Task Should_ReturnPublicProvider_RegardlessOfReachability_When_UserScoped()
    {
        // Public providers are always visible to the user tier; a reachable private one joins them (union).
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("pub-co", isPrivate: false);
        await SeedReachableChainAsync("priv-co", "gpu", "proj-in", isPrivate: true);

        var result = await QueryAsync(q => q.GetProvidersAsync(
            new ProviderFilter(null, null, OnlyPublic: true, ScopeShortNames: [ProjectShortName.Create("proj-in")]), 1, 50, ct));

        Assert.Equal(2, result.TotalItems);
        Assert.Equal(["priv-co", "pub-co"], result.Items.Select(p => p.Name.Value));
    }

    [Fact]
    public async Task Should_HideReachablePrivate_When_NeverPublicAndScopeEmpty()
    {
        // Empty scope short-circuits to "no reachable private providers": only public ones survive.
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("pub-co", isPrivate: false);
        await SeedReachableChainAsync("priv-co", "gpu", "proj-in", isPrivate: true);

        var result = await QueryAsync(q => q.GetProvidersAsync(
            new ProviderFilter(null, null, OnlyPublic: true, ScopeShortNames: []), 1, 50, ct));

        var only = Assert.Single(result.Items);
        Assert.Equal("pub-co", only.Name.Value);
    }

    [Fact]
    public async Task Should_KeepReachablePrivate_When_OwnProviderNameAlsoSet()
    {
        // The provider tier of the list read adds its own provider to the user tier's union; the reachable
        // private provider must survive alongside it, not be replaced by it.
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("acme", isPrivate: true);                                  // own, unreachable
        await SeedReachableChainAsync("priv-co", "gpu", "proj-in", isPrivate: true);       // reachable
        await SeedProviderAsync("hidden-co", isPrivate: true);                             // neither

        var result = await QueryAsync(q => q.GetProvidersAsync(
            new ProviderFilter(
                null, null, OnlyPublic: true,
                ScopeShortNames: [ProjectShortName.Create("proj-in")],
                OwnProviderName: ProviderName.From("acme")),
            1, 50, ct));

        Assert.Equal(["acme", "priv-co"], result.Items.Select(p => p.Name.Value));
        Assert.Equal(2, result.TotalItems);
    }

    [Fact]
    public async Task Should_ExcludeReachablePrivate_When_AssignmentSoftDeleted()
    {
        // Reachability honours the soft-delete guards (!al.IsDeleted): a retired assignment does not re-expose
        // the private provider.
        var ct = TestContext.Current.CancellationToken;
        await SeedReachableChainAsync("priv-co", "gpu", "proj-in", isPrivate: true, deleteAssignment: true);

        var result = await QueryAsync(q => q.GetProvidersAsync(
            new ProviderFilter(null, null, OnlyPublic: true, ScopeShortNames: [ProjectShortName.Create("proj-in")]), 1, 50, ct));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Should_ResolvePrivateDetail_When_ReachableAndUserScoped()
    {
        // By-id (GetProviderDetailsAsync) user tier: a reachable private provider resolves for the owning user.
        var ct = TestContext.Current.CancellationToken;
        await SeedReachableChainAsync("priv-co", "gpu", "proj-in", isPrivate: true);

        var result = await DetailAsync(q => q.GetProviderDetailsAsync(
            ProviderName.From("priv-co"), [ProjectShortName.Create("proj-in")], ct));

        Assert.NotNull(result);
        Assert.Equal("priv-co", result.Name.Value);
        Assert.True(result.IsPrivate);
    }

    [Fact]
    public async Task Should_HidePrivateDetail_When_NotReachableAndUserScoped()
    {
        // Out-of-scope private provider resolves to null — the endpoint turns this into the identical 404.
        var ct = TestContext.Current.CancellationToken;
        await SeedReachableChainAsync("priv-co", "gpu", "proj-other", isPrivate: true);

        var result = await DetailAsync(q => q.GetProviderDetailsAsync(
            ProviderName.From("priv-co"), [ProjectShortName.Create("proj-in")], ct));

        Assert.Null(result);
    }

    [Fact]
    public async Task Should_ResolvePrivateDetail_When_ScopeIsNull()
    {
        // Admin / provider identity (scope == null) resolves any active provider, private included.
        var ct = TestContext.Current.CancellationToken;
        await SeedReachableChainAsync("priv-co", "gpu", "proj-in", isPrivate: true);

        var result = await DetailAsync(q => q.GetProviderDetailsAsync(ProviderName.From("priv-co"), null, ct));

        Assert.NotNull(result);
        Assert.Equal("priv-co", result.Name.Value);
    }

    [Fact]
    public async Task Should_ResolvePublicDetail_When_UserScoped()
    {
        // A public provider resolves for the user tier regardless of reachability.
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("pub-co", isPrivate: false);

        var result = await DetailAsync(q => q.GetProviderDetailsAsync(
            ProviderName.From("pub-co"), [ProjectShortName.Create("proj-in")], ct));

        Assert.NotNull(result);
        Assert.Equal("pub-co", result.Name.Value);
    }

    private async Task<QueryPage<ProviderSummary>> QueryAsync(Func<IProviderQuery, Task<QueryPage<ProviderSummary>>> act)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IProviderQuery>();
        return await act(query);
    }

    private async Task<ProviderDetail?> DetailAsync(Func<IProviderQuery, Task<ProviderDetail?>> act)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IProviderQuery>();
        return await act(query);
    }

    private async Task SeedReachableChainAsync(string providerName, string locationName, string projectShortName, bool isPrivate, bool deleteAssignment = false)
    {
        var resourceId = ProjectResourceId.New();
        await SeedResourceAsync(resourceId);
        await SeedProviderWithAssignmentAsync(providerName, locationName, resourceId, isPrivate, deleteAssignment);
        var projectId = await SeedProjectAsync(projectShortName);
        await SeedResourceToProjectAsync(resourceId, projectId);
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

    private async Task SeedProviderAsync(string name, bool isPrivate)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        context.Add(ProviderAggregate.Create(ProviderName.From(name), countryId: 1, isPrivate: isPrivate));
        await context.SaveChangesAsync(ct);
    }

    private async Task SeedResourceAsync(ProjectResourceId id)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        // The owning provider is irrelevant here (these tests query the provider listing, not the resource's
        // owner); deactivate it so it stays out of the active-provider set the reachability tests assert on.
        var owner = ProviderAggregate.Create(ProviderName.From($"owner-{Guid.NewGuid():N}"), countryId: 1);
        Assert.True(owner.Deactivate().IsSuccess);
        context.Add(owner);
        var resource = ProjectResource.Create(ResourceName.Create("res"), owner, UserId.From(Guid.NewGuid()), Start, End, operationMode: EOperationMode.Unspecified);
        context.Add(resource);
        context.Entry(resource).Property("Id").CurrentValue = id;
        await SaveAsync(context, ct);
    }

    private async Task SeedProviderWithAssignmentAsync(string providerName, string locationName, ProjectResourceId resourceId, bool isPrivate, bool deleteAssignment)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var location = LocationName.From(locationName);
        var provider = ProviderAggregate.Create(ProviderName.From(providerName), countryId: 1, isPrivate: isPrivate);
        Assert.True(provider.AddLocation(location, $"https://{locationName}", ELocationType.HPC, isShared: true).IsSuccess);

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
        // Re-point the assignment at the pre-seeded resource by loading it and setting the _resource navigation
        // (EF rejects a severed required navigation), then detach the placeholder.
        var real = await context.Set<ProjectResource>()
            .IgnoreQueryFilters()
            .FirstAsync(pr => EF.Property<ProjectResourceId>(pr, "Id") == resourceId, ct);
        context.Entry(assignment.Value).Reference("_resource").CurrentValue = real;
        context.Entry(placeholder).State = EntityState.Detached;
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

        // FromName yields an unresolved ProjectDomainType with the sentinel key -1; give each a distinct key
        // so two seeded projects do not collide (the FK is required, so detaching is not an option).
        foreach (var domainTypeEntry in context.ChangeTracker.Entries<ProjectDomainType>().ToList())
        {
            var key = domainTypeEntry.Metadata.FindPrimaryKey()!.Properties[0].Name;
            domainTypeEntry.Property(key).CurrentValue = new ProjectDomainTypeId(++_domainTypeKeySeed);
        }

        await SaveAsync(context, ct);
        return projectId;
    }

    private async Task SeedResourceToProjectAsync(ProjectResourceId resourceId, ProjectId projectId)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var link = AssignedProjectResourceToProject.Create();
        context.Add(link);
        context.Entry(link).Property("ProjectId").CurrentValue = projectId;
        context.Entry(link).Property("ProjectResourceId").CurrentValue = resourceId;
        await SaveAsync(context, ct);
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();
}

/// <summary>
/// Real-SQL translation guard for the reachability query (F0.3, ADR-0018 read-scope tier). The inverse-chain
/// join is the one genuinely new piece of SQL in F0.3; EF in-memory cannot prove it translates, and prior
/// value-object constructs in this codebase passed in-memory while throwing on SQL Server. Set
/// <see cref="IntegrationDb.EnvVar"/> to run; otherwise it skips.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProviderReachabilityRealSqlTests : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public ProviderReachabilityRealSqlTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:UserOrgDBContext"] = IntegrationDb.ConnectionStringOrPlaceholder
            })
            .Build();

        var services = IntegrationDb.NewServices();
        services.AddCoreDataAccess(configuration);
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_TranslateAndExecute_When_ScopeGiven()
    {
        IntegrationDb.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IProviderQuery>();

        // A scope that matches no project resolves to an empty reachable set; the reachability join and the
        // public-OR-reachable predicate must still translate to SQL and execute (the failure mode is a runtime
        // translation throw, not a wrong count). Public providers in the test DB may surface — assert only that
        // it runs and pages cleanly.
        var result = await query.GetProvidersAsync(
            new ProviderFilter(null, null, OnlyPublic: true, ScopeShortNames: [ProjectShortName.Create("no-such-project-f03")]), 1, 50, ct);

        Assert.NotNull(result);
        Assert.True(result.TotalItems >= 0);
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();
}
