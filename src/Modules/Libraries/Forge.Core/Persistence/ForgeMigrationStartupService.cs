using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Forge.Core.Persistence;

/// <summary>
/// Checks (and optionally applies) pending EF Core migrations for <see cref="ForgeDbContext"/> at application startup.
/// Controlled by the <c>Database:AutoMigrateOnStartup</c> configuration key.
/// Throws <see cref="InvalidOperationException"/> if any pending migrations remain after the check.
/// </summary>
internal sealed class ForgeMigrationStartupService(
    IDbContextFactory<ForgeDbContext> factory,
    IConfiguration configuration,
    ILogger<ForgeMigrationStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var ctx = await factory.CreateDbContextAsync(cancellationToken);

        if (configuration.GetValue<bool>("Database:AutoMigrateOnStartup", false))
        {
            logger.LogInformation("Applying pending migrations for {Context}...", nameof(ForgeDbContext));
            await ctx.Database.MigrateAsync(cancellationToken);
        }

        var pending = (await ctx.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            var msg = $"{nameof(ForgeDbContext)} has {pending.Count} pending migration(s): {string.Join(", ", pending)}";
            logger.LogCritical("{Message}", msg);
            throw new InvalidOperationException(msg);
        }

        logger.LogInformation("{Context} migration check passed.", nameof(ForgeDbContext));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}