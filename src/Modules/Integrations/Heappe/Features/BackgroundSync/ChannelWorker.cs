using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading.Channels;

using Heappe.Features.CommandTemplateSynchronization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Microsoft.Extensions.Logging;

namespace Heappe.Features.BackgroundSync;

public sealed record WorkItem(string ProjectShortName, string ResourceName)
{
    public DateTimeOffset EnqueuedAt { get; init; } = DateTimeOffset.UtcNow;

    public string Key => $"{ProjectShortName}:{ResourceName}";
}

internal sealed class ChannelWorker(Channel<WorkItem> channel, IServiceProvider serviceProvider, ILogger<ChannelWorker> logger) : BackgroundService
{
    private readonly int _degreeOfParallelism = 4;
    private readonly TimeSpan _inProgressTimeout = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tracker = new InProgressTracker(_inProgressTimeout);
        var reader = channel.Reader;

        var consumers = CreateConsumers(reader, tracker, stoppingToken);

        LogWorkerStartup(consumers.Count);
        RecordStartupActivity();

        try
        {
            await Task.WhenAll(consumers);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("ChannelWorker stopping gracefully due to application shutdown - all consumers will terminate");
        }
    }

    private ImmutableList<Task> CreateConsumers(ChannelReader<WorkItem> reader, InProgressTracker tracker, CancellationToken stoppingToken)
    {
        return Enumerable.Range(0, _degreeOfParallelism)
            .Select(workerId => Task.Run(async () => await ConsumerLoop(workerId, reader, tracker, stoppingToken), stoppingToken))
            .ToImmutableList();
    }

    private async Task ConsumerLoop(int workerId, ChannelReader<WorkItem> reader, InProgressTracker tracker, CancellationToken stoppingToken)
    {
        using var consumerActivity = HeappeSyncTelemetry.ActivitySource.StartActivity(
            $"heappe_sync.consumer_{workerId}",
            ActivityKind.Internal);

        if (consumerActivity is not null)
        {
            consumerActivity.SetTag(HeappeSyncTelemetry.TagConsumerId, workerId);
        }

        logger.LogInformation("Background sync consumer {WorkerId} started (will process queue items)", workerId);

        await foreach (var item in reader.ReadAllAsync(stoppingToken))
        {
            using var scope = serviceProvider.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<IProjectCommandTemplateSyncService>();
            var processor = new WorkItemProcessor(syncService, tracker, logger);

            await processor.ProcessAsync(item, workerId, stoppingToken);
        }

        logger.LogInformation("Consumer {WorkerId} stopped (channel reader completed)", workerId);
    }

    private void LogWorkerStartup(int consumerCount)
    {
        logger.LogInformation("ChannelWorker initialized with {Count} parallel consumers (capacity: {Capacity} items)",
            consumerCount, 1000);
    }

    private void RecordStartupActivity()
    {
        using var startupActivity = HeappeSyncTelemetry.ActivitySource.StartActivity(
            "heappe_sync.startup",
            System.Diagnostics.ActivityKind.Internal);

        if (startupActivity is not null)
        {
            startupActivity.SetTag(HeappeSyncTelemetry.TagConsumerCount, _degreeOfParallelism);
            startupActivity.SetTag(HeappeSyncTelemetry.TagChannelCapacity, 1000);
        }
    }
}