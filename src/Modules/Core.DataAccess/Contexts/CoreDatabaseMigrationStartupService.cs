using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Core.DataAccess.Contexts;

/// <summary>
/// Checks (and optionally applies) pending EF Core migrations for the modular <see cref="DatabaseContext"/>
/// at application startup. Controlled by the <c>Database:AutoMigrateOnStartup</c> configuration key
/// (default off — the DatabaseMigrations tool owns applying). Throws if any migration stays pending, so the
/// API refuses to start against a schema that has not had the modular migration set applied.
/// Replaces the legacy <c>UserOrganizationManagement</c> DatabaseMigrationStartupService (ADR-0049 cutover).
/// </summary>
internal sealed class CoreDatabaseMigrationStartupService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<CoreDatabaseMigrationStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        if (configuration.GetValue<bool>("Database:AutoMigrateOnStartup", false))
        {
            logger.LogInformation("Applying pending migrations for {Context}...", nameof(DatabaseContext));
            await ctx.Database.MigrateAsync(cancellationToken);
        }

        var pending = (await ctx.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            var msg = $"{nameof(DatabaseContext)} has {pending.Count} pending migration(s): {string.Join(", ", pending)}";
            logger.LogCritical("{Message}", msg);
            throw new InvalidOperationException(msg);
        }

        logger.LogInformation("{Context} migration check passed.", nameof(DatabaseContext));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}