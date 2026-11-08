using System.Net;

using Heappe.Contracts;

using Lukdrasil.Error;

using UserOrg.Shared;

namespace Heappe;

internal static partial class Errors
{
    // A transport-level failure (the data call never got an HTTP response) — the instance is effectively
    // unreachable for this call (ADR 0074).
    internal static Error FailedToRetrieveResponse(string requestUrl, string responseObjName) => new ErrorBuilder()
        .WithCode(HeappeFailureCause.Unreachable)
        .WithDescription("Failed to retrieve response type '{0}' from '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(responseObjName, requestUrl);

    // The cause comes from NexusProblemMapper: the problem type NexusApi stamps on its own failures, else the status.
    internal static Error FailedToRetrieveResponse(string requestUrl, HttpStatusCode statusCode, string responseContent, string cause) => new ErrorBuilder()
        .WithCode(cause)
        .WithDescription("Failed to retrieve response from '{0}'. Status code: '{1}'. Response text: '{2}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(requestUrl, statusCode, responseContent);

    internal static Error FailedToDeserializeResponse(string responseObjName, string responseContent, string requestUri) => new ErrorBuilder()
        .WithCode(HeappeFailureCause.InvalidResponse)
        .WithDescription("Failed to deserialize response from '{0}' to type '{1}'. Response content: '{2}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(requestUri, responseObjName, responseContent);

    internal static Error FailedToRetrieveResponseTimeout(string requestUrl, string responseObjName) => new ErrorBuilder()
        .WithCode(HeappeFailureCause.Timeout)
        .WithDescription("Failed to retrieve response type '{0}' due to timeout from HEAppE '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(responseObjName, requestUrl);

    internal static Error HeappeCatalogWithoutUsableData(HeappeIdentifier identifier) => new ErrorBuilder()
        .WithCode(HeappeFailureCause.InvalidResponse)
        .WithDescription("HEAppE instance '{0}' returned a cluster catalog without any node type carrying project allocations (mid-rebuild skeleton); not cached.")
        .WithSeverity(ErrorSeverity.Warning)
        .Build(identifier);

    internal static Error NexusUnavailable(Exception cause) => new ErrorBuilder()
        .WithCode(HeappeFailureCause.NexusUnavailable)
        .WithDescription("NexusApi, the HEAppE proxy and registry, could not be reached: {0}")
        .WithSeverity(ErrorSeverity.Error)
        .Build(cause.Message);

    internal static Error HeappeInstanceNotFound(HeappeIdentifier identifier) => new ErrorBuilder()
        .WithCode(HeappeFailureCause.HeappeInstanceNotFound)
        .WithDescription("NexusApi lists no enabled HEAppE instance with identifier '{0}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(identifier);

    internal static Error TokenExchangeUnavailable(HeappeIdentifier identifier) => new ErrorBuilder()
        .WithCode(HeappeFailureCause.TokenExchangeUnavailable)
        .WithDescription("NexusApi lists no usable Keycloak client and scopes for HEAppE instance '{0}'; the caller token cannot be exchanged.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(identifier);

    internal static Error TokenExchangeFailed(HeappeIdentifier identifier, Exception? cause) => new ErrorBuilder()
        .WithCode(HeappeFailureCause.TokenExchangeFailed)
        .WithDescription("The caller token could not be exchanged for HEAppE instance '{0}': {1}.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(identifier, cause?.GetType().Name ?? "the exchange client is not configured");

    internal static Error FailedToRetrieveListAvailableClustersMethod(string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to retrieve list of available clusters from HEAppE instance at '{0}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(heappeAddress);

    internal static Error FailedToRetrieveListClustersMethod(string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to retrieve list of clusters from HEAppE instance at '{0}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(heappeAddress);

    internal static Error FailedToRetrieveProjectByResourceName(string resourceName, string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to retrieve project '{0}' from HEAppE instance at '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(resourceName, heappeAddress);

    internal static Error FailedToCreateProject(string resourceName, string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to create project '{0}' from HEAppE instance at '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(resourceName, heappeAddress);

    internal static Error FailedToCreateProjectAssignmentCluster(long projectId, long clusterId, string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to create project assignment cluster for project '{0}', and cluster '{1}' from HEAppE instance at '{2}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(projectId, clusterId, heappeAddress);

    internal static Error FailedToRetrieveCommandTemplatesForProject(long? projectId, string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to retrieve list of project command templates for project {0} from HEAppE instance at '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(projectId, heappeAddress);

    internal static Error FailedToTestClusterAccessForAccount(string accountName, long projectId, string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to test cluster access for account '{0}' and cluster '{1}' from HEAppE instance at '{2}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(accountName, projectId, heappeAddress);

    internal static Error FailedToGenerateSecureShellKey(string accountName, long projectId, string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to generate secure shell key for account '{0}' and cluster '{1}' from HEAppE instance at '{2}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(accountName, projectId, heappeAddress);

    internal static Error FailedToRetrieveAssignmentClustersForProject(long? projectId, string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to retrieve list of assignment clusters for project {0} from HEAppE instance at '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(projectId, heappeAddress);

    internal static Error FailedToCreateCommandTemplateForProject(long projectId, string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to create command template for project {0} from HEAppE instance at '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(projectId, heappeAddress);

    internal static Error FailedToModifyCommandTemplate(long commandTemplateId, string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to modify command template for project {0} from HEAppE instance at '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(commandTemplateId, heappeAddress);

    internal static Error FailedToRetrieveListChangedFilesForJob(long submittedJobInfoId, string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to retrieve list of changed files for job {0} from HEAppE instance at '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(submittedJobInfoId, heappeAddress);

    internal static Error FailedToDownloadFileFromJob(long submittedJobInfoId, string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to download file for job {0} from HEAppE instance at '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(submittedJobInfoId, heappeAddress);

    internal static Error FailedToRetrieveSessionCode(string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to retrieve session code from HEAppE instance at '{0}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(heappeAddress);

    internal static Error FailedToUpdateProjectAssignmentCluster(long projectId, long clusterId, string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to update project assignment cluster for project '{0}', and cluster '{1}' from HEAppE instance at '{2}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(projectId, clusterId, heappeAddress);

    internal static Error FailedToRetrieveClusterAuthenticationCredentialsAuthTypes(string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to retrieve cluster authentication credential authentication types from HEAppE instance at '{0}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(heappeAddress);

    internal static Error VersionNotSupported(string heappeAddress, Version version) => new ErrorBuilder()
        .WithDescription("HEAppE instance at '{0}' does not support required API version {1}.")
        .WithSeverity(ErrorSeverity.Warning)
        .Build(heappeAddress, version);


    internal static Error FailedToListHeappeClusters(HeappeIdentifier heappeIdentifier) => new ErrorBuilder()
        .WithDescription("Failed to list clusters on HEAppE instance for HEAppE identifier '{0}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(heappeIdentifier);

    internal static Error FailedToFindHeappeCluster(HeappeIdentifier heappeIdentifier, string clusterName) => new ErrorBuilder()
        .WithDescription("Failed to find HEAppE cluster '{0}' on HEAppE instance for HEAppE identifier '{1}'")
        .WithSeverity(ErrorSeverity.Error)
        .Build(clusterName, heappeIdentifier);

    internal static Error FailedToRetrieveListClusterNodeTypeAggregationsMethod(string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to retrieve list of cluster node type aggregations from HEAppE instance at '{0}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(heappeAddress);

    internal static Error FailedToRetrieveProjectClusterNodeTypeAggregation(long projectId, string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to retrieve project cluster node type aggregation for project '{0}' from HEAppE instance at '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(projectId, heappeAddress);

    internal static Error FailedToCreateProjectClusterNodeTypeAggregation(long clusterNodeTypeAggregationId, long projectId, string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to create project cluster node type aggregation for ClusterNodeTypeAggregationId '{0}' and project '{1}' from HEAppE instance at '{2}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(clusterNodeTypeAggregationId, projectId, heappeAddress);

    internal static Error FailedToUpdateProjectClusterNodeTypeAggregation(long clusterNodeTypeAggregationId, long projectId, string heappeAddress) => new ErrorBuilder()
        .WithDescription("Failed to update project cluster node type aggregation for ClusterNodeTypeAggregationId '{0}' and project '{1}' from HEAppE instance at '{2}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(clusterNodeTypeAggregationId, projectId, heappeAddress);

    internal static Error FailedToRetrieveActiveCommandTemplates(Guid projectId) => new ErrorBuilder()
        .WithDescription("Failed to retrieve active command templates for project '{0}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(projectId);
}