using Core.DataAccess;
using Core.DataAccess.Contexts;
using Core.DataAccess.Events;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;

using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProjectAggregate = Core.Domain.Project.Project;
using ProjectTagAggregate = Core.Domain.Project.ProjectTag;

namespace Core.UnitTests.ProjectTags;

/// <summary>
/// Tests for <see cref="ProjectTagDetailsQuery.IsTagAssignedToAnyProjectAsync"/> (F1 Phase B) — the
/// block-if-in-use gate behind the catalogue delete. Traverses the Project↔ProjectTag M:N skip navigation
/// over the EF in-memory provider; the Project soft-delete query filter excludes inactive projects.
/// </summary>
public sealed class ProjectTagInUseQueryTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _serviceProvider;
    private int _domainTypeKeySeed = 9000;

    public ProjectTagInUseQueryTests()
    {
        var dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DomainEventChannel>();
        services.AddScoped<IRoleService, UserDatabaseAccessTests.TestRoleService>();
        services.Configure<WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped(typeof(IDataQuery<>), typeof(DataQuery<>));
        services.AddScoped<IProjectTagQuery, ProjectTagDetailsQuery>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_ReturnFalse_When_TagNotAssigned()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProjectAsync("proj-none");

        Assert.False(await QueryAsync(q => q.IsTagAssignedToAnyProjectAsync(TagName.Create("hpc"), ct)).ConfigureAwait(false));
    }

    [Fact]
    public async Task Should_ReturnTrue_When_TagAssignedToActiveProject()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProjectWithTagAsync("proj-tagged", "hpc", tagDeleted: false);

        Assert.True(await QueryAsync(q => q.IsTagAssignedToAnyProjectAsync(TagName.Create("hpc"), ct)).ConfigureAwait(false));
    }

    [Fact]
    public async Task Should_ReturnFalse_When_OnlyTagSoftDeleted()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProjectWithTagAsync("proj-deltag", "hpc", tagDeleted: true);

        Assert.False(await QueryAsync(q => q.IsTagAssignedToAnyProjectAsync(TagName.Create("hpc"), ct)).ConfigureAwait(false));
    }

    [Fact]
    public async Task Should_ReturnFalse_When_OnlyAssignedToDeletedProject()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProjectWithTagAsync("proj-gone", "hpc", tagDeleted: false, projectDeleted: true);

        Assert.False(await QueryAsync(q => q.IsTagAssignedToAnyProjectAsync(TagName.Create("hpc"), ct)).ConfigureAwait(false));
    }

    private async Task<bool> QueryAsync(Func<IProjectTagQuery, Task<bool>> act)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IProjectTagQuery>();
        return await act(query);
    }

    private async Task SeedProjectAsync(string shortName) =>
        await SeedProjectWithTagAsync(shortName, tagName: null, tagDeleted: false);

    private async Task SeedProjectWithTagAsync(string shortName, string? tagName, bool tagDeleted, bool projectDeleted = false)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var project = ProjectAggregate.Create(
            new ProjectShortName(shortName),
            ProjectName.Create(shortName),
            ProjectDomainType.FromName(DomainTypeName.Create($"domain-{shortName}")),
            UserId.New(),
            Start,
            End,
            ServiceName.Default);

        if (tagName is not null)
        {
            var tag = ProjectTagAggregate.Create(TagName.Create(tagName), UserName.Create("author"));
            if (tagDeleted)
            {
                Assert.True(tag.Delete().IsSuccess);
            }

            Assert.True(project.AssignExistingTag(tag).IsSuccess);
        }

        if (projectDeleted)
        {
            Assert.True(project.Delete().IsSuccess);
        }

        context.Add(project);

        // FromName yields an unresolved ProjectDomainType with sentinel key -1; give each a distinct key so
        // seeding several projects does not collide on the FK.
        foreach (var domainTypeEntry in context.ChangeTracker.Entries<ProjectDomainType>().ToList())
        {
            var key = domainTypeEntry.Metadata.FindPrimaryKey()!.Properties[0].Name;
            domainTypeEntry.Property(key).CurrentValue = new ProjectDomainTypeId(++_domainTypeKeySeed);
        }

        // ProjectTag.Id is a ValueGeneratedNever shadow PK and CreatedByUserId is a required shadow FK; the
        // handler sets both at mint time, so fill them here the same way before saving.
        foreach (var tagEntry in context.ChangeTracker.Entries<ProjectTagAggregate>().ToList())
        {
            tagEntry.Property("Id").CurrentValue = Guid.CreateVersion7();
            tagEntry.Property("CreatedByUserId").CurrentValue = UserId.New();
        }

        await context.SaveChangesAsync(ct);
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();
}

/// <summary>
/// Real-SQL translation guard for the tag in-use query (F1 Phase B). The Project↔ProjectTag skip-navigation
/// SelectMany is the new SQL in F1; EF in-memory cannot prove it translates. Set <see cref="IntegrationDb.EnvVar"/>
/// to run; otherwise it skips.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProjectTagInUseRealSqlTests : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public ProjectTagInUseRealSqlTests()
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
        var query = scope.ServiceProvider.GetRequiredService<IProjectTagQuery>();

        // The failure mode is a runtime translation throw, not a wrong result; assert only that the
        // skip-navigation SelectMany translates to SQL and executes for an arbitrary (absent) tag.
        var result = await query.IsTagAssignedToAnyProjectAsync(TagName.Create("no-such-tag-f1"), ct);

        Assert.False(result);
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();
}
