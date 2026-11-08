using Microsoft.Extensions.Logging;

using UserOrg.Shared;

namespace Heappe.Features.InitializationProjectWithClusters;

internal static partial class ProjectCreationServiceLogs
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "HEAppE project init: Initialization started for project '{resourceName}' at HEAppE instance '{heappeIdentifier}'.")]
    internal static partial void LogProjectInitializationStart(this ILogger logger, string resourceName, HeappeIdentifier heappeIdentifier);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug, Message = "HEAppE project init: Initialization finished for project '{resourceName}' at HEAppE instance '{heappeIdentifier}'.")]
    internal static partial void LogProjectInitializationEnd(this ILogger logger, string resourceName, HeappeIdentifier heappeIdentifier);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Error, Message = "HEAppE project init: Failed to open a service connection (cause '{cause}') to HEAppE instance '{heappeIdentifier}' for project '{resourceName}'.")]
    internal static partial void LogServiceConnectionFailed(this ILogger logger, HeappeIdentifier heappeIdentifier, string resourceName, string cause);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Error, Message = "HEAppE project init: Failed to retrieve HEAppE project '{resourceName}' from instance '{heappeIdentifier}'.")]
    internal static partial void LogHeappeProjectRetrievalFailed(this ILogger logger, HeappeIdentifier heappeIdentifier, string resourceName);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Error, Message = "HEAppE project init: Failed to create HEAppE project '{resourceName}' on instance '{heappeIdentifier}'.")]
    internal static partial void LogHeappeProjectCreationFailed(this ILogger logger, HeappeIdentifier heappeIdentifier, string resourceName);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Error, Message = "HEAppE project init: Failed to retrieve clusters from HEAppE instance '{heappeIdentifier}'.")]
    internal static partial void LogHeappeClustersRetrievalFailed(this ILogger logger, HeappeIdentifier heappeIdentifier);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Error, Message = "HEAppE project init: HEAppE instance '{heappeIdentifier}' does not contain a cluster matching login node name '{loginNodeName}' (matched on host only, port ignored). Available clusters: [{availableClusters}].")]
    internal static partial void LogHeappeInstanceClusterMissing(this ILogger logger, HeappeIdentifier heappeIdentifier, string loginNodeName, string availableClusters);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Error, Message = "HEAppE project init: Failed to retrieve project-to-cluster assignments for project '{resourceName}' on instance '{heappeIdentifier}'.")]
    internal static partial void LogHeappeProjectAssignmentToClusterRetrievalFailed(this ILogger logger, HeappeIdentifier heappeIdentifier, string resourceName);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Error, Message = "HEAppE project init: Failed to create project-to-cluster assignment for project '{resourceName}' on instance '{heappeIdentifier}' for cluster id '{clusterId}'.")]
    internal static partial void LogHeappeProjectAssignmentToClusterCreationFailed(this ILogger logger, HeappeIdentifier heappeIdentifier, string resourceName, long clusterId);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Information, Message = "HEAppE project init: Project '{resourceName}' on HEAppE instance '{heappeIdentifier}' was updated. Changes: {changes}.")]
    internal static partial void LogHeappeProjectUpdated(this ILogger logger, HeappeIdentifier heappeIdentifier, string resourceName, string changes);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Error, Message = "HEAppE project init: Failed to update project '{resourceName}' on HEAppE instance '{heappeIdentifier}'.")]
    internal static partial void LogHeappeProjectUpdateFailed(this ILogger logger, HeappeIdentifier heappeIdentifier, string resourceName);

    [LoggerMessage(EventId = 1013, Level = LogLevel.Information, Message = "HEAppE project init: Project-to-cluster assignment for project '{resourceName}' on instance '{heappeIdentifier}' updated for cluster id '{clusterId}'. Changes: {changes}.")]
    internal static partial void LogHeappeProjectAssignmentToClusterUpdated(this ILogger logger, HeappeIdentifier heappeIdentifier, string resourceName, long clusterId, string changes);

    [LoggerMessage(EventId = 1014, Level = LogLevel.Error, Message = "HEAppE project init: Failed to update project-to-cluster assignment for project '{resourceName}' on instance '{heappeIdentifier}' for cluster id '{clusterId}'.")]
    internal static partial void LogHeappeProjectAssignmentToClusterUpdateFailed(this ILogger logger, HeappeIdentifier heappeIdentifier, string resourceName, long clusterId);

    [LoggerMessage(EventId = 1015, Level = LogLevel.Error, Message = "HEAppE project init: Failed to retrieve cluster node type aggregations from HEAppE instance '{heappeIdentifier}'.")]
    internal static partial void LogHeappeClusterNodeTypeAggregationRetrievalFailed(this ILogger logger, HeappeIdentifier heappeIdentifier);

    [LoggerMessage(EventId = 1016, Level = LogLevel.Error, Message = "HEAppE project init: HEAppE instance '{heappeIdentifier}' does not contain cluster node type aggregation '{aggregationName}' for project '{resourceName}' (id '{projectId}').")]
    internal static partial void LogHeappeClusterNodeTypeAggregationMissing(this ILogger logger, HeappeIdentifier heappeIdentifier, string resourceName, long projectId, string aggregationName);

    [LoggerMessage(EventId = 1017, Level = LogLevel.Error, Message = "HEAppE project init: Failed to retrieve project cluster node type aggregation for project '{resourceName}' (id '{projectId}') on instance '{heappeIdentifier}'.")]
    internal static partial void LogHeappeProjectClusterNodeTypeAggregationRetrievalFailed(this ILogger logger, HeappeIdentifier heappeIdentifier, string resourceName, long projectId);

    [LoggerMessage(EventId = 1018, Level = LogLevel.Error, Message = "HEAppE project init: Failed to update project cluster node type aggregation (aggregation id '{clusterNodeTypeAggregationId}') for project '{resourceName}' (id '{projectId}') on instance '{heappeIdentifier}'.")]
    internal static partial void LogHeappeProjectClusterNodeTypeAggregationUpdateFailed(this ILogger logger, HeappeIdentifier heappeIdentifier, string resourceName, long projectId, long clusterNodeTypeAggregationId);

    [LoggerMessage(EventId = 1019, Level = LogLevel.Information, Message = "HEAppE project init: Project cluster node type aggregation (aggregation id '{clusterNodeTypeAggregationId}') for project '{resourceName}' (id '{projectId}') on instance '{heappeIdentifier}' was updated. Changes: {changes}.")]
    internal static partial void LogHeappeProjectClusterNodeTypeAggregationUpdated(this ILogger logger, HeappeIdentifier heappeIdentifier, string resourceName, long projectId, long clusterNodeTypeAggregationId, string changes);

    [LoggerMessage(EventId = 1020, Level = LogLevel.Error, Message = "HEAppE project init: Failed to create project cluster node type aggregation (aggregation id '{clusterNodeTypeAggregationId}') for project '{resourceName}' (id '{projectId}') on instance '{heappeIdentifier}'.")]
    internal static partial void LogHeappeProjectClusterNodeTypeAggregationCreationFailed(this ILogger logger, HeappeIdentifier heappeIdentifier, string resourceName, long projectId, long clusterNodeTypeAggregationId);
}