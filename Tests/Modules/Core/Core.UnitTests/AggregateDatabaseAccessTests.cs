using Core.DataAccess;
using Core.DataAccess.Contexts;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProjectAggregate = Core.Domain.Project.Project;
using ProviderAggregate = Core.Domain.Provider.Provider;
using RequestAggregate = Core.Domain.Request.Request;
using UserAggregate = Core.Domain.User.User;

namespace Core.UnitTests;

/// <summary>
/// Integration smoke tests that hit a real database to verify the DDD aggregate mappings load.
/// Set <see cref="IntegrationDb.EnvVar"/> to a SQL Server connection string to run them; otherwise
/// they skip. Tagged Integration so they are excluded from the default unit-test run.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProjectDatabaseAccessTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _serviceProvider;

    public ProjectDatabaseAccessTests() => _serviceProvider = BuildServiceProvider();

    [Fact]
    public async Task Should_LoadProjectAggregate_When_PresentInDb()
    {
        IntegrationDb.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var token = Guid.NewGuid().ToString("N");
        ProjectShortName shortName = new($"t092-{token}");
        var tagName = TagName.Create($"t092-{token}");
        var tagId = Guid.CreateVersion7();
        var seed = new ReferenceSeed(token);

        try
        {
            await using (var scope = _serviceProvider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
                var (createdBy, domainType) = await SeedReferenceDataAsync(context, seed, ct);

                var seeded = ProjectAggregate.Create(
                    shortName,
                    ProjectName.Create(shortName.Value),
                    domainType,
                    createdBy,
                    Start,
                    End,
                    ServiceName.Default);

                var tag = ProjectTag.Create(tagName, UserName.Create($"t092-{token}"));
                Assert.True(seeded.AssignExistingTag(tag).IsSuccess);

                context.Add(seeded);

                // ProjectTag.Id is a ValueGeneratedNever shadow PK and CreatedByUserId a required shadow FK;
                // the production handler sets both at mint time, so the fixture has to do the same.
                var tagEntry = context.Entry(tag);
                tagEntry.Property("Id").CurrentValue = tagId;
                tagEntry.Property("CreatedByUserId").CurrentValue = createdBy;

                await context.SaveChangesAsync(ct);
            }

            await using (var scope = _serviceProvider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
                var project = await context.Set<ProjectAggregate>()
                    .Include("_projectTags")
                    .Include("_assignedUsers.User")
                    .Include("_assignedResources")
                    .Include("_projectDomainType")
                    .FirstOrDefaultAsync(x => x.ShortName == shortName, ct);

                Assert.NotNull(project);
                Assert.False(string.IsNullOrEmpty(project.ShortName.Value));
                Assert.False(string.IsNullOrEmpty(project.Name.Value));
                Assert.NotEqual(Guid.Empty, project.CreatedBy.Value);
                Assert.NotNull(project.Tags);
                Assert.Contains(tagName, project.Tags);
                Assert.NotNull(project.Users);
                Assert.NotNull(project.Resources);
            }
        }
        finally
        {
            await CleanupAsync(shortName, tagId, seed, ct);
        }
    }

    [Fact]
    public async Task Should_ExcludeSoftDeletedRows_When_ProjectQueryFilterApplied()
    {
        IntegrationDb.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var seed = new ReferenceSeed(Guid.NewGuid().ToString("N"));

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        try
        {
            var userId = await seed.SeedUserAsync(context, ct);
            var (domainTypeId, _) = await seed.SeedLookupAsync(context, "ProjectDomainType", ct);
            await seed.SeedProjectAsync(context, userId, domainTypeId, ct, "-visible");
            var deletedId = await seed.SeedProjectAsync(context, userId, domainTypeId, ct, "-deleted");
            await seed.SoftDeleteAsync(context, "Project", deletedId, ct);

            ProjectShortName visible = new(seed.ProjectShortNameFor("-visible"));
            ProjectShortName deleted = new(seed.ProjectShortNameFor("-deleted"));

            // why: the counts are scoped to this test's own two rows, so a fixture seeding or deleting
            // why: Project rows in a parallel class cannot move either number
            var totalCount = await context.Set<ProjectAggregate>().IgnoreQueryFilters()
                .Where(p => p.ShortName == visible || p.ShortName == deleted).CountAsync(ct);
            var activeCount = await context.Set<ProjectAggregate>()
                .Where(p => p.ShortName == visible || p.ShortName == deleted).CountAsync(ct);

            Assert.Equal(2, totalCount);
            Assert.Equal(1, activeCount);
        }
        finally
        {
            await seed.DeleteSeededAsync(context);
        }
    }

    // Every FK in this schema is NO ACTION, so the fixture graph comes out in dependency order: the
    // many-to-many join row, then the tag, then the project itself.
    private async Task CleanupAsync(ProjectShortName shortName, Guid tagId, ReferenceSeed seed, CancellationToken ct)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        await context.Database.ExecuteSqlAsync(
            $"DELETE FROM [AssignedProjectTagToProject] WHERE [ProjectTagsId] = {tagId}", ct);
        await context.Database.ExecuteSqlAsync($"DELETE FROM [ProjectTag] WHERE [Id] = {tagId}", ct);
        await context.Set<ProjectAggregate>().IgnoreQueryFilters()
            .Where(p => p.ShortName == shortName)
            .ExecuteDeleteAsync(ct);
        await seed.DeleteSeededAsync(context);
    }

    // why: a database migrated from zero carries no User and no ProjectDomainType row, so reading what is
    // why: there would turn this test into a vacuous skip; what the fixture seeds, the fixture deletes
    private static async Task<(UserId CreatedBy, ProjectDomainType DomainType)> SeedReferenceDataAsync(
        DatabaseContext context, ReferenceSeed seed, CancellationToken ct)
    {
        var createdBy = await seed.SeedUserAsync(context, ct);
        var (_, domainTypeName) = await seed.SeedLookupAsync(context, "ProjectDomainType", ct);

        // why: ProjectDomainType.Id is internal, so the row seeded above comes back by its own name — an
        // why: unfiltered read can return a parallel fixture's row, which that fixture then deletes
        var seededName = DomainTypeName.Create(domainTypeName);
        var domainType = await context.Set<ProjectDomainType>().FirstAsync(d => d.Name == seededName, ct);

        return (UserId.Create(createdBy), domainType);
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();

    private static ServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:UserOrgDBContext"] = IntegrationDb.ConnectionStringOrPlaceholder
            })
            .Build();

        var services = IntegrationDb.NewServices();
        services.AddCoreDataAccess(configuration);
        return services.BuildServiceProvider();
    }
}

