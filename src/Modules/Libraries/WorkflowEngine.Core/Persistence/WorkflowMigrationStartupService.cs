using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WorkflowEngine.Core.Persistence;

/// <summary>
/// Checks (and optionally applies) pending EF Core migrations for <see cref="WorkflowDbContext"/> at application startup.
/// Controlled by the <c>Database:AutoMigrateOnStartup</c> configuration key (default off — the DatabaseMigrations tool owns applying).
/// Throws <see cref="InvalidOperationException"/> if any pending migrations remain after the check.
/// Clone of the Outbox migration startup service.
/// </summary>
internal sealed class WorkflowMigrationStartupService(
    IDbContextFactory<WorkflowDbContext> factory,
    IConfiguration configuration,
    ILogger<WorkflowMigrationStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var ctx = await factory.CreateDbContextAsync(cancellationToken);

        if (configuration.GetValue<bool>("Database:AutoMigrateOnStartup", false))
        {
            logger.LogInformation("Applying pending migrations for {Context}...", nameof(WorkflowDbContext));
            await ctx.Database.MigrateAsync(cancellationToken);
        }

        var pending = (await ctx.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            var msg = $"{nameof(WorkflowDbContext)} has {pending.Count} pending migration(s): {string.Join(", ", pending)}";
            logger.LogCritical("{Message}", msg);
            throw new InvalidOperationException(msg);
        }

        logger.LogInformation("{Context} migration check passed.", nameof(WorkflowDbContext));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}