using Forge.Core.Features.UserOrgRoles;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Forge.Core.Persistence;

/// <summary>
/// Checks (and optionally applies) pending EF Core migrations for <see cref="UserOrgDbContext"/> at application startup.
/// Controlled by the <c>Database:AutoMigrateOnStartup</c> configuration key.
/// Throws <see cref="InvalidOperationException"/> if any pending migrations remain after the check.
/// </summary>
internal sealed class UserOrgMigrationStartupService(
    IDbContextFactory<UserOrgDbContext> factory,
    IConfiguration configuration,
    ILogger<UserOrgMigrationStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var ctx = await factory.CreateDbContextAsync(cancellationToken);

        if (configuration.GetValue<bool>("Database:AutoMigrateOnStartup", false))
        {
            logger.LogInformation("Applying pending migrations for {Context}...", nameof(UserOrgDbContext));
            await ctx.Database.MigrateAsync(cancellationToken);
        }

        var pending = (await ctx.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            var msg = $"{nameof(UserOrgDbContext)} has {pending.Count} pending migration(s): {string.Join(", ", pending)}";
            logger.LogCritical("{Message}", msg);
            throw new InvalidOperationException(msg);
        }

        logger.LogInformation("{Context} migration check passed.", nameof(UserOrgDbContext));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}