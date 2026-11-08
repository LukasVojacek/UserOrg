using Core.DataAccess;
using Core.DataAccess.Contexts;
using Core.DataAccess.Events;

using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ProjectTagAggregate = Core.Domain.Project.ProjectTag;
using UserAggregate = Core.Domain.User.User;

namespace Core.UnitTests.ProjectTags;

/// <summary>
/// Characterization tests (issue #249, P1.4 audit risky spot 1) for the DatabaseContext-level tag
/// resolve/persist contract that <c>IRepository&lt;ProjectTag, TagName&gt;</c> sits on top of (and that the
/// former resolve-or-create tag handler port, deleted in block B02, previously did too). Pins only behaviour
/// that is invariant across the P1.4 refactor (ADR-0078): <c>FindAsync</c> excludes soft-deleted rows; the
/// EF shadow-property mechanism used to mint the <c>Id</c> surrogate PK and the <c>CreatedByUserId</c>
/// shadow FK persists correctly; and a User lookup by <c>UserName</c> for an unknown name returns null (the
/// precondition the ensure-create "missing creator" path checks before throwing). The revive-on-create /
/// 404-on-assign soft-delete behaviour changes are out of scope here (covered by the block's own
/// RED-first tests).
/// </summary>
public sealed class ProjectTagRepositoryDatabaseContextTests : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public ProjectTagRepositoryDatabaseContextTests()
    {
        var dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DomainEventChannel>();
        services.AddScoped<IRoleService, UserDatabaseAccessTests.TestRoleService>();
        services.Configure<WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(options => options.UseInMemoryDatabase(dbName));

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_ReturnActiveTag_When_FindAsyncCalledOnExistingTag()
    {
        var ct = TestContext.Current.CancellationToken;
        var creatorId = await SeedUserAsync("author");
        await SeedTagAsync("hpc", creatorId, deleted: false);

        var found = await FindAsync("hpc", ct);

        Assert.NotNull(found);
        Assert.Equal(TagName.Create("hpc"), found!.Name);
    }

    [Fact]
    public async Task Should_ReturnNull_When_FindAsyncCalledOnSoftDeletedTag()
    {
        var ct = TestContext.Current.CancellationToken;
        var creatorId = await SeedUserAsync("author2");
        await SeedTagAsync("deleted-tag", creatorId, deleted: true);

        var found = await FindAsync("deleted-tag", ct);

        Assert.Null(found);
    }

    [Fact]
    public async Task Should_ReturnNull_When_FindAsyncCalledOnUnknownName()
    {
        var ct = TestContext.Current.CancellationToken;

        var found = await FindAsync("no-such-tag", ct);

        Assert.Null(found);
    }

    [Fact]
    public async Task Should_PersistShadowIdAndCreatedByUserId_When_TagAddedWithResolvedCreator()
    {
        var ct = TestContext.Current.CancellationToken;
        var creatorId = await SeedUserAsync("author3");

        Guid mintedId;
        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var repo = (IRepository<ProjectTagAggregate, TagName>)context;
            var tag = ProjectTagAggregate.Create(TagName.Create("shadow-fk"), UserName.Create("author3"));

            var added = await repo.AddAsync(tag, ct);

            // Mirrors today's ensure-create shadow-FK resolution (DatabaseContext.ProjectTagHandler):
            // Id minted, CreatedByUserId set to the surrogate id of the creator resolved by UserName.
            mintedId = Guid.CreateVersion7();
            context.Entry(added!).Property("Id").CurrentValue = mintedId;
            context.Entry(added!).Property("CreatedByUserId").CurrentValue = creatorId;
            await context.SaveChangesAsync(ct);
        }

        await using var verifyScope = _serviceProvider.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var persistedEntry = await verifyContext.Set<ProjectTagAggregate>()
            .FirstOrDefaultAsync(t => t.Name == TagName.Create("shadow-fk"), ct);

        Assert.NotNull(persistedEntry);
        Assert.Equal(mintedId, verifyContext.Entry(persistedEntry!).Property("Id").CurrentValue);
        Assert.Equal(creatorId, verifyContext.Entry(persistedEntry!).Property<UserId>("CreatedByUserId").CurrentValue);
    }

    [Fact]
    public async Task Should_ReturnNull_When_ResolvingCreatorByUnknownUserName()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        // Precondition the ensure-create "missing creator" path checks before throwing
        // InvalidOperationException (DatabaseContext.ProjectTagHandler.EnsureTagExistsAsync).
        var creator = await context.Set<UserAggregate>()
            .FirstOrDefaultAsync(u => u.UserName == UserName.Create("no-such-user"), ct);

        Assert.Null(creator);
    }

    [Fact]
    public async Task Should_RestoreAndReturnExisting_When_AddAsyncOnSoftDeletedName()
    {
        var ct = TestContext.Current.CancellationToken;
        var creatorId = await SeedUserAsync("reviver");
        await SeedTagAsync("revive-me", creatorId, deleted: true);

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var repo = (IRepository<ProjectTagAggregate, TagName>)context;
            var candidate = ProjectTagAggregate.Create(TagName.Create("revive-me"), UserName.Create("reviver"));

            var returned = await repo.AddAsync(candidate, ct);

            Assert.NotNull(returned);
            Assert.False(returned!.IsDeleted);
            await context.SaveChangesAsync(ct);
        }

        await using var verifyScope = _serviceProvider.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var rows = await verifyContext.Set<ProjectTagAggregate>()
            .IgnoreQueryFilters()
            .Where(t => t.Name == TagName.Create("revive-me"))
            .ToListAsync(ct);

        Assert.Single(rows);
        Assert.False(rows[0].IsDeleted);
    }

    [Fact]
    public async Task Should_MintShadowStateFromAggregate_When_AddAsyncOnGenuineNewName()
    {
        var ct = TestContext.Current.CancellationToken;
        var creatorId = await SeedUserAsync("minter");

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var repo = (IRepository<ProjectTagAggregate, TagName>)context;
            var candidate = ProjectTagAggregate.Create(TagName.Create("fresh-tag"), UserName.Create("minter"));

            var returned = await repo.AddAsync(candidate, ct);

            Assert.NotNull(returned);
            Assert.NotEqual(Guid.Empty, (Guid)context.Entry(returned!).Property("Id").CurrentValue!);
            Assert.Equal(creatorId, context.Entry(returned!).Property<UserId>("CreatedByUserId").CurrentValue);
            await context.SaveChangesAsync(ct);
        }

        await using var verifyScope = _serviceProvider.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var persisted = await verifyContext.Set<ProjectTagAggregate>()
            .FirstOrDefaultAsync(t => t.Name == TagName.Create("fresh-tag"), ct);

        Assert.NotNull(persisted);
        Assert.Equal(creatorId, verifyContext.Entry(persisted!).Property<UserId>("CreatedByUserId").CurrentValue);
    }

    [Fact]
    public async Task Should_Throw_When_AddAsyncGenuineNewNameCreatorUnknown()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var repo = (IRepository<ProjectTagAggregate, TagName>)context;
        var candidate = ProjectTagAggregate.Create(TagName.Create("orphan-tag"), UserName.Create("ghost"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await repo.AddAsync(candidate, ct));
    }

    private async Task<ProjectTagAggregate?> FindAsync(string name, CancellationToken ct)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var repo = (IRepository<ProjectTagAggregate, TagName>)context;
        return await repo.FindAsync(TagName.Create(name), ct);
    }

    private async Task<UserId> SeedUserAsync(string idp)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var userId = UserId.From(Guid.NewGuid());
        var user = UserAggregate.CreateUser(Email.Create($"{idp}@example.com"), UserName.Create(idp), IdpId.Create(idp));
        context.Add(user);
        context.Entry(user).Property("Id").CurrentValue = userId;
        await context.SaveChangesAsync(ct);
        return userId;
    }

    private async Task SeedTagAsync(string name, UserId creatorId, bool deleted)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var tag = ProjectTagAggregate.Create(TagName.Create(name), UserName.Create("author"));
        if (deleted)
        {
            Assert.True(tag.Delete().IsSuccess);
        }

        var entry = context.Add(tag);
        entry.Property("Id").CurrentValue = Guid.CreateVersion7();
        entry.Property("CreatedByUserId").CurrentValue = creatorId;
        await context.SaveChangesAsync(ct);
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();
}
