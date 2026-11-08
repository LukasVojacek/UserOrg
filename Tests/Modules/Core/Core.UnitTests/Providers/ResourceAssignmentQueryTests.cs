using Core.DataAccess.Contexts;
using Core.DataAccess.Events;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;

using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ProjectAggregate = Core.Domain.Project.Project;
using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Providers;

/// <summary>
/// Regression tests for <see cref="ResourceAssignmentQuery"/> — the write-validation reads behind the
/// resource-assignment endpoints (ADR-0016 / ADR-0041). Each read addresses the resource by its natural key
/// <c>(ProviderName, ResourceName)</c> and resolves the surrogate internally (L2.6). The EF Core in-memory
/// provider compiles the shadow-FK joins (<c>LocationId</c>, <c>ProjectResourceId</c>, <c>ProjectId</c>) the
/// same way SQL Server does, so the joins are exercised without a live database.
/// </summary>
public sealed class ResourceAssignmentQueryTests : IAsyncDisposable
{
    private static readonly (string Name, string Value)[] NoSpecs = [];
    private static readonly LocationName Gpu = LocationName.From("gpu");
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _serviceProvider;
    private int _domainTypeKeySeed = 1000;

    public ResourceAssignmentQueryTests()
    {
        var dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DomainEventChannel>();
        services.AddScoped<IRoleService, UserDatabaseAccessTests.TestRoleService>();
        services.Configure<WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped(typeof(IDataQuery<>), typeof(DataQuery<>));
        services.AddScoped<IResourceAssignmentQuery, ResourceAssignmentQuery>();

        _serviceProvider = services.BuildServiceProvider();
    }

    private static ProjectResourceKey Key(string provider, string name) =>
        new(ProviderName.From(provider), ResourceName.From(name));

    [Fact]
    public async Task Should_ReturnFalse_When_ResourceDoesNotExist()
    {
        var result = await QueryAsync(q => q.HasNonSharedLocationAsync(Key("acme", "missing"), TestContext.Current.CancellationToken));

        Assert.False(result);
    }

    [Fact]
    public async Task Should_ReturnFalse_When_ResourceExistsWithoutNonSharedLocation()
    {
        await SeedProviderAndResourceAsync("acme", ProviderId.New(), ProjectResourceId.New(), "res", EOperationMode.ReadOnly);

        var result = await QueryAsync(q => q.HasNonSharedLocationAsync(Key("acme", "res"), TestContext.Current.CancellationToken));

        Assert.False(result);
    }

    [Fact]
    public async Task Should_ReportNonSharedLocation_When_ActiveAssignmentOnNonSharedLocation()
    {
        var resourceId = ProjectResourceId.New();
        await SeedProviderWithAssignmentAsync("acme", resourceId, "res", locationShared: false, softDelete: false);

        var result = await QueryAsync(q => q.HasNonSharedLocationAsync(Key("acme", "res"), TestContext.Current.CancellationToken));

        Assert.True(result);
    }

    [Fact]
    public async Task Should_NotReportNonSharedLocation_When_AssignmentSoftDeleted()
    {
        // Legacy omits the IsDeleted filter on invariant #1; this query corrects it — a soft-deleted
        // allocation must not keep the non-shared location reserved.
        var resourceId = ProjectResourceId.New();
        await SeedProviderWithAssignmentAsync("acme", resourceId, "res", locationShared: false, softDelete: true);

        var result = await QueryAsync(q => q.HasNonSharedLocationAsync(Key("acme", "res"), TestContext.Current.CancellationToken));

        Assert.False(result);
    }

    [Fact]
    public async Task Should_NotReportNonSharedLocation_When_LocationIsShared()
    {
        var resourceId = ProjectResourceId.New();
        await SeedProviderWithAssignmentAsync("acme", resourceId, "res", locationShared: true, softDelete: false);

        var result = await QueryAsync(q => q.HasNonSharedLocationAsync(Key("acme", "res"), TestContext.Current.CancellationToken));

        Assert.False(result);
    }

    // ── GetResourceSurrogateAsync (per-provider key → storage Guid, ADR-0041 L2.6) ─

    [Fact]
    public async Task Should_ResolveSurrogate_When_ResourceMatchesUnderOwningProvider()
    {
        var resourceId = ProjectResourceId.New();
        await SeedProviderAndResourceAsync("acme", ProviderId.New(), resourceId, "OPEN-33-82", EOperationMode.ReadOnly);

        var result = await QueryAsync(q => q.GetResourceSurrogateAsync(Key("acme", "OPEN-33-82"), TestContext.Current.CancellationToken));

        Assert.Equal(resourceId.Value, result);
    }

