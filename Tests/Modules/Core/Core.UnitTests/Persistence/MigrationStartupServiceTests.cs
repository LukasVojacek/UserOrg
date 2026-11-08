using Core.DataAccess.Contexts;
using Core.DataAccess.Events;
using Core.DataAccess.Outbox;
using Core.DataAccess.ResourceRequests;
using Core.Domain.Shared;

using Forge.Core.Features.Role.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Core.UnitTests.Persistence;

/// <summary>
/// Characterization tests for the three Core.DataAccess migration startup services
/// (<see cref="CoreDatabaseMigrationStartupService"/>, <see cref="OutboxMigrationStartupService"/> and
/// <see cref="ResourceRequestsMigrationStartupService"/>) on an in-memory SQLite database. The migrations are
/// SQL Server migrations and cannot run on SQLite, so the tests drive the check through the migration history
/// table: an empty history leaves every migration pending, and a history listing every migration leaves none,
/// which also makes the <c>Database:AutoMigrateOnStartup</c> apply a no-op.
/// </summary>
public sealed class MigrationStartupServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public static TheoryData<string> Contexts => [nameof(DatabaseContext), nameof(OutboxDbContext), nameof(ResourceRequestsDbContext)];

    [Theory]
    [MemberData(nameof(Contexts))]
    public async Task StartAsync_throws_naming_every_pending_migration_when_none_is_applied(string contextName)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var harness = await OpenAsync(contextName, autoMigrate: false, ct);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.StartAsync(ct));

        var migrations = harness.AllMigrations;
        Assert.NotEmpty(migrations);
        Assert.Equal(
            $"{contextName} has {migrations.Count} pending migration(s): {string.Join(", ", migrations)}",
            ex.Message);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public async Task StartAsync_names_only_the_migrations_missing_from_the_history(string contextName)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var harness = await OpenAsync(contextName, autoMigrate: false, ct);
        var last = harness.AllMigrations[^1];
        await harness.RecordAppliedAsync(harness.AllMigrations.SkipLast(1), ct);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.StartAsync(ct));

        Assert.Equal($"{contextName} has 1 pending migration(s): {last}", ex.Message);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public async Task StartAsync_passes_when_every_migration_is_applied(string contextName)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var harness = await OpenAsync(contextName, autoMigrate: false, ct);
        await harness.RecordAppliedAsync(harness.AllMigrations, ct);

        await harness.Service.StartAsync(ct);
        await harness.Service.StopAsync(ct);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public async Task StartAsync_with_auto_migrate_applies_nothing_and_passes_when_every_migration_is_applied(string contextName)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var harness = await OpenAsync(contextName, autoMigrate: true, ct);
        await harness.RecordAppliedAsync(harness.AllMigrations, ct);

        await harness.Service.StartAsync(ct);

        Assert.Equal(harness.AllMigrations.Count, await harness.CountHistoryRowsAsync(ct));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private async Task<Harness> OpenAsync(string contextName, bool autoMigrate, CancellationToken ct)
    {
        await _connection.OpenAsync(ct);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:AutoMigrateOnStartup"] = autoMigrate.ToString() })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DomainEventChannel>();
        services.AddScoped<IRoleService, UserDatabaseAccessTests.TestRoleService>();
        services.Configure<WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(Sqlite);
        services.AddDbContextFactory<OutboxDbContext>(Sqlite);
        services.AddDbContextFactory<ResourceRequestsDbContext>(Sqlite);
        var provider = services.BuildServiceProvider();

        IHostedService service = contextName switch
        {
            nameof(DatabaseContext) => new CoreDatabaseMigrationStartupService(
                provider.GetRequiredService<IServiceScopeFactory>(), configuration, NullLogger<CoreDatabaseMigrationStartupService>.Instance),
            nameof(OutboxDbContext) => new OutboxMigrationStartupService(
                provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>(), configuration, NullLogger<OutboxMigrationStartupService>.Instance),
            nameof(ResourceRequestsDbContext) => new ResourceRequestsMigrationStartupService(
                provider.GetRequiredService<IDbContextFactory<ResourceRequestsDbContext>>(), configuration, NullLogger<ResourceRequestsMigrationStartupService>.Instance),
            _ => throw new ArgumentOutOfRangeException(nameof(contextName), contextName, null),
        };

        Type contextType = contextName switch
        {
            nameof(DatabaseContext) => typeof(DatabaseContext),
            nameof(OutboxDbContext) => typeof(OutboxDbContext),
            _ => typeof(ResourceRequestsDbContext),
        };

        return new Harness(provider, contextType, service);
    }

    private void Sqlite(DbContextOptionsBuilder options) =>
        options
            .UseSqlite(_connection)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

    private sealed class Harness(ServiceProvider provider, Type contextType, IHostedService service) : IAsyncDisposable
    {
        internal IHostedService Service { get; } = service;

        internal IReadOnlyList<string> AllMigrations { get; } = WithContext(provider, contextType, ctx => ctx.Database.GetMigrations().ToList());

        internal async Task RecordAppliedAsync(IEnumerable<string> migrationIds, CancellationToken ct)
        {
            await using var scope = provider.CreateAsyncScope();
            var ctx = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            await ctx.Database.ExecuteSqlRawAsync(
                """CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" ("MigrationId" TEXT NOT NULL PRIMARY KEY, "ProductVersion" TEXT NOT NULL);""",
                ct);
            foreach (var id in migrationIds)
            {
                await ctx.Database.ExecuteSqlRawAsync(
                    """INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ({0}, '10.0.0');""",
                    [id],
                    ct);
            }
        }

        internal async Task<int> CountHistoryRowsAsync(CancellationToken ct)
        {
            await using var scope = provider.CreateAsyncScope();
            var ctx = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            return await ctx.Database.SqlQueryRaw<int>("""SELECT COUNT(*) AS "Value" FROM "__EFMigrationsHistory" """).SingleAsync(ct);
        }

        public ValueTask DisposeAsync() => provider.DisposeAsync();

        private static T WithContext<T>(ServiceProvider provider, Type contextType, Func<DbContext, T> read)
        {
            using var scope = provider.CreateScope();
            return read((DbContext)scope.ServiceProvider.GetRequiredService(contextType));
        }
    }
}
