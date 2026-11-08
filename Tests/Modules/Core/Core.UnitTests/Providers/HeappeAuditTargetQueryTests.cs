using Core.DataAccess.Contexts;
using Core.DataAccess.Events;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Shared;

using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Shared;

using ProjectAggregate = Core.Domain.Project.Project;
using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Providers;

/// <summary>
/// Regression tests for <see cref="HeappeAuditTargetQuery"/> — the modular read side behind the HEAppE
/// resource validity audit (ADR-0025) — over the EF Core in-memory provider. The provider compiles the
/// shadow-FK joins (<c>ProjectId</c>, <c>ProjectResourceId</c>, <c>LocationId</c>, <c>AssignedLocationId</c>)
/// the same way SQL Server does, mirroring <see cref="ResourceAssignmentQueryTests"/>.
/// </summary>
public sealed class HeappeAuditTargetQueryTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValidStart = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValidEnd = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiredEnd = new(2026, 3, 31, 0, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _serviceProvider;
    private int _domainTypeKeySeed = 2000;

    public HeappeAuditTargetQueryTests()
    {
        var dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DomainEventChannel>();
        services.AddScoped<IRoleService, UserDatabaseAccessTests.TestRoleService>();
        services.Configure<WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped(typeof(IDataQuery<>), typeof(DataQuery<>));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddScoped<IHeappeAuditTargetQuery, HeappeAuditTargetQuery>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_ReturnOnlyValidHpcResourceWithHeappeValues_When_ListingAuditTargets()
    {
        var ct = TestContext.Current.CancellationToken;

        // Expected: currently-valid resource, HPC location, both HEAPPE values present.
        var validResourceId = ProjectResourceId.New();
        await SeedResourceAsync(validResourceId, "valid-hpc", ValidStart, ValidEnd);
        var validProject = await SeedProjectAsync("proj-valid");
        await SeedResourceToProjectAsync(validResourceId, validProject);
        await SeedAssignmentAsync(validResourceId, "loc-hpc", ELocationType.HPC,
            ("HEAPPE_URL", "https://heappe.example.org"), ("HEAPPE_IDENTIFIER", "inst-1"));

        // Excluded: non-HPC (Cloud) location, even with HEAPPE values.
        var cloudResourceId = ProjectResourceId.New();
        await SeedResourceAsync(cloudResourceId, "cloud-res", ValidStart, ValidEnd);
        var cloudProject = await SeedProjectAsync("proj-cloud");
        await SeedResourceToProjectAsync(cloudResourceId, cloudProject);
        await SeedAssignmentAsync(cloudResourceId, "loc-cloud", ELocationType.CLOUD,
            ("HEAPPE_URL", "https://heappe.example.org"), ("HEAPPE_IDENTIFIER", "inst-2"));

        // Excluded: expired resource (EndDate < now), HPC location with HEAPPE values.
        var expiredResourceId = ProjectResourceId.New();
        await SeedResourceAsync(expiredResourceId, "expired-hpc", ValidStart, ExpiredEnd);
        var expiredProject = await SeedProjectAsync("proj-expired");
        await SeedResourceToProjectAsync(expiredResourceId, expiredProject);
        await SeedAssignmentAsync(expiredResourceId, "loc-hpc-expired", ELocationType.HPC,
            ("HEAPPE_URL", "https://heappe.example.org"), ("HEAPPE_IDENTIFIER", "inst-3"));

        var result = await QueryAsync(q => q.GetAuditTargetsAsync(ct));

        var target = Assert.Single(result);
        Assert.Equal("valid-hpc", target.ResourceName);
        Assert.Equal("proj-valid", target.ProjectShortName);
        Assert.Equal("https://heappe.example.org", target.HeappeUrl);
        Assert.Equal("inst-1", target.HeappeIdentifier);
        Assert.Equal(ValidStart, target.StartDate);
        Assert.Equal(ValidEnd, target.EndDate);
    }

    private async Task<TResult> QueryAsync<TResult>(Func<IHeappeAuditTargetQuery, Task<TResult>> act)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IHeappeAuditTargetQuery>();
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

    private async Task SeedResourceAsync(ProjectResourceId id, string resourceName, DateTimeOffset start, DateTimeOffset end)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var owner = ProviderAggregate.Create(ProviderName.From($"owner-{Guid.NewGuid():N}"), countryId: 1);
        context.Add(owner);
        var resource = ProjectResource.Create(ResourceName.Create(resourceName), owner, UserId.From(Guid.NewGuid()), start, end);
        context.Add(resource);
        context.Entry(resource).Property("Id").CurrentValue = id;
        await SaveAsync(context, ct);
    }

    private async Task SeedAssignmentAsync(
        ProjectResourceId resourceId,
        string locationName,
        ELocationType locationType,
        params (string Name, string Value)[] specifications)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var name = LocationName.From(locationName);
        var provider = ProviderAggregate.Create(ProviderName.From($"prov-{locationName}"), countryId: 1);
        Assert.True(provider.AddLocation(name, "https://loc", locationType, isShared: true).IsSuccess);

        // AssignResource enforces invariant #5 (resource owned by this provider), so assign a same-provider
        // placeholder, then override the assignment's ProjectResourceId shadow FK to the pre-seeded resource id
        // (ADR-0041 L2.1) — exercising the row→resource link this query reads, without the cross-provider guard.
        var placeholder = ProjectResource.Create(ResourceName.Create($"placeholder-{locationName}"), provider, UserId.From(Guid.NewGuid()), ValidStart, ValidEnd);
        var assignment = provider.AssignResource(
            name, placeholder, allocationUnitTypeId: 1, allocationUnitCount: null,
            description: null, aggregationName: null, specifications: specifications,
            resourceHasNonSharedLocation: false, slug: $"asg-{resourceId.Value:N}");
        Assert.True(assignment.IsSuccess);

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
            ValidStart,
            ValidEnd,
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
