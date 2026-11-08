using Lukdrasil.Error;

using Microsoft.Extensions.Logging;

using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;

namespace Heappe.Features.CommandTemplateSynchronization;

/// <summary>
/// Provides a set of static logging methods for tracking and reporting events during the synchronization of project
/// command templates with HEAppE instances.
/// </summary>
/// <remarks>This class defines structured log messages for various stages and outcomes of the project command
/// template synchronization process, including informational, warning, and error events. The logging methods are
/// intended to assist with monitoring, debugging, and diagnosing issues that may arise during synchronization between
/// local project definitions and remote HEAppE resources. All methods are static and are designed to be used as
/// extension methods on an ILogger instance.</remarks>
internal static partial class ProjectCommandTemplateSyncLogs
{
    [LoggerMessage(EventId = 10, Level = LogLevel.Debug, Message = "ProjectCmdTempSync: Synchronization started for project '{projectShortName}', and resource '{resourceName}'.")]
    internal static partial void LogProjectSynchronizationStart(this ILogger logger, string projectShortName, string resourceName);

    [LoggerMessage(EventId = 10, Level = LogLevel.Debug, Message = "ProjectCmdTempSync: Synchronization finished for project '{projectShortName}', and resource '{resourceName}'.")]
    internal static partial void LogProjectSynchronizationFinish(this ILogger logger, string projectShortName, string resourceName);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "ProjectCmdTempSync: Failed to retrieve command template definitions for location type '{locationType}', location '{locationId}'.")]
    internal static partial void LogGetCommandTemplateDefinitionFailure(this ILogger logger, Exception exception, ELocationType locationType, Guid locationId);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "ProjectCmdTempSync: Project '{projectShortName}' or resource '{resourceName}' is expired or inactive; synchronization skipped.")]
    internal static partial void LogProjectOrResourceExpiration(this ILogger logger, string projectShortName, string resourceName);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "ProjectCmdTempSync: Failed to open a service connection (cause '{cause}') to HEAppE identifier '{heappeIdentifier}' (project '{projectShortName}', resource '{resourceName}'); instance will be skipped.")]
    internal static partial void LogServiceConnectionFailed(this ILogger logger, HeappeIdentifier heappeIdentifier, string projectShortName, string resourceName, string cause);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "ProjectCmdTempSync: Synchronization finished with validation errors for project '{projectShortName}', resource '{resourceName}'. Errors: '{errors}'")]
    internal static partial void LogProjectSynchronizationErrors(this ILogger logger, string projectShortName, string resourceName, ValidationError errors);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "ProjectCmdTempSync: Location '{locationName}' (id '{locationId}') is frozen by an active kill-switch; synchronization skipped for project '{projectShortName}', resource '{resourceName}'.")]
    internal static partial void LogLocationFrozenSkipped(this ILogger logger, string locationName, Guid locationId, string projectShortName, string resourceName);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "ProjectCmdTempSync: Failed to retrieve LocationPartition for queue '{queueName}' at location '{locationId}' while creating project command template '{commandTemplateName}'; creation aborted.")]
    internal static partial void LogLocationPartitionRetrievalFailure(this ILogger logger, string commandTemplateName, string queueName, Guid locationId);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "ProjectCmdTempSync: HEAppE project for resource '{resourceName}' not found on instance '{heappeIdentifier}' (project '{projectShortName}').")]
    internal static partial void LogHeappeProjectNotFound(this ILogger logger, string resourceName, HeappeIdentifier heappeIdentifier, string projectShortName);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "ProjectCmdTempSync: Cluster '{cluster}' not present in HEAppE instance '{heappeIdentifier}' (project '{projectShortName}', resource '{resourceName}').")]
    internal static partial void LogClusterMissing(this ILogger logger, string cluster, HeappeIdentifier heappeIdentifier, string projectShortName, string resourceName);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "ProjectCmdTempSync: Cluster '{cluster}' with aggregation '{aggregation}' not present in HEAppE instance '{heappeIdentifier}' (project '{projectShortName}', resource '{resourceName}').")]
    internal static partial void LogClusterWithAggregationMissing(this ILogger logger, string cluster, string aggregation, HeappeIdentifier heappeIdentifier, string projectShortName, string resourceName);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "ProjectCmdTempSync: Project '{projectShortName}', resource '{resourceName}' has no available locations for synchronization; synchronization skipped.")]
    internal static partial void LogLocationNotFound(this ILogger logger, string projectShortName, string resourceName);

    [LoggerMessage(EventId = 10, Level = LogLevel.Debug, Message = "ProjectCmdTempSync: No command template definitions found for location type '{locationType}', location '{locationId}'.")]
    internal static partial void LogNoCommandTemplateDefinitionsFound(this ILogger logger, ELocationType locationType, Guid locationId);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "ProjectCmdTempSync: Failed to update expiration state for project '{projectShortName}', resource '{resourceName}'.")]
    internal static partial void LogProjectResourceExpirationStateUpdateFailure(this ILogger logger, string projectShortName, string resourceName);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "ProjectSync: Failed to obtain project resource allocation for project '{projectShortName}', resource '{resourceName}'. Error: {error}.")]
    internal static partial void LogProjectResourceAllocationObtainingError(this ILogger logger, string projectShortName, string resourceName, Error error);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "ProjectCmdTempSync: Project '{projectShortName}', resource '{resourceName}' has no configured HEAppE locations (HEAPPE_URL/HEAPPE_IDENTIFIER); synchronization skipped.")]
    internal static partial void LogProjectResourceNoHeappeLocations(this ILogger logger, string projectShortName, string resourceName);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "ProjectCmdTempSync: Failed to update command template state for commandTemplate '{commandTemplateName}', HEAppECommandTemplateid '{heappeCommandTemplatId}', queue '{queueName}', locationId '{locationId}'.")]
    internal static partial void LogCommandTemplateStateUpdateFailure(this ILogger logger, string commandTemplateName, long heappeCommandTemplatId, string queueName, Guid locationId);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "ProjectCmdTempSync: Failed to create project command template '{commandTemplateName}' for queue '{queueName}' at location '{locationId}'.")]
    internal static partial void LogProjectCommandTemplateCreationError(this ILogger logger, string commandTemplateName, string queueName, Guid locationId);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "ProjectCmdTempSync: Failed to create HEAppE command template '{commandTemplateName}' in HEAppE project '{heappeProjectName}' for queue '{queueName}'; remote creation failed.")]
    internal static partial void LogHeappeCommandTemplateCreationError(this ILogger logger, string commandTemplateName, string heappeProjectName, string queueName);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "ProjectCmdTempSync: Failed to synchronize command template '{commandTemplateName}' with HEAppE instance '{heappeIdentifier}' for queue '{queueName}' at location '{locationId}' (project '{projectShortName}', resource '{resourceName}').")]
    internal static partial void LogCommandTemplateSyncFailure(this ILogger logger, string commandTemplateName, HeappeIdentifier heappeIdentifier, string queueName, Guid locationId, string projectShortName, string resourceName);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "ProjectCmdTempSync: Failed to retrieve command templates for HEAppE project '{heappeProjectId}' on instance '{heappeIdentifier}' (project '{projectShortName}', resource '{resourceName}'): {error}.")]
    internal static partial void LogFailedToRetrieveHeappeCommandTemplates(this ILogger logger, long heappeProjectId, HeappeIdentifier heappeIdentifier, string projectShortName, string resourceName, Error error);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "ProjectCmdTempSync: Failed to list clusters for HEAppE instance '{heappeIdentifier}' (project '{projectShortName}', resource '{resourceName}'). Error: '{error}'.")]
    internal static partial void LogFailedToListHeappeClusters(this ILogger logger, HeappeIdentifier heappeIdentifier, string projectShortName, string resourceName, Error error);




    // Need to be checked

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "ProjectCmdTempSync: Location partition (partition:'{partitionName}', aggregation:'{aggregationName}')  marked as deleted for location '{locationId}'  from HEAppE instance '{heappeIdentifier}'.")]
    internal static partial void LogLocationPartitionMarkedAsDeleted(this ILogger logger, string partitionName, string aggregationName, Guid locationId, HeappeIdentifier heappeIdentifier);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "ProjectCmdTempSync: Location partition (partition:'{partitionName}', aggregation:'{aggregationName}') was created for location '{locationId}' from HEAppE instance '{heappeIdentifier}'.")]
    internal static partial void LogLocationPartitionCreated(this ILogger logger, string partitionName, string aggregationName, Guid locationId, HeappeIdentifier heappeIdentifier);

    [LoggerMessage(EventId = 10, Level = LogLevel.Debug, Message = "ProjectCmdTempSync: No location partition changes for location '{locationId}'.")]
    internal static partial void LogLocationPartitionsNoChanges(this ILogger logger, Guid locationId);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "ProjectCmdTempSync: Synchronized location partitions for location '{locationId}'; changes: {changes}.")]
    internal static partial void LogLocationPartitionsSynchronized(this ILogger logger, Guid locationId, int changes);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "ProjectCmdTempSync: Database error while persisting location partition changes for location '{locationId}' from HEAppE instance '{heappeIdentifier}'.")]
    internal static partial void LogLocationPartitionsSaveChangesFailure(this ILogger logger, Guid locationId, HeappeIdentifier heappeIdentifier, Exception exception);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "ProjectCmdTempSync: Aggregation '{aggregation}' for location '{locationId}' not present in HEAppE; marked {deletedCount} local partitions as deleted.")]
    internal static partial void LogAggregationMissingMarkedPartitionsDeleted(this ILogger logger, string aggregation, Guid locationId, int deletedCount);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "ProjectCmdTempSync: Failed to synchronize location queues for location '{locationId}', aggregation '{aggregation}' on HEAppE instance '{heappeIdentifier}' (project '{projectShortName}', resource '{resourceName}'). Error: {error}.")]
    internal static partial void LogLocationQueueSynchronizationFailure(this ILogger logger, Guid locationId, string? aggregation, HeappeIdentifier heappeIdentifier, string projectShortName, string resourceName, Error error);
}