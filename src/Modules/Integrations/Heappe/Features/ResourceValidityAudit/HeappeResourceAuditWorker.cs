using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;

namespace Heappe.Features.ResourceValidityAudit;

/// <summary>
/// Scheduled audit. Waits a fixed delay after startup, then runs every <c>Interval</c>. Each cycle is gated on the
/// <see cref="FeatureFlagName"/> flag (evaluated at runtime so it can be toggled without a restart) and posts a Teams
/// card only when there are findings.
/// </summary>
internal sealed class HeappeResourceAuditWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<HeappeResourceAuditOptions> options,
    ILogger<HeappeResourceAuditWorker> logger) : BackgroundService
{
    /// <summary>Feature flag (Microsoft.FeatureManagement) that enables both this worker's cycles and the manual endpoint.</summary>
    internal const string FeatureFlagName = "HeappeResourceAudit";

    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.Interval);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await WaitForNextAsync(timer, stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            var featureManager = scope.ServiceProvider.GetRequiredService<IFeatureManager>();
            if (!await featureManager.IsEnabledAsync(FeatureFlagName))
            {
                return;
            }

            var audit = scope.ServiceProvider.GetRequiredService<IHeappeResourceAuditService>();
            var webhook = scope.ServiceProvider.GetRequiredService<HeappeResourceAuditWebhook>();
            await HeappeResourceAuditRun.CollectAndNotifyAsync(audit, webhook, logger, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down — not an error
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scheduled HEAppE resource audit run failed.");
        }
    }

    private static async Task<bool> WaitForNextAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}