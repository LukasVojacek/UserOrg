using Core.DataAccess;
using Core.DataAccess.Contexts;
using Core.DataAccess.Events;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;

using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProjectAggregate = Core.Domain.Project.Project;
using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Providers;

/// <summary>
/// Tests for <see cref="ProjectResourceAssignmentQuery"/> (F5 Phase C) — the project-level non-shared
/// invariant behind the resource↔project assign endpoint (G6). Over the EF in-memory provider, which compiles
/// the shadow-FK EXISTS chain (ProjectId, ProjectResourceId, LocationId) the same way SQL Server does.
/// </summary>
public sealed class ProjectResourceAssignmentQueryTests : IAsyncDisposable
{
    private static readonly (string Name, string Value)[] NoSpecs = [];
    private static readonly LocationName Gpu = LocationName.From("gpu");
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _serviceProvider;
    private int _domainTypeKeySeed = 7000;

    public ProjectResourceAssignmentQueryTests()
    {
        var dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DomainEventChannel>();
        services.AddScoped<IRoleService, UserDatabaseAccessTests.TestRoleService>();
        services.Configure<WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped(typeof(IDataQuery<>), typeof(DataQuery<>));
        services.AddScoped<IProjectResourceAssignmentQuery, ProjectResourceAssignmentQuery>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_ReturnFalse_When_ProjectHasNoResources()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProjectAsync("proj-empty");

        Assert.False(await QueryAsync(q => q.ProjectHasNonSharedResourceAsync(ProjectShortName.Create("proj-empty"), ct)).ConfigureAwait(false));
    }

    [Fact]
    public async Task Should_ReturnTrue_When_ProjectHasResourceOnNonSharedLocation()
    {
        var ct = TestContext.Current.CancellationToken;
        var resourceId = ProjectResourceId.New();
        var projectId = await SeedProjectAsync("proj-ns");
        await SeedProviderWithAssignmentAsync(resourceId, locationShared: false, softDelete: false);
        await SeedResourceToProjectAsync(resourceId, projectId, deleted: false);

        Assert.True(await QueryAsync(q => q.ProjectHasNonSharedResourceAsync(ProjectShortName.Create("proj-ns"), ct)).ConfigureAwait(false));
    }

    [Fact]
    public async Task Should_ReturnFalse_When_ProjectResourceOnSharedLocation()
    {
        var ct = TestContext.Current.CancellationToken;
        var resourceId = ProjectResourceId.New();
        var projectId = await SeedProjectAsync("proj-shared");
        await SeedProviderWithAssignmentAsync(resourceId, locationShared: true, softDelete: false);
        await SeedResourceToProjectAsync(resourceId, projectId, deleted: false);

        Assert.False(await QueryAsync(q => q.ProjectHasNonSharedResourceAsync(ProjectShortName.Create("proj-shared"), ct)).ConfigureAwait(false));
    }

    [Fact]
    public async Task Should_ReturnFalse_When_NonSharedAllocationSoftDeleted()
    {
        // A soft-deleted allocation no longer reserves its non-shared location.
        var ct = TestContext.Current.CancellationToken;
        var resourceId = ProjectResourceId.New();
        var projectId = await SeedProjectAsync("proj-del-alloc");
        await SeedProviderWithAssignmentAsync(resourceId, locationShared: false, softDelete: true);
        await SeedResourceToProjectAsync(resourceId, projectId, deleted: false);

        Assert.False(await QueryAsync(q => q.ProjectHasNonSharedResourceAsync(ProjectShortName.Create("proj-del-alloc"), ct)).ConfigureAwait(false));
    }

    [Fact]
    public async Task Should_ReturnFalse_When_ProjectLinkSoftDeleted()
    {
        // The resource is on a non-shared location, but its link to the project is soft-deleted → not held.
        var ct = TestContext.Current.CancellationToken;
        var resourceId = ProjectResourceId.New();
        var projectId = await SeedProjectAsync("proj-del-link");
        await SeedProviderWithAssignmentAsync(resourceId, locationShared: false, softDelete: false);
        await SeedResourceToProjectAsync(resourceId, projectId, deleted: true);

        Assert.False(await QueryAsync(q => q.ProjectHasNonSharedResourceAsync(ProjectShortName.Create("proj-del-link"), ct)).ConfigureAwait(false));
    }

    private async Task<TResult> QueryAsync<TResult>(Func<IProjectResourceAssignmentQuery, Task<TResult>> act)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IProjectResourceAssignmentQuery>();
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

    private async Task SeedProviderWithAssignmentAsync(ProjectResourceId resourceId, bool locationShared, bool softDelete)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(ProviderName.From($"prov-{resourceId.Value:N}"), countryId: 1);
        Assert.True(provider.AddLocation(Gpu, "https://gpu", ELocationType.HPC, isShared: locationShared).IsSuccess);

        // The resource is owned by THIS provider (invariant #5 passes), so assign it directly and pin its
        // surrogate to the requested resourceId — the assignment's shadow FK derives from the navigation.
        var resource = ProjectResource.Create(ResourceName.Create($"res-{resourceId.Value:N}"), provider, UserId.From(Guid.NewGuid()), Start, End);
        var assignment = provider.AssignResource(
            Gpu, resource, allocationUnitTypeId: 1, allocationUnitCount: null,
            description: null, aggregationName: null, specifications: NoSpecs,
            resourceHasNonSharedLocation: false, slug: $"asg-gpu-{resourceId.Value:N}");
        Assert.True(assignment.IsSuccess);

        if (softDelete)
        {
            Assert.True(provider.RemoveAssignment(Gpu, assignment.Value.Slug!).IsSuccess);
        }

        context.Add(provider);
        context.Add(resource);
        context.Entry(resource).Property("Id").CurrentValue = resourceId;
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

        // FromName yields an unresolved ProjectDomainType with the sentinel key -1; give each a distinct key so
        // seeding several projects does not collide on the FK (the query under test never joins the domain type).
        foreach (var domainTypeEntry in context.ChangeTracker.Entries<ProjectDomainType>().ToList())
        {
            var key = domainTypeEntry.Metadata.FindPrimaryKey()!.Properties[0].Name;
            domainTypeEntry.Property(key).CurrentValue = new ProjectDomainTypeId(++_domainTypeKeySeed);
        }

        await SaveAsync(context, ct);
        return projectId;
    }

    private async Task SeedResourceToProjectAsync(ProjectResourceId resourceId, ProjectId projectId, bool deleted)
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

/// <summary>
/// Real-SQL translation guard for the project non-shared query (F5 Phase C). The shadow-FK EXISTS chain
/// (project → link → allocation → location) is the new SQL in F5; EF in-memory cannot prove it translates, and
/// prior value-object constructs passed in-memory while throwing on SQL Server. Set <see cref="IntegrationDb.EnvVar"/>
/// to run; otherwise it skips.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProjectResourceAssignmentRealSqlTests : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public ProjectResourceAssignmentRealSqlTests()
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
    public async Task Should_TranslateAndExecute_When_Called()
    {
        IntegrationDb.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IProjectResourceAssignmentQuery>();

        // The failure mode is a runtime translation throw, not a wrong result; assert only that the EXISTS chain
        // translates to SQL and executes for an arbitrary (possibly absent) project.
        var result = await query.ProjectHasNonSharedResourceAsync(ProjectShortName.Create("no-such-project-f5"), ct);

        Assert.False(result);
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();
}