[Trait("Category", "Integration")]
public sealed class ProviderDatabaseAccessTests : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public ProviderDatabaseAccessTests() => _serviceProvider = BuildServiceProvider();

    [Fact]
    public async Task Should_LoadProviderAggregate_When_PresentInDb()
    {
        IntegrationDb.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var seed = new ReferenceSeed(Guid.NewGuid().ToString("N"));

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        try
        {
            // why: a database migrated from zero carries no Provider row, so the fixture seeds the one it
            // why: reads back — an unfiltered read can return a parallel fixture's row, which it then deletes
            var (_, providerName) = await seed.SeedProviderAsync(context, ct);

            var provider = await context.Set<ProviderAggregate>()
                .FirstOrDefaultAsync(p => p.Name == ProviderName.From(providerName), ct);

            Assert.NotNull(provider);
            Assert.False(string.IsNullOrEmpty(provider.Name.Value));
        }
        finally
        {
            await seed.DeleteSeededAsync(context);
        }
    }

    [Fact]
    public async Task Should_ExcludeSoftDeletedRows_When_ProviderQueryFilterApplied()
    {
        IntegrationDb.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var seed = new ReferenceSeed(Guid.NewGuid().ToString("N"));

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        try
        {
            var (_, visibleName) = await seed.SeedProviderAsync(context, ct, "-visible");
            var (deletedId, deletedName) = await seed.SeedProviderAsync(context, ct, "-deleted");
            await seed.SoftDeleteAsync(context, "Provider", deletedId, ct);

            var visible = ProviderName.From(visibleName);
            var deleted = ProviderName.From(deletedName);

            // why: the counts are scoped to this test's own two rows, so a fixture seeding or deleting
            // why: Provider rows in a parallel class cannot move either number
            var totalCount = await context.Set<ProviderAggregate>().IgnoreQueryFilters()
                .Where(p => p.Name == visible || p.Name == deleted).CountAsync(ct);
            var activeCount = await context.Set<ProviderAggregate>()
                .Where(p => p.Name == visible || p.Name == deleted).CountAsync(ct);

            Assert.Equal(2, totalCount);
            Assert.Equal(1, activeCount);
        }
        finally
        {
            await seed.DeleteSeededAsync(context);
        }
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();

    private static ServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:UserOrgDBContext"] = IntegrationDb.ConnectionStringOrPlaceholder
            })
            .Build();

        var services = IntegrationDb.NewServices();
        services.AddCoreDataAccess(configuration);
        return services.BuildServiceProvider();
    }
}

