using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Core.DataAccess.Outbox;

/// <summary>
/// Checks (and optionally applies) pending EF Core migrations for <see cref="OutboxDbContext"/> at application startup.
/// Controlled by the <c>Database:AutoMigrateOnStartup</c> configuration key.
/// Throws <see cref="InvalidOperationException"/> if any pending migrations remain after the check.
/// Clone of the HEAppE integration migration startup service.
/// </summary>
internal sealed class OutboxMigrationStartupService(
    IDbContextFactory<OutboxDbContext> factory,
    IConfiguration configuration,
    ILogger<OutboxMigrationStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var ctx = await factory.CreateDbContextAsync(cancellationToken);

        if (configuration.GetValue<bool>("Database:AutoMigrateOnStartup", false))
        {
            logger.LogInformation("Applying pending migrations for {Context}...", nameof(OutboxDbContext));
            await ctx.Database.MigrateAsync(cancellationToken);
        }

        var pending = (await ctx.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            var msg = $"{nameof(OutboxDbContext)} has {pending.Count} pending migration(s): {string.Join(", ", pending)}";
            logger.LogCritical("{Message}", msg);
            throw new InvalidOperationException(msg);
        }

        logger.LogInformation("{Context} migration check passed.", nameof(OutboxDbContext));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}