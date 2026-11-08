using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Heappe.Persistence;

/// <summary>
/// Startup service that ensures the Heappe integration database is seeded with default data
/// before the application begins serving requests.
/// </summary>
internal sealed class HeappeDbInitializerService(
    IDbContextFactory<HeappeIntegrationDbContext> factory,
    ILogger<HeappeDbInitializerService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Running Heappe integration database seed.");
        await using var ctx = await factory.CreateDbContextAsync(cancellationToken);
        await HeappeIntegrationPersistenceExtensions.SeedAsync(ctx, cancellationToken);
        logger.LogInformation("Heappe integration database seed completed.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}