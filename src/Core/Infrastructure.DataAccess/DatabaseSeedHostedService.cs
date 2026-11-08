using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UserOrganizationManagement.Infrastructure.DataAccess;

/// <summary>
/// Runs <see cref="DatabaseSeedService.SeedDatabase"/> at startup, after the migration check completes.
/// </summary>
internal sealed class DatabaseSeedHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseSeedHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var seed = scope.ServiceProvider.GetRequiredService<DatabaseSeedService>();
        logger.LogInformation("Seeding database...");
        seed.SeedDatabase();
        logger.LogInformation("Database seed completed.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}