using System.Diagnostics;
using System.Threading.Channels;

using Heappe.Features.BackgroundSync;

using Microsoft.Extensions.Logging;

using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.ValueObjects;

namespace Userorg.Adapter.Heappe;

internal sealed class HeappeSynchronizationService(
    ILogger<HeappeSynchronizationService> logger,
    Channel<WorkItem> channel) : IHeappeSynchronizationService
{
    public async Task TriggerSynchronizationForProjectResource(ProjectShortName projectShortName, string resourceName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(projectShortName);
        ArgumentNullException.ThrowIfNull(resourceName);

        var workItem = new WorkItem(projectShortName.Value, resourceName);

        logger.LogDebug("Attempting to enqueue synchronization work item: {Key}", workItem.Key);

        using var activity = HeappeSyncTelemetry.ActivitySource.StartActivity(
            HeappeSyncTelemetry.ActivityChannelEnqueue,
            ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag(HeappeSyncTelemetry.TagProject, projectShortName.Value);
            activity.SetTag(HeappeSyncTelemetry.TagResource, resourceName);
            activity.SetTag(HeappeSyncTelemetry.TagKey, workItem.Key);
        }

        try
        {
            if (!channel.Writer.TryWrite(workItem))
            {
                logger.LogDebug("Channel full, waiting to write work item asynchronously: {Key}", workItem.Key);
                await channel.Writer.WriteAsync(workItem, ct);
            }

            logger.LogInformation("Work item successfully enqueued for synchronization: {Project}/{Resource}", projectShortName.Value, resourceName);
            activity?.SetTag(HeappeSyncTelemetry.TagSuccess, true);
        }
        catch (ChannelClosedException ex)
        {
            logger.LogError(ex, "CRITICAL: Failed to enqueue work item - synchronization service is stopped. Project={Project}, Resource={Resource}, Key={Key}",
                projectShortName.Value, resourceName, workItem.Key);

            activity?.SetStatus(ActivityStatusCode.Error, "Channel closed - sync service stopped");
            throw;
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(ex, "Work item enqueue was cancelled (likely timeout). Project={Project}, Resource={Resource}, Key={Key}",
                projectShortName.Value, resourceName, workItem.Key);

            activity?.SetStatus(ActivityStatusCode.Error, "Operation cancelled");
            throw;
        }
    }
}