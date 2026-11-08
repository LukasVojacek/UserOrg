using Forge.Core.Features.UserOrgRoles;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Forge.Core.Tests.Features.UserOrgRoles;

/// <summary>
/// Runs the raw permission SQL the Gateway builds its per-user permission snapshot from against a real
/// relational database, so the soft-delete predicates are exercised as SQL. A user deactivated by the
/// inactive-user sync (<c>[User].IsDeleted = 1</c>) keeps its role rows, so the query itself has to drop
/// them — otherwise a still-valid token passes the Gateway with the old permissions (#306).
/// </summary>
public sealed class UserOrgDbContextUserPermissionsTests
{
    private const string ActiveUser = "idp-active";
    private const string DeactivatedUser = "idp-deactivated";
    private static readonly Guid _projectId = Guid.Parse("6b2d9c1e-3f4a-4c5b-8d6e-7f8091a2b3c4");

    [Fact]
    public async Task Should_ReturnSystemAndProjectRoles_When_UserIsActive()
    {
        await using var database = await TestDatabase.CreateAsync();

        var permissions = await database.Context.GetUserPermissionsAsync(ActiveUser, TestContext.Current.CancellationToken);

        Assert.Equal(2, permissions.Count);
        Assert.Contains(permissions, p => p.ProjectId is null && p.SystemRoleId == 1);
        Assert.Contains(permissions, p => p.ProjectId == _projectId && p.ShortName == "PRJ1" && p.SystemRoleId == 3);
    }

    [Fact]
    public async Task Should_ReturnNoPermissions_When_UserIsSoftDeleted()
    {
        await using var database = await TestDatabase.CreateAsync();

        var permissions = await database.Context.GetUserPermissionsAsync(DeactivatedUser, TestContext.Current.CancellationToken);

        Assert.Empty(permissions);
    }

    [Fact]
    public async Task Should_ReturnNoPermissions_When_UserIsUnknown()
    {
        await using var database = await TestDatabase.CreateAsync();

        var permissions = await database.Context.GetUserPermissionsAsync("idp-nobody", TestContext.Current.CancellationToken);

        Assert.Empty(permissions);
    }

    /// <summary>
    /// SQLite with the three tables the query touches. The production SQL names <c>dbo.Project</c>, so an
    /// in-memory database is attached under the <c>dbo</c> alias to resolve that schema prefix.
    /// </summary>
    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestDatabase(SqliteConnection connection, UserOrgDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public UserOrgDbContext Context { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Filename=:memory:");
            await connection.OpenAsync();

            await using (var attach = connection.CreateCommand())
            {
                attach.CommandText = "attach database ':memory:' as dbo";
                await attach.ExecuteNonQueryAsync();
            }

            var options = new DbContextOptionsBuilder<UserOrgDbContext>().UseSqlite(connection).Options;
            var context = new UserOrgDbContext(options);
            await context.Database.ExecuteSqlRawAsync(
                """
                create table [User] (Id text not null primary key, IdentityProviderUserId text not null,
                                     IsDeleted integer not null default 0);
                create table UserPermission (Id text not null primary key, UserId text not null, ProjectId text null,
                                             SystemRoleId integer not null, IsDeleted integer not null default 0);
                create table dbo.Project (Id text not null primary key, ShortName text not null,
                                          IsDeleted integer not null default 0);
                """);
            await context.Database.ExecuteSqlRawAsync(
                "insert into dbo.Project (Id, ShortName, IsDeleted) values ({0}, {1}, 0)", _projectId.ToString(), "PRJ1");

            var database = new TestDatabase(connection, context);
            await database.AddUserWithRolesAsync(ActiveUser, isDeleted: false);
            await database.AddUserWithRolesAsync(DeactivatedUser, isDeleted: true);
            return database;
        }

        private async Task AddUserWithRolesAsync(string idpUserId, bool isDeleted)
        {
            var userId = Guid.NewGuid().ToString();
            await Context.Database.ExecuteSqlRawAsync(
                "insert into [User] (Id, IdentityProviderUserId, IsDeleted) values ({0}, {1}, {2})",
                userId, idpUserId, isDeleted ? 1 : 0);
            await Context.Database.ExecuteSqlRawAsync(
                "insert into UserPermission (Id, UserId, ProjectId, SystemRoleId, IsDeleted) values ({0}, {1}, null, 1, 0)",
                Guid.NewGuid().ToString(), userId);
            await Context.Database.ExecuteSqlRawAsync(
                "insert into UserPermission (Id, UserId, ProjectId, SystemRoleId, IsDeleted) values ({0}, {1}, {2}, 3, 0)",
                Guid.NewGuid().ToString(), userId, _projectId.ToString());
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
