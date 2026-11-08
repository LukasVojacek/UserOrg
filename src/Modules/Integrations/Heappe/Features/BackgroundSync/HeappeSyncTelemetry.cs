using System.Diagnostics;

namespace Heappe.Features.BackgroundSync;

public static class HeappeSyncTelemetry
{
    public const string ActivitySourceName = "UserOrg.Integrations.Heappe.Sync";
    public const string Version = "1.0.0";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    public const string ActivityChannelEnqueue = "heappe_sync.enqueue";
    public const string ActivityBackgroundSync = "heappe_sync.background_process";
    public const string ActivitySyncResource = "heappe_sync.resource_sync";

    public const string TagProject = "heappe_sync.project";
    public const string TagResource = "heappe_sync.resource";
    public const string TagKey = "heappe_sync.key";
    public const string TagConsumerId = "heappe_sync.consumer_id";
    public const string TagDurationSeconds = "heappe_sync.duration_seconds";
    public const string TagQueueWaitSeconds = "heappe_sync.queue_wait_seconds";
    public const string TagSkipReason = "heappe_sync.skip_reason";
    public const string TagTimeoutMinutes = "heappe_sync.timeout_minutes";
    public const string TagChannelCapacity = "heappe_sync.channel_capacity";
    public const string TagConsumerCount = "heappe_sync.consumer_count";
    public const string TagSuccess = "heappe_sync.success";
    public const string TagLocationsSucceeded = "heappe_sync.locations_succeeded";
    public const string TagLocationsFailed = "heappe_sync.locations_failed";
}