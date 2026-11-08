using Core.DataAccess.Contexts;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ProjectAggregate = Core.Domain.Project.Project;
using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Projects;

/// <summary>
/// <see cref="ProjectDetailsQuery.GetProjectsAsync"/>'s provider-admin tier (<see cref="ProjectFilter.ProviderName"/>),
/// run against the SQLite relational provider rather than EF InMemory: the membership join spans
/// Project → AssignedProjectResourceToProject → ProjectResource on shadow-FK columns, and a nested
/// correlated <c>.Any()</c> over that shape silently returns an empty set under InMemory — a real
/// provider is the only one that exercises the actual translated SQL this endpoint runs in production.
/// </summary>
public sealed class ProjectDetailsQueryTests : SqliteQueryHarness
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Expired = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private int _domainTypeKeySeed = 1000;

    protected override void AddServices(IServiceCollection services) =>
        services.AddScoped<IProjectQuery, ProjectDetailsQuery>();

    [Fact]
    public async Task Should_IncludeProject_WhenOnlyOneProviderResourceLinksIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await SeedProviderAsync("acme");
        var project = await SeedProjectAsync("proj-a");
        var resource = await SeedResourceAsync(acme, Start, End);
        await LinkAsync(project, resource);

        var result = await QueryAsync(q => q.GetProjectsAsync(ProviderFilter("acme"), 1, 50, ct));

        var only = Assert.Single(result.Items);
        Assert.Equal("proj-a", only.ShortName.Value);
    }

    [Fact]
    public async Task Should_ResolveMembership_OnRelationalProvider()
    {
        // Two providers, one matching resource each, real SQL translation: proves the join discriminates
        // by provider on an actual relational engine. This does not pin the join's shape — a nested
        // correlated .Any() over the shadow FK would pass identically here; only EF InMemory tells the
        // two apart (per the class doc), so the shape constraint is enforced by review, not by a test.
        var ct = TestContext.Current.CancellationToken;
        var acme = await SeedProviderAsync("acme");
        var other = await SeedProviderAsync("other");
        var matching = await SeedProjectAsync("proj-acme");
        var nonMatching = await SeedProjectAsync("proj-other");
        await LinkAsync(matching, await SeedResourceAsync(acme, Start, End));
        await LinkAsync(nonMatching, await SeedResourceAsync(other, Start, End));

        var result = await QueryAsync(q => q.GetProjectsAsync(ProviderFilter("acme"), 1, 50, ct));

        var only = Assert.Single(result.Items);
        Assert.Equal("proj-acme", only.ShortName.Value);
    }

    [Fact]
    public async Task Should_ExcludeSoftDeletedAssignment()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await SeedProviderAsync("acme");
        var project = await SeedProjectAsync("proj-a");
        var resource = await SeedResourceAsync(acme, Start, End);
        await LinkAsync(project, resource, isDeleted: true);

        var result = await QueryAsync(q => q.GetProjectsAsync(ProviderFilter("acme"), 1, 50, ct));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Should_ExcludeNullProviderResource()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("acme");
        var project = await SeedProjectAsync("proj-a");
        var resource = await SeedResourceAsync(providerId: null, Start, End);
        await LinkAsync(project, resource);

        var result = await QueryAsync(q => q.GetProjectsAsync(ProviderFilter("acme"), 1, 50, ct));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Should_ExcludeSoftDeletedProjectResource()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await SeedProviderAsync("acme");
        var project = await SeedProjectAsync("proj-a");
        var resource = await SeedResourceAsync(acme, Start, End, isDeleted: true);
        await LinkAsync(project, resource);

        var result = await QueryAsync(q => q.GetProjectsAsync(ProviderFilter("acme"), 1, 50, ct));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Should_ReturnEmptyPage_When_ProviderIsDeactivated()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await SeedProviderAsync("acme", isDeleted: true);
        var project = await SeedProjectAsync("proj-a");
        var resource = await SeedResourceAsync(acme, Start, End);
        await LinkAsync(project, resource);

        var result = await QueryAsync(q => q.GetProjectsAsync(ProviderFilter("acme"), 1, 50, ct));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalItems);
    }

    [Fact]
    public async Task Should_SplitAcrossPages_WhenResultsExceedLimit()
    {
        // proj-a carries two matching resources: Distinct() must still collapse it to one row before
        // paging, so a page boundary drawn mid-fan-out cannot double-count it or tear the third project
        // onto the wrong page.
        var ct = TestContext.Current.CancellationToken;
        var acme = await SeedProviderAsync("acme");
        var projectA = await SeedProjectAsync("proj-a");
        var projectB = await SeedProjectAsync("proj-b");
        var projectC = await SeedProjectAsync("proj-c");
        await LinkAsync(projectA, await SeedResourceAsync(acme, Start, End));
        await LinkAsync(projectA, await SeedResourceAsync(acme, Start, End));
        await LinkAsync(projectB, await SeedResourceAsync(acme, Start, End));
        await LinkAsync(projectC, await SeedResourceAsync(acme, Start, End));

        var firstPage = await QueryAsync(q => q.GetProjectsAsync(ProviderFilter("acme"), 1, 2, ct));
        var secondPage = await QueryAsync(q => q.GetProjectsAsync(ProviderFilter("acme"), 2, 2, ct));

        Assert.Equal(["proj-a", "proj-b"], firstPage.Items.Select(p => p.ShortName.Value));
        Assert.Equal(3, firstPage.TotalItems);
        Assert.Equal(["proj-c"], secondPage.Items.Select(p => p.ShortName.Value));
        Assert.Equal(3, secondPage.TotalItems);
    }

    [Fact]
    public async Task Should_ExcludeSoftDeletedProject()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await SeedProviderAsync("acme");
        var project = await SeedProjectAsync("proj-a", isDeleted: true);
        var resource = await SeedResourceAsync(acme, Start, End);
        await LinkAsync(project, resource);

        var result = await QueryAsync(q => q.GetProjectsAsync(ProviderFilter("acme"), 1, 50, ct));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Should_IncludeExpiredProject()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await SeedProviderAsync("acme");
        var project = await SeedProjectAsync("proj-a", startDate: Expired.AddYears(-1), endDate: Expired);
        var resource = await SeedResourceAsync(acme, Start, End);
        await LinkAsync(project, resource);

        var result = await QueryAsync(q => q.GetProjectsAsync(ProviderFilter("acme"), 1, 50, ct));

        var only = Assert.Single(result.Items);
        Assert.Equal("proj-a", only.ShortName.Value);
        Assert.Equal(Expired, only.EndDate);
    }

    [Fact]
    public async Task Should_IncludeProject_WhenSomeProviderResourcesExpired_AndOthersValid()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await SeedProviderAsync("acme");
        var project = await SeedProjectAsync("proj-a");
        await LinkAsync(project, await SeedResourceAsync(acme, Expired, Expired));
        await LinkAsync(project, await SeedResourceAsync(acme, Start, End));

        var result = await QueryAsync(q => q.GetProjectsAsync(ProviderFilter("acme"), 1, 50, ct));

        var only = Assert.Single(result.Items);
        Assert.Equal("proj-a", only.ShortName.Value);
    }

    [Fact]
    public async Task Should_IncludeProject_WhenAllProviderResourcesExpired()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await SeedProviderAsync("acme");
        var project = await SeedProjectAsync("proj-a");
        await LinkAsync(project, await SeedResourceAsync(acme, Expired, Expired));

        var result = await QueryAsync(q => q.GetProjectsAsync(ProviderFilter("acme"), 1, 50, ct));

        var only = Assert.Single(result.Items);
        Assert.Equal("proj-a", only.ShortName.Value);
    }

    [Fact]
    public async Task Should_ReturnProjectOnce_WhenSeveralResourcesLinkIt()
    {
        // Existential membership plus the mandated explicit .Join() means n matching resources would
        // yield n rows without the Distinct() collapse — assert both the row count and the paged total,
        // since a query that still double-counts in PagedResponse can pass a row-count-only assertion.
        var ct = TestContext.Current.CancellationToken;
        var acme = await SeedProviderAsync("acme");
        var project = await SeedProjectAsync("proj-a");
        await LinkAsync(project, await SeedResourceAsync(acme, Start, End));
        await LinkAsync(project, await SeedResourceAsync(acme, Start, End));
        await LinkAsync(project, await SeedResourceAsync(acme, Expired, Expired));

        var result = await QueryAsync(q => q.GetProjectsAsync(ProviderFilter("acme"), 1, 50, ct));

        var only = Assert.Single(result.Items);
        Assert.Equal("proj-a", only.ShortName.Value);
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task Should_ReturnOnlyProjectsOfTheDomainType_When_FilteredByDomainTypeName()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProjectAsync("proj-a");
        await SeedProjectAsync("proj-b");

        var result = await QueryAsync(q => q.GetProjectsAsync(DomainTypeFilter("domain-proj-b"), 1, 50, ct));

        var only = Assert.Single(result.Items);
        Assert.Equal("proj-b", only.ShortName.Value);
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task Should_ReturnEmptyPage_When_DomainTypeNameUnknown()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProjectAsync("proj-a");

        var result = await QueryAsync(q => q.GetProjectsAsync(DomainTypeFilter("no-such-domain"), 1, 50, ct));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalItems);

        var unfiltered = await QueryAsync(q => q.GetProjectsAsync(new ProjectFilter(null, null, null, null, null), 1, 50, ct));
        Assert.Single(unfiltered.Items);
    }

    [Fact]
    public async Task Should_ReturnDomainTypeName_When_GettingExtendedProjectWithoutResources()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProjectAsync("proj-a");
        await SeedProjectAsync("proj-b");

        var result = await ExtendedAsync(q => q.GetExtendedProjectAsync(new ProjectShortName("proj-b"), ct));

        Assert.NotNull(result);
        Assert.Empty(result.AssignedResources);
        Assert.Equal("domain-proj-b", result.DomainTypeName.Value);
    }

    [Fact]
    public async Task Should_ReturnDomainTypeName_When_GettingExtendedProjectWithResources()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await SeedProviderAsync("acme");
        await SeedProjectAsync("proj-a");
        var project = await SeedProjectAsync("proj-b");
        await LinkAsync(project, await SeedResourceAsync(acme, Start, End));

        var result = await ExtendedAsync(q => q.GetExtendedProjectAsync(new ProjectShortName("proj-b"), ct));

        Assert.NotNull(result);
        Assert.Equal("domain-proj-b", result.DomainTypeName.Value);
    }

    private static ProjectFilter DomainTypeFilter(string domainTypeName) => new(
        ShortName: null,
        ContactEmail: null,
        DomainTypeName: DomainTypeName.Create(domainTypeName),
        StartDate: null,
        EndDate: null);

    private async Task<ExtendedProjectSummary?> ExtendedAsync(Func<IProjectQuery, Task<ExtendedProjectSummary?>> act)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IProjectQuery>();
        return await act(query);
    }

    private static ProjectFilter ProviderFilter(string providerName) => new(
        ShortName: null,
        ContactEmail: null,
        DomainTypeName: null,
        StartDate: null,
        EndDate: null,
        ScopeShortNames: null,
        ProviderName: ProviderName.From(providerName));

    private async Task<QueryPage<ProjectSummary>> QueryAsync(Func<IProjectQuery, Task<QueryPage<ProjectSummary>>> act)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IProjectQuery>();
        return await act(query);
    }

    private async Task<ProviderId> SeedProviderAsync(string name, bool isDeleted = false)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(ProviderName.From(name), countryId: 1);
        if (isDeleted)
        {
            Assert.True(provider.Deactivate().IsSuccess);
        }

        context.Add(provider);
        await SaveAsync(context, ct);

        return (ProviderId)context.Entry(provider).Property("Id").CurrentValue!;
    }

    // A null providerId seeds a legacy standalone resource: ProjectResource.Create requires a real owning
    // Provider, so a throwaway one is created to satisfy it and the shadow ProviderId FK is nulled out
    // afterwards, reproducing a pre-ADR-0031 row rather than a resource that legitimately belongs to acme.
    private async Task<ProjectResourceId> SeedResourceAsync(ProviderId? providerId, DateTimeOffset start, DateTimeOffset end, bool isDeleted = false)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = providerId is { } id
            ? await context.Set<ProviderAggregate>().IgnoreQueryFilters().SingleAsync(p => EF.Property<ProviderId>(p, "Id") == id, ct)
            : ProviderAggregate.Create(ProviderName.From($"placeholder-{Guid.NewGuid():N}"), countryId: 1);

        if (providerId is null)
        {
            context.Add(provider);
        }

        var resource = ProjectResource.Create(ResourceName.Create($"res-{Guid.NewGuid():N}"), provider, UserId.From(Guid.NewGuid()), start, end);
        if (isDeleted)
        {
            resource.Delete();
            resource.UpdateState(EProjectResourceState.Deleted);
        }

        context.Add(resource);
        await SaveAsync(context, ct);

        if (providerId is null)
        {
            context.Entry(resource).Property("ProviderId").CurrentValue = null;
            await context.SaveChangesAsync(ct);
        }

        return (ProjectResourceId)context.Entry(resource).Property("Id").CurrentValue!;
    }

    private async Task<ProjectId> SeedProjectAsync(string shortName, bool isDeleted = false, DateTimeOffset? startDate = null, DateTimeOffset? endDate = null)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var project = ProjectAggregate.Create(
            new ProjectShortName(shortName),
            ProjectName.Create(shortName),
            ProjectDomainType.FromName(DomainTypeName.Create($"domain-{shortName}")),
            UserId.New(),
            startDate ?? Start,
            endDate ?? End,
            ServiceName.Default);
        if (isDeleted)
        {
            Assert.True(project.Delete().IsSuccess);
        }

        context.Add(project);

        // FromName yields an unresolved ProjectDomainType with the sentinel key -1; give each a distinct
        // key so two seeded projects do not collide (the FK is required, so detaching is not an option).
        foreach (var domainTypeEntry in context.ChangeTracker.Entries<ProjectDomainType>().ToList())
        {
            var key = domainTypeEntry.Metadata.FindPrimaryKey()!.Properties[0].Name;
            domainTypeEntry.Property(key).CurrentValue = new ProjectDomainTypeId(++_domainTypeKeySeed);
        }

        await SaveAsync(context, ct);

        return (ProjectId)context.Entry(project).Property("Id").CurrentValue!;
    }

    private async Task LinkAsync(ProjectId projectId, ProjectResourceId resourceId, bool isDeleted = false)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var link = AssignedProjectResourceToProject.Create();
        if (isDeleted)
        {
            link.Delete();
        }

        context.Add(link);
        context.Entry(link).Property("ProjectId").CurrentValue = projectId;
        context.Entry(link).Property("ProjectResourceId").CurrentValue = resourceId;
        await SaveAsync(context, ct);
    }

    // SQLite cannot evaluate the SQL-Server-only CreatedAt default (SYSUTCDATETIME()) this model
    // reproduces for legacy-schema parity with production; the domain factories never set the shadow
    // CreatedAt themselves (they rely on the DB default), so stamp it explicitly for every newly added
    // row here or SaveChanges asks SQLite to run a function it does not have.
    private static async Task SaveAsync(DatabaseContext context, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added || entry.Metadata.FindProperty("CreatedAt") is null)
                continue;

            // The shadow "CreatedAt" backing type differs per entity (Project uses DateTimeOffset,
            // Provider/ProjectResource use DateTime) — match whichever is left at its unset CLR default.
            var current = entry.Property("CreatedAt").CurrentValue;
            entry.Property("CreatedAt").CurrentValue = current switch
            {
                DateTime { Ticks: 0 } => now.UtcDateTime,
                DateTimeOffset { Ticks: 0 } => now,
                _ => current,
            };
        }

        await context.SaveChangesAsync(ct);
    }
}
