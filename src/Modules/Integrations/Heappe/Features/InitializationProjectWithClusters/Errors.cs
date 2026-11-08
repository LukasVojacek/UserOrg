using Lukdrasil.Error;

using UserOrg.Shared;

namespace Heappe.Features.InitializationProjectWithClusters;

internal static partial class Errors
{
    internal static Error FailedToRetrieveHeappeProject(HeappeIdentifier heappeIdentifier, string projectShortName) => new ErrorBuilder()
        .WithDescription("Failed to retrieve project '{0}' from HEAppE instance '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(projectShortName, heappeIdentifier);

    internal static Error FailedToCreateHeappeProject(HeappeIdentifier heappeIdentifier, string projectShortName) => new ErrorBuilder()
        .WithDescription("Failed to create project '{0}' on HEAppE instance '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(projectShortName, heappeIdentifier);

    internal static Error FailedToUpdateHeappeProject(HeappeIdentifier heappeIdentifier, string projectShortName) => new ErrorBuilder()
        .WithDescription("Failed to update project '{0}' on HEAppE instance '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(projectShortName, heappeIdentifier);

    internal static Error FailedToRetrieveHeappeClusters(HeappeIdentifier heappeIdentifier) => new ErrorBuilder()
        .WithDescription("Failed to retrieve clusters from HEAppE instance '{0}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(heappeIdentifier);

    internal static Error HeappeInstanceClusterMissing(HeappeIdentifier heappeIdentifier, string loginNodeName) => new ErrorBuilder()
        .WithDescription("HEAppE instance '{0}' does not contain a cluster with login node name '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(heappeIdentifier, loginNodeName);

    internal static Error FailedToRetrieveHeappeProjectAssignmentToCluster(HeappeIdentifier heappeIdentifier, string projectShortName) => new ErrorBuilder()
        .WithDescription("Failed to retrieve project-to-cluster assignments for project '{0}' on HEAppE instance '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(projectShortName, heappeIdentifier);

    internal static Error FailedToCreateHeappeProjectAssignmentToCluster(HeappeIdentifier heappeIdentifier, string projectShortName, long clusterId) => new ErrorBuilder()
        .WithDescription("Failed to create project-to-cluster assignment for project '{0}' on HEAppE instance '{1}' for cluster id '{2}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(projectShortName, heappeIdentifier, clusterId);

    internal static Error FailedToUpdateHeappeProjectAssignmentToCluster(HeappeIdentifier heappeIdentifier, string projectShortName, long clusterId) => new ErrorBuilder()
        .WithDescription("Failed to update project-to-cluster assignment for project '{0}' on HEAppE instance '{1}' for cluster id '{2}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(projectShortName, heappeIdentifier, clusterId);

    internal static Error FailedToRetrieveHeappeClusterNodeTypeAggregation(HeappeIdentifier heappeIdentifier) => new ErrorBuilder()
        .WithDescription("Failed to retrieve cluster node type aggregations from HEAppE instance '{0}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(heappeIdentifier);

    internal static Error HeappeClusterNodeTypeAggregationMissing(HeappeIdentifier heappeIdentifier, string projectShortName, long projectId, string aggregationName) => new ErrorBuilder()
        .WithDescription("HEAppE instance '{0}' does not contain cluster node type aggregation '{1}' for project '{2}' (id '{3}').")
        .WithSeverity(ErrorSeverity.Error)
        .Build(heappeIdentifier, aggregationName, projectShortName, projectId);

    internal static Error FailedToRetrieveHeappeProjectClusterNodeTypeAggregation(HeappeIdentifier heappeIdentifier, string projectShortName, long projectId) => new ErrorBuilder()
        .WithDescription("Failed to retrieve project cluster node type aggregation for project '{0}' (id '{1}') on HEAppE instance '{2}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(projectShortName, projectId, heappeIdentifier);

    internal static Error FailedToUpdateHeappeProjectClusterNodeTypeAggregation(HeappeIdentifier heappeIdentifier, string projectShortName, long projectId, long clusterNodeTypeAggregationId) => new ErrorBuilder()
        .WithDescription("Failed to update project cluster node type aggregation (aggregation id '{0}') for project '{1}' (id '{2}') on HEAppE instance '{3}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(clusterNodeTypeAggregationId, projectShortName, projectId, heappeIdentifier);

    internal static Error FailedToCreateHeappeProjectClusterNodeTypeAggregation(HeappeIdentifier heappeIdentifier, string projectShortName, long projectId, long clusterNodeTypeAggregationId) => new ErrorBuilder()
        .WithDescription("Failed to create project cluster node type aggregation (aggregation id '{0}') for project '{1}' (id '{2}') on HEAppE instance '{3}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(clusterNodeTypeAggregationId, projectShortName, projectId, heappeIdentifier);
}