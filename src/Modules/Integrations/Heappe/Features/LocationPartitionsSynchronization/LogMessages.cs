using Lukdrasil.Error;

using Microsoft.Extensions.Logging;

using UserOrg.Shared;

namespace Heappe.Features.LocationPartitionsSynchronization;

internal static partial class LogMessages
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "LocationPartitionSync: Location partitions synchronization started for location '{locationId}'.")]
    internal static partial void LogLocationPartitionsStarted(this ILogger logger, Guid locationId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "LocationPartitionSync: Location partition (partition:'{partitionName}', aggregation:'{aggregationName}') marked as deleted for location '{locationId}', HEAppE identifier '{heappeIdentifier}'.")]
    internal static partial void LogLocationPartitionMarkedAsDeleted(this ILogger logger, string partitionName, string aggregationName, Guid locationId, HeappeIdentifier heappeIdentifier);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "LocationPartitionSync: Location partition (partition:'{partitionName}', aggregation:'{aggregationName}') was created for location '{locationId}', HEAppE identifier '{heappeIdentifier}'.")]
    internal static partial void LogLocationPartitionCreated(this ILogger logger, string partitionName, string aggregationName, Guid locationId, HeappeIdentifier heappeIdentifier);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "LocationPartitionSync: Location partitions synchronized for location '{locationId}'.")]
    internal static partial void LogLocationPartitionsSynchronized(this ILogger logger, Guid locationId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "LocationPartitionSync: Database error while persisting location partition changes for location '{locationId}', HEAppE identifier '{heappeIdentifier}'.")]
    internal static partial void LogLocationPartitionsSaveChangesFailure(this ILogger logger, Guid locationId, HeappeIdentifier heappeIdentifier, Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "LocationPartitionSync: Failed to open a service connection (cause '{cause}') to HEAppE identifier '{heappeIdentifier}' (resource: '{resourceName}').")]
    internal static partial void LogServiceConnectionFailed(this ILogger logger, HeappeIdentifier heappeIdentifier, string resourceName, string cause);

    [LoggerMessage(EventId = 9, Level = LogLevel.Error, Message = "LocationPartitionSync: Failed to list clusters on HEAppE instance for HEAppE identifier '{heappeIdentifier}'. Error: '{error}'.")]
    internal static partial void LogFailedToListHeappeClusters(this ILogger logger, HeappeIdentifier heappeIdentifier, Error error);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "LocationPartitionSync: Failed to find a cluster matching location name '{clusterName}' or login endpoint '{loginEndpoint}' (endpoint matched on host only, port ignored) on HEAppE instance for HEAppE identifier '{heappeIdentifier}'. Available clusters: [{availableClusters}].")]
    internal static partial void LogFailedToFindHeappeCluster(this ILogger logger, HeappeIdentifier heappeIdentifier, string clusterName, string loginEndpoint, string availableClusters);
}