using Core.DataAccess.Contexts;
using Core.DataAccess.Events;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Provider;
using Core.Domain.Shared;

using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using DomainProviderName = Core.Domain.Provider.ProviderName;
using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;
using ProjectAggregate = Core.Domain.Project.Project;
using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Providers;

/// <summary>
/// Tests for <see cref="DdiProjectsInfoQuery"/> over the EF Core in-memory provider, covering the legacy
/// "ProjectsInfo" aggregation reproduced in D3: active resources grouped by their own owning provider
/// (ADR-0031), null-provider resources in the empty-provider group, the legacy CollectorName format, the
/// active-date filter, soft-delete exclusion of assignments and project links, and the assignment
/// projection (LocationType name, AllocationUnitType enum + specifications). Mirrors the harness in
/// <see cref="ResourceDetailsQueryTests"/>.
/// </summary>
public sealed class DdiProjectsInfoQueryTests : IAsyncDisposable
{
    private static readonly (string Name, string Value)[] NoSpecs = [];
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2030, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _serviceProvider;
    private int _domainTypeKeySeed = 5000;

    public DdiProjectsInfoQueryTests()
    {
        var dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DomainEventChannel>();
        services.AddScoped<IRoleService, UserDatabaseAccessTests.TestRoleService>();
        services.Configure<WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped(typeof(IDataQuery<>), typeof(DataQuery<>));
        services.AddScoped<IDdiProjectsInfoQuery, DdiProjectsInfoQuery>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_GroupResourcesByOwningProvider_When_ProviderIsNullOrSet()
    {
        // gpu-res owned by provider "acme"; orphan-res owned by no provider (null) → empty-provider group.
        var ct = TestContext.Current.CancellationToken;
        var acmeId = ProviderId.New();
        await SeedProviderAsync("acme", acmeId, description: "Acme HPC");
        await SeedResourceAsync(ProjectResourceId.New(), "gpu-res", acmeId);
        await SeedResourceAsync(ProjectResourceId.New(), "orphan-res", ownerProviderId: null);

        var groups = await QueryAsync(ct);

        Assert.Equal(2, groups.Count);

        var acme = Assert.Single(groups, g => g.ProviderId == acmeId.Value.ToString());
        Assert.Equal("acme", acme.ProviderName);
        Assert.Equal("Acme HPC", acme.ProviderDescription);
        Assert.Equal("gpu-res", Assert.Single(acme.Resources).Name);

        var empty = Assert.Single(groups, g => string.IsNullOrEmpty(g.ProviderId));
        Assert.Null(empty.ProviderName);
        Assert.Equal(string.Empty, empty.ProviderDescription);
        Assert.Equal("orphan-res", Assert.Single(empty.Resources).Name);
    }

    [Fact]
    public async Task Should_FormatCollectorNameWithUnderscores_When_ProviderNameHasSpace()
    {
        // CollectorName = {ShortName}|{ProviderName with spaces → underscores}|{ResourceName}.
        var ct = TestContext.Current.CancellationToken;
        var providerId = ProviderId.New();
        var resourceId = ProjectResourceId.New();
        await SeedProviderAsync("Big Iron", providerId, description: null);
        await SeedResourceAsync(resourceId, "gpu-res", providerId);
        var project = await SeedProjectAsync("proj-a");
        await SeedResourceToProjectAsync(resourceId, project, deleted: false);

        var groups = await QueryAsync(ct);

        var resource = Assert.Single(Assert.Single(groups).Resources);
        var associated = Assert.Single(resource.AssociatedProjects);
        Assert.Equal("proj-a|Big_Iron|gpu-res", associated.CollectorName);
        Assert.Equal("proj-a", associated.ShortName);
    }

    [Fact]
    public async Task Should_ExcludeResource_When_EndDateBeforeToday()
    {
        // Active filter: EndDate >= today. A resource ending yesterday is excluded.
        var ct = TestContext.Current.CancellationToken;
        var providerId = ProviderId.New();
        await SeedProviderAsync("acme", providerId, description: null);
        await SeedResourceAsync(ProjectResourceId.New(), "active-res", providerId);
        await SeedResourceAsync(
            ProjectResourceId.New(), "expired-res", providerId,
            startDate: new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-10), TimeSpan.Zero),
            endDate: new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-1), TimeSpan.Zero));

        var groups = await QueryAsync(ct);

        var resource = Assert.Single(Assert.Single(groups).Resources);
        Assert.Equal("active-res", resource.Name);
    }

    [Fact]
    public async Task Should_ExcludeSoftDeletedAssignmentsAndProjectLinks_When_Projecting()
    {
        // A soft-deleted AssignedLocation is excluded from Assignments; a soft-deleted project link is
        // excluded from AssociatedProjects.
        var ct = TestContext.Current.CancellationToken;
        var providerId = ProviderId.New();
        var resourceId = ProjectResourceId.New();
        await SeedProviderWithActiveAndDeletedAssignmentAsync("acme", providerId, resourceId, "gpu-res");

        var activeProject = await SeedProjectAsync("proj-active");
        var deletedProject = await SeedProjectAsync("proj-deleted");
        await SeedResourceToProjectAsync(resourceId, activeProject, deleted: false);
        await SeedResourceToProjectAsync(resourceId, deletedProject, deleted: true);

        var groups = await QueryAsync(ct);

        var resource = Assert.Single(Assert.Single(groups, g => g.ProviderId == providerId.Value.ToString()).Resources);

        var assignment = Assert.Single(resource.Assignments);
        Assert.Equal("gpu", assignment.LocationName); // the active one, not the soft-deleted "cpu"

        var associated = Assert.Single(resource.AssociatedProjects);
        Assert.Equal("proj-active", associated.ShortName);
    }

    [Fact]
    public async Task Should_ResolveResourceTypeAndUnitTypeAndSpecs_When_Projecting()
    {
        var ct = TestContext.Current.CancellationToken;
        var providerId = ProviderId.New();
        var resourceId = ProjectResourceId.New();
        await SeedLocationTypeAsync(ELocationType.Kubernetes, "Cluster");
        await SeedProviderWithTypedAssignmentAsync(
            "acme", providerId, "gpu", resourceId, "gpu-res",
            locationType: ELocationType.Kubernetes, allocationUnitTypeId: (int)EAllocationUnitType.NodeHours, specs: [("CORES", "128")]);

        var groups = await QueryAsync(ct);

        var resource = Assert.Single(Assert.Single(groups, g => g.ProviderId == providerId.Value.ToString()).Resources);
        var assignment = Assert.Single(resource.Assignments);

        Assert.Equal("gpu", assignment.LocationName);
        Assert.Equal("Cluster", assignment.ResourceType);
        Assert.Equal(EAllocationUnitType.NodeHours, assignment.AllocationUnitType);
        var spec = Assert.Single(assignment.Specifications);
        Assert.Equal("CORES", spec.Name);
        Assert.Equal("128", spec.Value);
    }

    private async Task<IReadOnlyList<ProviderResourcesView>> QueryAsync(CancellationToken ct)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IDdiProjectsInfoQuery>();
        return await query.GetProjectsInfoAsync(ct);
    }

    // The in-memory provider does not run SQL defaults, so the persistence-only shadow "ServiceName"
    // (HasDefaultValueSql('LEXIS')) stays null and trips its required check. Mirrors ResourceDetailsQueryTests.
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

    // A provider with an explicit surrogate id, so resources can carry its ProviderId shadow FK and group by it.
    private async Task SeedProviderAsync(string providerName, ProviderId providerId, string? description)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(
            DomainProviderName.From(providerName),
            countryId: 1,
            description: description is null ? null : Description.Create(description));
        context.Add(provider);
        context.Entry(provider).Property("Id").CurrentValue = providerId;
        await SaveAsync(context, ct);
    }

    // A resource owned by the given provider (null → standalone/orphan resource).
    private async Task SeedResourceAsync(
        ProjectResourceId id,
        string resourceName,
        ProviderId? ownerProviderId,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        // A resource needs a Provider instance for its factory; when ownerProviderId is null we still need a
        // provider for Create but leave the shadow FK null afterwards (legacy standalone row).
        var ownerName = DomainProviderName.From($"owner-{Guid.NewGuid():N}");
        var owner = ProviderAggregate.Create(ownerName, countryId: 1);
        context.Add(owner);

        var resource = ProjectResource.Create(
            ResourceName.Create(resourceName), owner, UserId.From(Guid.NewGuid()), startDate ?? Start, endDate ?? End);
        context.Add(resource);
        context.Entry(resource).Property("Id").CurrentValue = id;

        // Override the shadow FK directly: null for an orphan, the requested id for an owned resource. The
        // ProjectResource.Create wires _provider to the throwaway owner, so set the FK column explicitly.
        context.Entry(resource).Property("ProviderId").CurrentValue =
            ownerProviderId is { } pid ? (ProviderId?)pid : null;

        await SaveAsync(context, ct);
    }

    private async Task<Core.Domain.Project.ProjectId> SeedProjectAsync(string shortName)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var projectId = Core.Domain.Project.ProjectId.From(Guid.NewGuid());
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

    private async Task SeedResourceToProjectAsync(ProjectResourceId resourceId, Core.Domain.Project.ProjectId projectId, bool deleted)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var link = Core.Domain.Project.AssignedProjectResourceToProject.Create();
        if (deleted)
        {
            link.Delete();
        }

        context.Add(link);
        context.Entry(link).Property("ProjectId").CurrentValue = projectId;
        context.Entry(link).Property("ProjectResourceId").CurrentValue = resourceId;
        await SaveAsync(context, ct);
    }

    // One provider (its own owner), the resource assigned to two locations — one active ("gpu"), one
    // soft-deleted ("cpu"). Invariant #5 (ADR-0041 L2.3) requires the assigned resource to belong to the
    // assigning provider, so the resource is created under this provider with its surrogate forced to
    // resourceId; grouping still keys off the resource's owning provider (providerId).
    private async Task SeedProviderWithActiveAndDeletedAssignmentAsync(
        string providerName, ProviderId providerId, ProjectResourceId resourceId, string resourceName)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(DomainProviderName.From(providerName), countryId: 1);
        var gpu = LocationName.From("gpu");
        var cpu = LocationName.From("cpu");
        Assert.True(provider.AddLocation(gpu, "https://gpu", ELocationType.HPC, isShared: true).IsSuccess);
        Assert.True(provider.AddLocation(cpu, "https://cpu", ELocationType.HPC, isShared: true).IsSuccess);

        var resource = ProjectResource.Create(ResourceName.Create(resourceName), provider, UserId.From(Guid.NewGuid()), Start, End);
        context.Add(resource);
        context.Entry(resource).Property("Id").CurrentValue = resourceId;

        Assert.True(provider.AssignResource(gpu, resource, 1, null, null, null, NoSpecs, false, slug: "asg-gpu").IsSuccess);
        var deleted = provider.AssignResource(cpu, resource, 1, null, null, null, NoSpecs, false, slug: "asg-cpu");
        Assert.True(deleted.IsSuccess);
        Assert.True(provider.RemoveAssignment(cpu, deleted.Value.Slug!).IsSuccess);

        context.Add(provider);
        context.Entry(provider).Property("Id").CurrentValue = providerId;
        context.Entry(resource).Property("ProviderId").CurrentValue = providerId;
        await SaveAsync(context, ct);
    }

    // One provider (its own owner), one location of the given LocationType, the resource (owned by this
    // provider, surrogate forced to resourceId per invariant #5) assigned with the given unit type + specs.
    private async Task SeedProviderWithTypedAssignmentAsync(
        string providerName,
        ProviderId providerId,
        string locationName,
        ProjectResourceId resourceId,
        string resourceName,
        ELocationType locationType,
        int allocationUnitTypeId,
        (string Name, string Value)[] specs)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var location = LocationName.From(locationName);
        var provider = ProviderAggregate.Create(DomainProviderName.From(providerName), countryId: 1);
        Assert.True(provider.AddLocation(location, $"https://{locationName}", locationType, isShared: true).IsSuccess);

        var resource = ProjectResource.Create(ResourceName.Create(resourceName), provider, UserId.From(Guid.NewGuid()), Start, End);
        context.Add(resource);
        context.Entry(resource).Property("Id").CurrentValue = resourceId;

        Assert.True(provider.AssignResource(
            location, resource, allocationUnitTypeId, allocationUnitCount: null,
            description: null, aggregationName: null, specifications: specs,
            resourceHasNonSharedLocation: false, slug: $"asg-{locationName}").IsSuccess);

        context.Add(provider);
        context.Entry(provider).Property("Id").CurrentValue = providerId;
        context.Entry(resource).Property("ProviderId").CurrentValue = providerId;

        await SaveAsync(context, ct);
    }

    private async Task SeedLocationTypeAsync(ELocationType id, string name)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var entity = (LocationType)Activator.CreateInstance(typeof(LocationType), nonPublic: true)!;
        typeof(LocationType).GetProperty(nameof(LocationType.Id))!.SetValue(entity, id);
        typeof(LocationType).GetProperty(nameof(LocationType.Name))!.SetValue(entity, name);
        context.Add(entity);
        await SaveAsync(context, ct);
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();
}
