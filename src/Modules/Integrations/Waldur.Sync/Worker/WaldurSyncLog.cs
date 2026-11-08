using Microsoft.Extensions.Logging;

namespace Waldur.Sync.Worker;

/// <summary>
/// The structured, source-generated logging surface for the sync worker (ADR-0058). Levels are the
/// contract: Debug = troubleshooting detail; Information = per-entity start/end (surfaced by
/// StepUpLogging around a failure); Information always-log = the two iteration-summary lines;
/// Warning/Error = one structured entry per failed item with a <c>phase</c> tag. Unchanged items log
/// nothing.
/// </summary>
internal static partial class WaldurSyncLog
{
    [LoggerMessage(EventId = 5000, Level = LogLevel.Information,
        Message = "Waldur sync startup: rebuilding SHA map for instance {Instance}.")]
    public static partial void StartupRebuilding(ILogger logger, string instance);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information,
        Message = "Waldur sync startup: instance {Instance} SHA map rebuilt with {Entries} entries.")]
    public static partial void StartupRebuilt(ILogger logger, string instance, int entries);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information,
        Message = "Waldur sync startup: instance {Instance} published unconditional StorageConfigurationChanged.")]
    public static partial void StartupStorageConfigPublished(ILogger logger, string instance);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Error,
        Message = "Waldur sync startup: instance {Instance} StorageConfigurationChanged publish failed; step 1 will retry.")]
    public static partial void StartupStorageConfigFailed(ILogger logger, string instance, Exception exception);

    [LoggerMessage(EventId = 5005, Level = LogLevel.Error,
        Message = "Waldur sync startup: instance {Instance} StorageConfigurationChanged rejected by handler ({Error}); step 1 will retry.")]
    public static partial void StartupStorageConfigRejected(ILogger logger, string instance, string error);

    [LoggerMessage(EventId = 5010, Level = LogLevel.Information,
        Message = "Waldur sync iteration starting for instance {Instance} (trigger: {Trigger}).")]
    public static partial void IterationStarting(ILogger logger, string instance, string trigger);

    [LoggerMessage(EventId = 5011, Level = LogLevel.Information,
        Message = "Waldur sync iteration complete for instance {Instance}: duration={DurationMs}ms fetched={Fetched} created={Created} updated={Updated} removed={Removed} unchanged={Unchanged} failed={Failed} (blocked-by-parent={BlockedByParent}) absent={Absent}.")]
    public static partial void IterationCompleted(
        ILogger logger, string instance, double durationMs, int fetched, int created, int updated,
        int removed, int unchanged, int failed, int blockedByParent, int absent);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Error,
        Message = "Waldur sync worker for instance {Instance} faulted; contained to this worker, backing off and retrying next interval (host + other instances unaffected).")]
    public static partial void WorkerFaulted(ILogger logger, string instance, Exception exception);

    [LoggerMessage(EventId = 5020, Level = LogLevel.Information,
        Message = "Waldur sync processing {EntityType} {ExternalId} for instance {Instance}.")]
    public static partial void EntityProcessing(ILogger logger, string instance, string entityType, string externalId);

    [LoggerMessage(EventId = 5021, Level = LogLevel.Information,
        Message = "Waldur sync processed {EntityType} {ExternalId} for instance {Instance}.")]
    public static partial void EntityProcessed(ILogger logger, string instance, string entityType, string externalId);

    [LoggerMessage(EventId = 5022, Level = LogLevel.Debug,
        Message = "Waldur sync emitting {EventType} for {EntityType} {ExternalId} (instance {Instance}).")]
    public static partial void EventEmitting(ILogger logger, string instance, string eventType, string entityType, string externalId);

    [LoggerMessage(EventId = 5030, Level = LogLevel.Error,
        Message = "Waldur sync item failed: instance={Instance} entityType={EntityType} externalId={ExternalId} phase={Phase} outcome=retry-next-iteration.")]
    public static partial void ItemFailed(
        ILogger logger, string instance, string entityType, string externalId, string phase, Exception exception);

    [LoggerMessage(EventId = 5034, Level = LogLevel.Error,
        Message = "Waldur sync item rejected: instance={Instance} entityType={EntityType} externalId={ExternalId} phase={Phase} error={Error} outcome=retry-next-iteration.")]
    public static partial void ItemRejected(
        ILogger logger, string instance, string entityType, string externalId, string phase, string error);

    [LoggerMessage(EventId = 5035, Level = LogLevel.Error,
        Message = "Waldur sync fetch failed: instance={Instance} entityType={EntityType} externalId={ExternalId} reason={Reason} phase={Phase} outcome=retry-next-iteration.")]
    public static partial void FetchDependencyFailed(
        ILogger logger, string instance, string entityType, string externalId, string reason, string phase, Exception? exception);

    [LoggerMessage(EventId = 5031, Level = LogLevel.Error,
        Message = "Waldur sync phase failed: instance={Instance} phase={Phase} outcome=retry-next-iteration (whole iteration abandoned).")]
    public static partial void PhaseFailed(ILogger logger, string instance, string phase, Exception exception);

    [LoggerMessage(EventId = 5032, Level = LogLevel.Warning,
        Message = "Waldur sync mapping skipped {EntityType} {ExternalId} for instance {Instance}: {Reason} — {Detail}.")]
    public static partial void MappingSkipped(
        ILogger logger, string instance, string entityType, string externalId, string reason, string detail);

    [LoggerMessage(EventId = 5033, Level = LogLevel.Debug,
        Message = "Waldur sync skipped dependent resource {ExternalId} for instance {Instance}: its offering is unavailable because an upstream offering fetch failed this iteration (ADR-0063) — distinct from a genuine missing offering (retry next iteration).")]
    public static partial void ResourceSkippedByFetchFailure(ILogger logger, string instance, string externalId);

    [LoggerMessage(EventId = 5040, Level = LogLevel.Warning,
        Message = "Waldur sync absence: instance={Instance} {EntityType} {ExternalId} left the pull; no delete event (D3), SHA entry dropped.")]
    public static partial void AbsenceTransition(ILogger logger, string instance, string entityType, string externalId);
}