using System.Diagnostics;

using Heappe.Contracts.Entries.Sync;
using Heappe.Features.CommandTemplateSynchronization;

using Lukdrasil.Result;

using Microsoft.Extensions.Logging;

namespace Heappe.Features.BackgroundSync;

internal sealed class WorkItemProcessor
{
    private readonly IProjectCommandTemplateSyncService _syncService;
    private readonly InProgressTracker _tracker;
    private readonly ILogger _logger;

    public WorkItemProcessor(IProjectCommandTemplateSyncService syncService, InProgressTracker tracker, ILogger logger)
    {
        _syncService = syncService;
        _tracker = tracker;
        _logger = logger;
    }

    public async Task ProcessAsync(WorkItem item, int consumerId, CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        var queueWaitTime = (startedAt - item.EnqueuedAt).TotalSeconds;

        _logger.LogInformation("Background processing started: Consumer {ConsumerId}, WorkItem Key: {Key}, EnqueuedAt: {EnqueuedAt}, StartedAt: {StartedAt}",
            consumerId, item.Key, item.EnqueuedAt, startedAt);

        _logger.LogDebug("Consumer {ConsumerId} received work item from queue after {QueueWaitTime}s: {Key}",
            consumerId, queueWaitTime, item.Key);

        var deduplicationResult = DetermineDeduplicationAction(item.Key, startedAt);

        if (deduplicationResult == DeduplicationAction.Skip)
        {
            _logger.LogDebug("Skipping duplicate work item (already being processed): {Key}", item.Key);
            return;
        }

        if (deduplicationResult == DeduplicationAction.RetryAfterTimeout)
        {
            _logger.LogWarning("TIMEOUT DETECTED: In-progress work item exceeded {TimeoutMinutes}min, reprocessing: {Key}",
                _tracker.GetTimeoutDuration().TotalMinutes, item.Key);
            _tracker.UpdateStartTime(item.Key, startedAt);
        }

        await ExecuteSyncWithTelemetry(item, consumerId, queueWaitTime, ct);
    }

    private DeduplicationAction DetermineDeduplicationAction(string key, DateTime startedAt)
    {
        if (_tracker.TryMarkInProgress(key, startedAt))
            return DeduplicationAction.Process;

        return _tracker.HasTimedOut(key)
            ? DeduplicationAction.RetryAfterTimeout
            : DeduplicationAction.Skip;
    }

    private async Task ExecuteSyncWithTelemetry(WorkItem item, int consumerId, double queueWaitTime, CancellationToken ct)
    {
        using var processActivity = HeappeSyncTelemetry.ActivitySource.StartActivity(
            HeappeSyncTelemetry.ActivityBackgroundSync,
            ActivityKind.Internal);

        SetActivityTags(processActivity, item, consumerId, queueWaitTime);

        try
        {
            _logger.LogInformation("Background sync starting for {Key}", item.Key);
            var syncStarted = DateTime.UtcNow;

            using var syncActivity = HeappeSyncTelemetry.ActivitySource.StartActivity(
                HeappeSyncTelemetry.ActivitySyncResource,
                ActivityKind.Internal);

            if (syncActivity is not null)
            {
                syncActivity.SetTag(HeappeSyncTelemetry.TagKey, item.Key);
                syncActivity.SetTag(HeappeSyncTelemetry.TagProject, item.ProjectShortName);
                syncActivity.SetTag(HeappeSyncTelemetry.TagResource, item.ResourceName);
            }

            var syncOutcome = await _syncService.SyncCommandTemplatesForResource(item.ProjectShortName, item.ResourceName, ct);

            var duration = (DateTime.UtcNow - syncStarted).TotalSeconds;
            var (succeededCount, failedLocations) = SummarizeOutcome(syncOutcome);
            var allSucceeded = syncOutcome.IsSuccess && failedLocations.Count == 0;

            if (!syncOutcome.IsSuccess)
            {
                // The sync produced no usable result — either an early pre-location failure
                // (project/resource lookup, expired allocation) or every location failed. Neither
                // carries a per-location breakdown here, so report the error explicitly rather
                // than logging COMPLETED.
                _logger.LogWarning("Background sync FAILED for {Key}: {Error}", item.Key, syncOutcome.Error);
            }
            else if (failedLocations.Count > 0)
            {
                _logger.LogWarning(
                    "Background sync PARTIAL/FAILED for {Key}: {SucceededCount} location(s) succeeded, {FailedCount} failed: {FailedLocations}",
                    item.Key, succeededCount, failedLocations.Count,
                    string.Join("; ", failedLocations.Select(f => $"{f.LocationName}/{f.AggregationName} @ {f.HeappeInstanceUri}: {string.Join(", ", f.Errors)}")));
            }
            else
            {
                _logger.LogInformation("Background sync COMPLETED for {Key} (duration: {DurationSeconds}s, queued for: {QueueWaitTime}s)",
                    item.Key, duration, queueWaitTime);
            }

            syncActivity?.SetTag(HeappeSyncTelemetry.TagDurationSeconds, duration);
            syncActivity?.SetTag(HeappeSyncTelemetry.TagSuccess, allSucceeded);
            syncActivity?.SetTag(HeappeSyncTelemetry.TagLocationsSucceeded, succeededCount);
            syncActivity?.SetTag(HeappeSyncTelemetry.TagLocationsFailed, failedLocations.Count);
            processActivity?.SetTag(HeappeSyncTelemetry.TagDurationSeconds, duration);
            processActivity?.SetTag(HeappeSyncTelemetry.TagSuccess, allSucceeded);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation(ex, "Background sync CANCELLED for {Key} (likely due to application shutdown)", item.Key);
            processActivity?.SetStatus(ActivityStatusCode.Error, "Operation cancelled during sync");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background sync FAILED for {Key} - Project: {Project}, Resource: {Resource}",
                item.Key, item.ProjectShortName, item.ResourceName);
            processActivity?.SetStatus(ActivityStatusCode.Error, $"Sync failed: {ex.GetType().Name}");
        }
        finally
        {
            _tracker.MarkCompleted(item.Key);
            _logger.LogDebug("Consumer {ConsumerId} finished processing {Key}", consumerId, item.Key);
        }
    }

    private static void SetActivityTags(Activity? activity, WorkItem item, int consumerId, double queueWaitTime)
    {
        if (activity is null)
            return;

        activity.SetTag(HeappeSyncTelemetry.TagConsumerId, consumerId);
        activity.SetTag(HeappeSyncTelemetry.TagKey, item.Key);
        activity.SetTag(HeappeSyncTelemetry.TagProject, item.ProjectShortName);
        activity.SetTag(HeappeSyncTelemetry.TagResource, item.ResourceName);
        activity.SetTag(HeappeSyncTelemetry.TagQueueWaitSeconds, queueWaitTime);
    }

    private static (int SucceededCount, IReadOnlyList<LocationSyncResultEntry> Failed) SummarizeOutcome(
        ValidationResult<CommandTemplateSyncResult> outcome)
    {
        if (!outcome.IsSuccess)
        {
            return (0, []); // pre-location failure (project/resource/allocation) — nothing to break down per-location
        }

        var locations = outcome.Value.Locations;
        return (locations.Count(l => l.IsSuccess), locations.Where(l => !l.IsSuccess).ToList());
    }
}

internal enum DeduplicationAction
{
    Process,
    Skip,
    RetryAfterTimeout
}