    [Fact]
    public async Task Should_ReturnNull_When_ResourceNameUnknownUnderProvider()
    {
        await SeedProviderAndResourceAsync("acme", ProviderId.New(), ProjectResourceId.New(), "OPEN-33-82", EOperationMode.Unspecified);

        var result = await QueryAsync(q => q.GetResourceSurrogateAsync(Key("acme", "does-not-exist"), TestContext.Current.CancellationToken));

        Assert.Null(result);
    }

    [Fact]
    public async Task Should_ReturnNull_When_ResourceNameUnderDifferentProvider()
    {
        // The split's canonical regression (ADR-0026, invariant #5): a name owned by one provider is
        // unreachable through another provider's route.
        await SeedProviderAndResourceAsync("acme", ProviderId.New(), ProjectResourceId.New(), "shared-name", EOperationMode.Unspecified);
        await SeedProviderAsync("beta", ProviderId.New());

        var result = await QueryAsync(q => q.GetResourceSurrogateAsync(Key("beta", "shared-name"), TestContext.Current.CancellationToken));

        Assert.Null(result);
    }

    [Fact]
    public async Task Should_ReturnNull_When_ResourceHasNullProviderId()
    {
        // A NULL-ProviderId legacy row has no provider to address it under (D5) → unreachable via v2 assign.
        await SeedProviderAsync("acme", ProviderId.New());
        await SeedLegacyResourceWithoutProviderAsync(ProjectResourceId.New(), "legacy-res");

        var result = await QueryAsync(q => q.GetResourceSurrogateAsync(Key("acme", "legacy-res"), TestContext.Current.CancellationToken));

        Assert.Null(result);
    }

    [Fact]
    public async Task Should_ReturnNull_When_ResourceSoftDeletedUnderProvider()
    {
        // The !IsDeleted query filter hides a soft-deleted row, so it resolves like a missing one → 404.
        await SeedProviderAndResourceAsync("acme", ProviderId.New(), ProjectResourceId.New(), "gpu-res", EOperationMode.Unspecified, deleted: true);

        var result = await QueryAsync(q => q.GetResourceSurrogateAsync(Key("acme", "gpu-res"), TestContext.Current.CancellationToken));

        Assert.Null(result);
    }

    [Fact]
    public async Task Should_ExposeProviderName_When_ResourceMaterialisedWithProvider()
    {
        // ADR-0031: the owning provider is a shadow FK; once the _provider navigation is materialised the
        // resource exposes the provider's natural key through ProviderName (no surrogate id on the surface).
        var ct = TestContext.Current.CancellationToken;
        var resourceId = ProjectResourceId.New();
        await SeedProviderAndResourceAsync("acme", ProviderId.New(), resourceId, "OPEN-33-82", EOperationMode.Unspecified);

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var resource = await context.Set<ProjectResource>()
            .Include("_provider")
            .FirstOrDefaultAsync(pr => EF.Property<ProjectResourceId>(pr, "Id") == resourceId, ct);

        Assert.NotNull(resource);
        Assert.Equal(ProviderName.From("acme"), resource.ProviderName);
        Assert.Equal(Key("acme", "OPEN-33-82"), resource.Key);
    }

    [Fact]
    public async Task Should_DistinguishKnownFromUnknown_AllocationUnitType()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAllocationUnitTypeAsync(7, "node-hours");