[Trait("Category", "Integration")]
public sealed class RequestDatabaseAccessTests : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public RequestDatabaseAccessTests() => _serviceProvider = BuildServiceProvider();

    [Fact]
    public async Task Should_LoadRequestAggregate_When_PresentInDb()
    {
        IntegrationDb.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var token = Guid.NewGuid().ToString("N");
        var seed = new ReferenceSeed(token);

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        try
        {
            var userId = await seed.SeedUserAsync(context, ct);
            var (domainTypeId, _) = await seed.SeedLookupAsync(context, "ProjectDomainType", ct);
            var projectId = await seed.SeedProjectAsync(context, userId, domainTypeId, ct);
            var requestId = await seed.SeedRequestAsync(context, userId, projectId, ct);

            // why: ContactEmail is a non-nullable struct in the domain but a nullable column for legacy rows,
            // why: and EF Core 10 throws on NULL before the converter runs — the fixture's own row has a value
            var request = await context.Set<RequestAggregate>()
                .FromSqlRaw("SELECT * FROM [Request] WHERE [Id] = @p0", requestId)
                .FirstOrDefaultAsync(ct);

            Assert.NotNull(request);
            Assert.NotEqual(Guid.Empty, request.ProjectId.Value);
            Assert.NotEqual(Guid.Empty, request.UserId.Value);
        }
        finally
        {
            await seed.DeleteSeededAsync(context);
        }
    }

    [Fact]
    public async Task Should_ExcludeSoftDeletedRows_When_RequestQueryFilterApplied()
    {
        IntegrationDb.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var seed = new ReferenceSeed(Guid.NewGuid().ToString("N"));

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        try
        {
            var userId = await seed.SeedUserAsync(context, ct);
            var (domainTypeId, _) = await seed.SeedLookupAsync(context, "ProjectDomainType", ct);
            var projectId = await seed.SeedProjectAsync(context, userId, domainTypeId, ct);
            await seed.SeedRequestAsync(context, userId, projectId, ct, "-visible");
            var deletedId = await seed.SeedRequestAsync(context, userId, projectId, ct, "-deleted");
            await seed.SoftDeleteAsync(context, "Request", deletedId, ct);

            var visible = seed.RequestSlugFor("-visible");
            var deleted = seed.RequestSlugFor("-deleted");

            // why: the counts are scoped to this test's own two rows, so a fixture seeding or deleting
            // why: Request rows in a parallel class cannot move either number
            var totalCount = await context.Set<RequestAggregate>().IgnoreQueryFilters()
                .Where(r => r.Slug == visible || r.Slug == deleted).CountAsync(ct);
            var activeCount = await context.Set<RequestAggregate>()
                .Where(r => r.Slug == visible || r.Slug == deleted).CountAsync(ct);

            Assert.Equal(2, totalCount);
            Assert.Equal(1, activeCount);
        }
        finally
        {
            await seed.DeleteSeededAsync(context);
        }
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();

    private static ServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:UserOrgDBContext"] = IntegrationDb.ConnectionStringOrPlaceholder
            })
            .Build();

        var services = IntegrationDb.NewServices();
        services.AddCoreDataAccess(configuration);
        return services.BuildServiceProvider();
    }
}
