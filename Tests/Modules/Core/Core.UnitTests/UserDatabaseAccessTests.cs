using Core.DataAccess;
using Core.DataAccess.Contexts;
using Core.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using UserAggregate = Core.Domain.User.User;

namespace Core.UnitTests;

/// <summary>
/// Integration tests that hit a real database to verify the refactored DDD mappings. Set
/// <see cref="IntegrationDb.EnvVar"/> to a SQL Server connection string to run them; otherwise they skip.
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class UserDatabaseAccessTests : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;
    public UserDatabaseAccessTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:UserOrgDBContext"] = IntegrationDb.ConnectionStringOrPlaceholder
            })
            .Build();

        var services = IntegrationDb.NewServices();
        services.Configure<WellKnownRoles>(configuration);
        services.AddCoreDataAccess(configuration);
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_LoadUserWithRoleAndPreferences_When_PresentInDb()
    {
        IntegrationDb.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var token = Guid.NewGuid().ToString("N");
        var email = Email.Create($"t092-{token}@example.org");
        var userName = UserName.From($"t092-{token}@example.org");
        var idpId = IdpId.Create($"t092-idp-{token}");
        const string Preferences = """{"theme":"dark"}""";

        try
        {
            await using (var scope = _serviceProvider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var repository = uow.GetRepository<UserAggregate, UserName>();

                var seeded = await repository.AddAsync(UserAggregate.CreateUser(email, userName, idpId), ct);
                Assert.NotNull(seeded);

                // The preference child is keyed by the user's own id, which AddAsync stamps onto the
                // shadow PK; it is the only place the fixture can read it from.
                var userId = (UserId)context.Entry(seeded).Property("Id").CurrentValue!;
                Assert.True(seeded.UpdateUserPreferences(Preferences, userId).IsSuccess);

                await uow.SaveChangesAsync(ct);
            }

            await using (var scope = _serviceProvider.CreateAsyncScope())
            {
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var repository = uow.GetRepository<UserAggregate, UserName>();

                // FindAsync is the hydration path that loads _systemPermissions and _userPreference and
                // initializes the role map; SystemRoleName throws without both.
                var user = await repository.FindAsync(userName, ct);

                Assert.NotNull(user);
                Assert.Equal(email, user.Email);
                Assert.Equal(userName, user.UserName);
                Assert.False(string.IsNullOrEmpty(user.SystemRoleName));
                Assert.Equal("dark", user.UserPreferences.RootElement.GetProperty("theme").GetString());
            }
        }
        finally
        {
            await CleanupAsync(userName, ct);
        }
    }

    // FK_UserPermission_User_UserId and FK_UserPreference_User_UserId are both NO ACTION, so the children
    // go before the user row; a leaked fixture user would make a later run pass on stale data.
    private async Task CleanupAsync(UserName userName, CancellationToken ct)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var name = userName.Value;

        await context.Database.ExecuteSqlAsync(
            $"DELETE FROM [UserPermission] WHERE [UserId] IN (SELECT [Id] FROM [User] WHERE [UserName] = {name})", ct);
        await context.Database.ExecuteSqlAsync(
            $"DELETE FROM [UserPreference] WHERE [UserId] IN (SELECT [Id] FROM [User] WHERE [UserName] = {name})", ct);
        await context.Database.ExecuteSqlAsync($"DELETE FROM [User] WHERE [UserName] = {name}", ct);
    }

    [Fact]
    public async Task Should_ExcludeSoftDeletedRows_When_UserQueryFilterApplied()
    {
        IntegrationDb.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var seed = new ReferenceSeed(Guid.NewGuid().ToString("N"));

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        try
        {
            await seed.SeedUserAsync(context, ct, "-visible");
            var deletedId = await seed.SeedUserAsync(context, ct, "-deleted");
            await seed.SoftDeleteAsync(context, "User", deletedId, ct);

            var visible = UserName.From(seed.UserNameFor("-visible"));
            var deleted = UserName.From(seed.UserNameFor("-deleted"));

            // why: the counts are scoped to this test's own two rows, so a fixture seeding or deleting
            // why: User rows in a parallel class cannot move either number
            var totalCount = await context.Set<UserAggregate>().IgnoreQueryFilters()
                .Where(u => u.UserName == visible || u.UserName == deleted).CountAsync(ct);
            var activeCount = await context.Set<UserAggregate>()
                .Where(u => u.UserName == visible || u.UserName == deleted).CountAsync(ct);

            Assert.Equal(2, totalCount);
            Assert.Equal(1, activeCount);
        }
        finally
        {
            await seed.DeleteSeededAsync(context);
        }
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();
}