        Assert.True(await QueryAsync(q => q.AllocationUnitTypeExistsAsync(7, ct)).ConfigureAwait(false));
        Assert.False(await QueryAsync(q => q.AllocationUnitTypeExistsAsync(999, ct)).ConfigureAwait(false));
    }

    [Fact]
    public async Task Should_MapActiveProjects_And_ExcludeDeletedLinks_When_GettingProjectsUsingResource()
    {
        var ct = TestContext.Current.CancellationToken;
        var resourceId = ProjectResourceId.New();
        await SeedProviderAndResourceAsync("acme", ProviderId.New(), resourceId, "gpu-res", EOperationMode.Unspecified);
        var activeProject = await SeedProjectAsync("proj-active");
        var deletedLinkProject = await SeedProjectAsync("proj-deleted");
        await SeedResourceToProjectAsync(resourceId, activeProject, deleted: false);
        await SeedResourceToProjectAsync(resourceId, deletedLinkProject, deleted: true);

        var result = await QueryAsync(q => q.GetProjectsUsingResourceAsync(Key("acme", "gpu-res"), ct));

        var only = Assert.Single(result);
        Assert.Equal("proj-active", only.ProjectShortName.Value);
        Assert.Equal("gpu-res", only.ResourceName.Value);
    }

    [Fact]
    public async Task Should_ReturnEmpty_When_ResourceKeyUnresolved()
    {
        var result = await QueryAsync(q => q.GetProjectsUsingResourceAsync(Key("acme", "missing"), TestContext.Current.CancellationToken));

        Assert.Empty(result);
    }

    private async Task<TResult> QueryAsync<TResult>(Func<IResourceAssignmentQuery, Task<TResult>> act)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IResourceAssignmentQuery>();
        return await act(query);
    }

    // The in-memory provider does not run SQL defaults, so the persistence-only shadow "ServiceName"
    // (HasDefaultValueSql('LEXIS') on Location/AssignedLocation) stays null and trips its required check.
    // Fill it the way the real INSERT default would before saving.
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

    // Seeds an active provider with an explicit surrogate id and one resource it OWNS (ProviderId = providerId),
    // optionally soft-deleted — the per-provider resolver maps (providerName, name) → that id under the EXISTS
    // ownership gate + !IsDeleted filter.
    private async Task SeedProviderAndResourceAsync(
        string providerName, ProviderId providerId, ProjectResourceId resourceId, string resourceName,
        EOperationMode mode, bool deleted = false)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(ProviderName.From(providerName), countryId: 1);
        context.Add(provider);
        context.Entry(provider).Property("Id").CurrentValue = providerId;

        var resource = ProjectResource.Create(ResourceName.Create(resourceName), provider, UserId.From(Guid.NewGuid()), Start, End, operationMode: mode);
        if (deleted)
        {
            resource.Delete();
            resource.UpdateState(EProjectResourceState.Deleted);
        }

        context.Add(resource);
        context.Entry(resource).Property("Id").CurrentValue = resourceId;
        await SaveAsync(context, ct);
    }

    private async Task SeedProviderAsync(string providerName, ProviderId providerId)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(ProviderName.From(providerName), countryId: 1);
        context.Add(provider);
        context.Entry(provider).Property("Id").CurrentValue = providerId;
        await SaveAsync(context, ct);
    }

    // A legacy standalone row: ProviderId is null. Create requires a non-null id, so null it after building.
    private async Task SeedLegacyResourceWithoutProviderAsync(ProjectResourceId resourceId, string resourceName)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var owner = ProviderAggregate.Create(ProviderName.From($"owner-{Guid.NewGuid():N}"), countryId: 1);
        context.Add(owner);
        var resource = ProjectResource.Create(ResourceName.Create(resourceName), owner, UserId.From(Guid.NewGuid()), Start, End);
        context.Add(resource);
        context.Entry(resource).Property("Id").CurrentValue = resourceId;
        // Legacy standalone row: null both the navigation and the shadow FK so the ProviderId column persists as null.
        context.Entry(resource).Reference("_provider").CurrentValue = null;
        context.Entry(resource).Property("ProviderId").CurrentValue = null;
        await SaveAsync(context, ct);
    }

    private async Task SeedProviderWithAssignmentAsync(string providerName, ProjectResourceId resourceId, string resourceName, bool locationShared, bool softDelete)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(ProviderName.From(providerName), countryId: 1);
        var resource = ProjectResource.Create(ResourceName.Create(resourceName), provider, UserId.From(Guid.NewGuid()), Start, End);
        Assert.True(provider.AddLocation(Gpu, "https://gpu", ELocationType.HPC, isShared: locationShared).IsSuccess);
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

    private async Task SeedAllocationUnitTypeAsync(int id, string name)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        // AllocationUnitType is seeded reference data with no public factory; instantiate it directly.
        var entity = (AllocationUnitType)Activator.CreateInstance(typeof(AllocationUnitType), nonPublic: true)!;
        typeof(AllocationUnitType).GetProperty(nameof(AllocationUnitType.Id))!.SetValue(entity, id);
        typeof(AllocationUnitType).GetProperty(nameof(AllocationUnitType.Name))!.SetValue(entity, name);
        context.Add(entity);
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
            // Distinct domain-type name per project: FromName keys the ProjectDomainType by name, so a
            // shared "HPC" would collide on the second insert into the same in-memory store.
            ProjectDomainType.FromName(DomainTypeName.Create($"domain-{shortName}")),
            UserId.New(),
            Start,
            End,
            ServiceName.Default);
        context.Add(project);
        context.Entry(project).Property("Id").CurrentValue = projectId;

        // FromName yields an unresolved ProjectDomainType with the sentinel key -1; seeding two projects
        // would insert two rows with that same key and collide. Give each a distinct key (the FK is
        // required, so detaching is not an option) — the query under test never joins the domain type.
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
