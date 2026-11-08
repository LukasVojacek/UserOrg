using System.Diagnostics;
using System.Diagnostics.Metrics;

using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries.Init;
using Heappe.Contracts.HEappeApiDto.Requests;
using Heappe.Contracts.HEappeApiDto.Responses;
using Heappe.Features.VersionResolver;
using Heappe.Helpers;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.Logging;

using UserOrg.Shared;

namespace Heappe.Features.InitializationProjectWithClusters;

public class ProjectCreationService(
    ILogger<ProjectCreationService> logger,
    IHeappeService heappeService,
    IHeappeVersionResolver heappeVersionResolver,
    IHeappeConnectionFactory connectionFactory) : IProjectCreationService
{
    // OpenTelemetry / System.Diagnostics.Metrics instruments (simple, process-scoped)
    private static readonly Meter s_meter = new("Heappe.InitializationProject", "1.0.0");
    private static readonly Counter<long> s_projectsCreatedCounter = s_meter.CreateCounter<long>("heappe_projects_created_total", description: "Number of HEAppE projects created by the initializer");
    private static readonly Counter<long> s_projectsUpdatedCounter = s_meter.CreateCounter<long>("heappe_projects_updated_total", description: "Number of HEAppE projects updated by the initializer");
    private static readonly Histogram<double> s_projectCreateDurationMs = s_meter.CreateHistogram<double>("heappe_project_create_duration_ms", "ms", "Duration of project create operation in milliseconds");
    private static readonly Histogram<double> s_projectUpdateDurationMs = s_meter.CreateHistogram<double>("heappe_project_update_duration_ms", "ms", "Duration of project update operation in milliseconds");
    public static readonly Version MinimalVersionForPreferredAuthTypeProperty = new Version(6, 4, 0);

    public async Task<Result<long, Error>> InitializeHEAppEProjectWithClustersAsync(HeappeProjectSpecificationEntry projectSpecification, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(projectSpecification);
        logger.LogProjectInitializationStart(projectSpecification.ProjectSpecification.Name, projectSpecification.HeappeIdentifier);

        var connectionResult = await GetServiceHeappeConnectionInfoAsync(projectSpecification.HeappeIdentifier, ct);
        if (!connectionResult.IsSuccess)
        {
            logger.LogServiceConnectionFailed(projectSpecification.HeappeIdentifier, projectSpecification.ProjectSpecification.Name, connectionResult.Error.Code);
            return Result<long, Error>.Failure(connectionResult.Error, connectionResult.State);
        }

        var serviceConnectionInfo = connectionResult.Value;
        var clustersResult = await heappeService.GetClusters(serviceConnectionInfo, ct);
        if (!clustersResult.IsSuccess)
        {
            logger.LogHeappeClustersRetrievalFailed(projectSpecification.HeappeIdentifier);
            return Result<long, Error>.Failure(Errors.FailedToRetrieveHeappeClusters(projectSpecification.HeappeIdentifier));
        }

        var cluster = clustersResult.Value.FirstOrDefault(f => ClusterEndpointMatcher.MatchesMasterNode(f.MasterNodeName, projectSpecification.ClusterSpecification.LoginNodeName));
        if (cluster is null)
        {
            var availableClusters = string.Join("; ", clustersResult.Value.Select(c => $"{c.Name} (masterNode: {c.MasterNodeName})"));
            logger.LogHeappeInstanceClusterMissing(projectSpecification.HeappeIdentifier, projectSpecification.ClusterSpecification.LoginNodeName, availableClusters);
            return Result<long, Error>.Failure(Errors.HeappeInstanceClusterMissing(projectSpecification.HeappeIdentifier, projectSpecification.ClusterSpecification.LoginNodeName));
        }

        var projectResult = await EnsureProjectAsync(projectSpecification, serviceConnectionInfo, ct);
        if (!projectResult.IsSuccess)
        {
            return Result<long, Error>.Failure(projectResult.Error, projectResult.State);
        }

        var ensureProjectClusterAssignmentResult = await EnsureProjectAssignmentToClusterAsync(projectSpecification, serviceConnectionInfo, cluster, projectResult.Value, ct);
        if (!ensureProjectClusterAssignmentResult.IsSuccess)
        {
            return Result<long, Error>.Failure(ensureProjectClusterAssignmentResult.Error, ensureProjectClusterAssignmentResult.State);
        }

        var ensureNodeTypeAggregationResult = await EnsureProjectClusterNodeTypeAggregationAsync(projectSpecification.ClusterSpecification.AllocationSpecification, serviceConnectionInfo, projectResult.Value, ct);
        if (!ensureNodeTypeAggregationResult.IsSuccess)
        {
            return Result<long, Error>.Failure(ensureNodeTypeAggregationResult.Error, ensureNodeTypeAggregationResult.State);
        }


        logger.LogProjectInitializationEnd(projectSpecification.ProjectSpecification.Name, projectSpecification.HeappeIdentifier);
        return Result<long, Error>.Success(projectResult.Value.Id);
    }

    /// <summary>The service-account connection to the HEAppE instance <paramref name="heappeIdentifier"/>, through the NexusApi proxy.</summary>
    public async Task<Result<BaseHeappeConnectionInfo, Error>> GetServiceHeappeConnectionInfoAsync(HeappeIdentifier heappeIdentifier, CancellationToken ct) =>
        await connectionFactory.ForServiceAsync(heappeIdentifier, ct);

    /// <summary>
    /// Ensures a HEAPpe project exists by retrieving an existing project or creating a new one if not found, updating
    /// the project if specifications have changed.
    /// </summary>
    /// <param name="projectSpecification">The HEAPpe project specification containing the project details and target URI.</param>
    /// <param name="serviceConnectionInfo">The connection information for accessing the HEAPpe service.</param>
    /// <param name="ct">The cancellation token to cancel the operation.</param>
    /// <returns>A result containing the project model on success, or an error if retrieval or creation fails.</returns>
    private async Task<Result<ProjectModel, Error>> EnsureProjectAsync(HeappeProjectSpecificationEntry projectSpecification, BaseHeappeConnectionInfo serviceConnectionInfo, CancellationToken ct)
    {
        var projectResult = await heappeService.GetProjectByResourceName(serviceConnectionInfo, projectSpecification.ProjectSpecification.Name, ct);
        if (!projectResult.IsSuccess && projectResult.State != State.NotFound)
        {
            logger.LogHeappeProjectRetrievalFailed(projectSpecification.HeappeIdentifier, projectSpecification.ProjectSpecification.Name);
            return Result<ProjectModel, Error>.Failure(Errors.FailedToRetrieveHeappeProject(projectSpecification.HeappeIdentifier, projectSpecification.ProjectSpecification.Name));
        }

        if (projectResult.IsSuccess)
        {
            projectResult = await UpdateProjectIfNeeded(projectResult.Value, projectSpecification.ProjectSpecification, serviceConnectionInfo, ct);
            if (!projectResult.IsSuccess)
            {
                return Result<ProjectModel, Error>.Failure(projectResult.Error);
            }
        }
        else
        {
            var sw = Stopwatch.StartNew();
            projectResult = await CreateHeappeProjectAsync(projectSpecification.ProjectSpecification, serviceConnectionInfo, ct);
            sw.Stop();

            if (!projectResult.IsSuccess)
            {
                logger.LogHeappeProjectCreationFailed(projectSpecification.HeappeIdentifier, projectSpecification.ProjectSpecification.Name);
                return Result<ProjectModel, Error>.Failure(Errors.FailedToCreateHeappeProject(projectSpecification.HeappeIdentifier, projectSpecification.ProjectSpecification.Name));
            }

            s_projectCreateDurationMs.Record(sw.Elapsed.TotalMilliseconds);
            s_projectsCreatedCounter.Add(1);
        }

        return Result<ProjectModel, Error>.Success(projectResult.Value);
    }

    /// <summary>
    /// Creates a project in Heappe using the provided specification and connection information.
    /// </summary>
    /// <param name="projectSpecification">Specification containing project name, description, dates, and configuration.</param>
    /// <param name="serviceConnectionInfo">Connection information for the Heappe service.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A result containing the created project model on success, or an error on failure.</returns>
    private async Task<Result<ProjectModel, Error>> CreateHeappeProjectAsync(ProjectSpecificationEntry projectSpecification, BaseHeappeConnectionInfo serviceConnectionInfo, CancellationToken ct)
    {
        var truncatedDescription = projectSpecification.Description?[..Math.Min(100, projectSpecification.Description.Length)];
        var createProjectRequest = new CreateProjectRequest(
            projectSpecification.Name,
            truncatedDescription,
            projectSpecification.AccountingString,
            projectSpecification.StartDate,
            projectSpecification.EndDate,
            projectSpecification.UsageType,
            projectSpecification.UseAccountingStringForScheduler,
            projectSpecification.PIEmail,
            projectSpecification.IsOneToOneMapping
         );

        return await heappeService.CreateProject(serviceConnectionInfo, createProjectRequest, ct);
    }

    /// <summary>
    /// Updates the specified project if any of its properties differ from the desired specification.
    /// </summary>
    /// <remarks>No update is performed if the existing project already matches the desired specification. The
    /// method communicates with the HEAppE service to apply updates when needed.</remarks>
    /// <param name="existing">The current project model representing the existing state of the project.</param>
    /// <param name="desired">The desired project specification containing the target state for the project.</param>
    /// <param name="serviceConnectionInfo">The connection information required to communicate with the HEAppE service.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A result containing the updated project model if changes were applied, or the original project model if no
    /// update was necessary. Returns an error if the update operation fails.</returns>
    private async Task<Result<ProjectModel, Error>> UpdateProjectIfNeeded(
        ProjectModel existing,
        ProjectSpecificationEntry desired,
        BaseHeappeConnectionInfo serviceConnectionInfo,
        CancellationToken ct)
    {
        var changes = new List<string>();

        if (!string.Equals(existing.Name, desired.Name, StringComparison.Ordinal))
        {
            changes.Add($"Name: '{existing.Name}' -> '{desired.Name}'");
        }

        if (!string.Equals(existing.Description ?? string.Empty, desired.Description ?? string.Empty, StringComparison.Ordinal))
        {
            changes.Add($"Description: '{existing.Description}' -> '{desired.Description}'");
        }

        if (existing.StartDate != desired.StartDate)
        {
            changes.Add($"StartDate: '{existing.StartDate:O}' -> '{desired.StartDate:O}'");
        }

        if (existing.EndDate != desired.EndDate)
        {
            changes.Add($"EndDate: '{existing.EndDate:O}' -> '{desired.EndDate:O}'");
        }

        if (existing.UsageType != (int)desired.UsageType)
        {
            changes.Add($"UsageType: '{existing.UsageType}' -> '{desired.UsageType}'");
        }

        if (existing.UseAccountingStringForScheduler != desired.UseAccountingStringForScheduler)
        {
            changes.Add($"UseAccountingStringForScheduler: '{existing.UseAccountingStringForScheduler}' -> '{desired.UseAccountingStringForScheduler}'");
        }

        if (existing.IsOneToOneMapping != desired.IsOneToOneMapping)
        {
            changes.Add($"IsOneToOneMapping: '{existing.IsOneToOneMapping}' -> '{desired.IsOneToOneMapping}'");
        }

        if (changes.Count == 0)
        {
            return Result<ProjectModel, Error>.Success(existing, State.Ok);
        }

        var sw = Stopwatch.StartNew();
        var truncatedDescription = desired.Description?[..Math.Min(100, desired.Description.Length)];
        var updateRequest = new UpdateProjectRequest(
            existing.Id,
            desired.Name,
            truncatedDescription,
            desired.StartDate,
            desired.EndDate,
            desired.UsageType,
            desired.UseAccountingStringForScheduler,
            desired.IsOneToOneMapping
        );

        var updateResult = await heappeService.UpdateProject(serviceConnectionInfo, updateRequest, ct);
        sw.Stop();

        if (!updateResult.IsSuccess)
        {
            logger.LogHeappeProjectUpdateFailed(serviceConnectionInfo.Identifier, desired.Name);
            return Result<ProjectModel, Error>.Failure(Errors.FailedToUpdateHeappeProject(serviceConnectionInfo.Identifier, desired.Name));
        }

        s_projectUpdateDurationMs.Record(sw.Elapsed.TotalMilliseconds);
        s_projectsUpdatedCounter.Add(1);

        var changesSummary = string.Join("; ", changes);
        logger.LogHeappeProjectUpdated(serviceConnectionInfo.Identifier, desired.Name, changesSummary);
        return updateResult;
    }


    /// <summary>
    /// Ensures a project is assigned to the specified cluster, creating the assignment if it doesn't exist or updating
    /// it if changes are needed.
    /// </summary>
    /// <param name="projectSpecification">Specification containing cluster assignment details for the project.</param>
    /// <param name="serviceConnectionInfo">Connection information for the Heappe service.</param>
    /// <param name="cluster">Target cluster for the project assignment.</param>
    /// <param name="project">Project to be assigned to the cluster.</param>
    /// <param name="ct">Token to cancel the asynchronous operation.</param>
    /// <returns>Success if the assignment was ensured; otherwise, a failure result containing the error.</returns>
    private async Task<Result<Error>> EnsureProjectAssignmentToClusterAsync(HeappeProjectSpecificationEntry projectSpecification, BaseHeappeConnectionInfo serviceConnectionInfo, ClusterDetailedModel cluster, ProjectModel project, CancellationToken ct)
    {
        var projectClustersAssignmentResult = await heappeService.GetProjectAssignmentToClustersAsync(serviceConnectionInfo, project.Id, ct);
        if (!projectClustersAssignmentResult.IsSuccess)
        {
            logger.LogHeappeProjectAssignmentToClusterRetrievalFailed(serviceConnectionInfo.Identifier, project.AccountingString);
            return Result<Error>.Failure(Errors.FailedToRetrieveHeappeProjectAssignmentToCluster(serviceConnectionInfo.Identifier, project.AccountingString));
        }

        var projectClusterAssignment = projectClustersAssignmentResult.Value.FirstOrDefault(f => f.ClusterId == cluster.Id);
        if (projectClusterAssignment != null)
        {
            var updateProjectAssignmentResult = await UpdateProjectAssignmentIfNeeded(projectClusterAssignment, projectSpecification.ClusterSpecification, project.AccountingString, serviceConnectionInfo, ct);
            if (!updateProjectAssignmentResult.IsSuccess)
            {
                return Result<Error>.Failure(updateProjectAssignmentResult.Error);
            }
        }
        else
        {
            var clusterProjectAssignmentRequest = await PrepareClusterProjectRequest(projectSpecification, serviceConnectionInfo.Identifier, cluster, project, ct);

            var projectAssignmentResult = await heappeService.CreateProjectAssignmentToClusterAsync(serviceConnectionInfo, clusterProjectAssignmentRequest, ct);
            if (!projectAssignmentResult.IsSuccess)
            {
                logger.LogHeappeProjectAssignmentToClusterCreationFailed(serviceConnectionInfo.Identifier, project.AccountingString, cluster.Id);
                return Result<Error>.Failure(Errors.FailedToCreateHeappeProjectAssignmentToCluster(serviceConnectionInfo.Identifier, project.AccountingString, cluster.Id));
            }
        }

        return Result<Error>.Success();
    }

    /// <summary>
    /// Prepares a cluster project assignment request with version-appropriate properties based on the Heappe instance
    /// version.
    /// </summary>
    /// <param name="projectSpecification">The project specification containing cluster storage paths and authentication preferences.</param>
    /// <param name="heappeIdentifier">The identifier of the Heappe instance to query for version information.</param>
    /// <param name="cluster">The cluster to which the project will be assigned.</param>
    /// <param name="project">The project to assign to the cluster.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>A request object appropriate for the Heappe instance version, including authentication type if supported.</returns>
    private async Task<CreateProjectAssignmentToClusterRequest> PrepareClusterProjectRequest(HeappeProjectSpecificationEntry projectSpecification, HeappeIdentifier heappeIdentifier, ClusterDetailedModel cluster, ProjectModel project, CancellationToken ct)
    {
        var version = await heappeVersionResolver.GetVersion(heappeIdentifier, ct);
        if (version < MinimalVersionForPreferredAuthTypeProperty)
        {
            return new CreateProjectAssignmentToClusterRequest(
                cluster.Id,
                project.Id,
                projectSpecification.ClusterSpecification.ScratchStoragePath,
                projectSpecification.ClusterSpecification.ProjectStoragePath
            );

        }
        var clusterProjectAssignmentRequest = new CreateProjectAssignmentToClusterRequestV2(
            cluster.Id,
            project.Id,
            projectSpecification.ClusterSpecification.ScratchStoragePath,
            projectSpecification.ClusterSpecification.ProjectStoragePath,
            projectSpecification.ProjectSpecification.PreferredAuthType
        );

        return clusterProjectAssignmentRequest;
    }

    /// <summary>
    /// Updates an existing project assignment to a cluster if the desired specification differs from the current
    /// assignment.
    /// </summary>
    /// <remarks>The method only performs an update if the storage paths in the desired specification differ
    /// from those in the existing assignment. Logging is performed for both successful and failed update
    /// attempts.</remarks>
    /// <param name="existing">The current project assignment to the cluster to be evaluated and potentially updated.</param>
    /// <param name="desired">The desired cluster specification entry containing the target storage paths.</param>
    /// <param name="resourceName">The name of the resource associated with the project assignment, used for logging and error reporting.</param>
    /// <param name="serviceConnectionInfo">The connection information required to communicate with the HEAppE service.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A result containing the updated project assignment to the cluster if the update succeeds; otherwise, an error
    /// describing the failure.</returns>
    private async Task<Result<ProjectAssignmentToClusterModel, Error>> UpdateProjectAssignmentIfNeeded(
        ProjectAssignmentToClusterModel existing,
        ClusterSpecificationEntry desired,
        string resourceName,
        BaseHeappeConnectionInfo serviceConnectionInfo,
        CancellationToken ct)
    {
        var changes = new List<string>();
        var updateAssignmentRequest = new UpdateProjectAssignmentToClusterRequest(
            existing.ClusterId,
            existing.ProjectId,
            desired.ScratchStoragePath,
            desired.ProjectStoragePath
        );

        if (!string.Equals(existing.ProjectStoragePath, updateAssignmentRequest.ProjectStoragePath, StringComparison.Ordinal))
        {
            changes.Add($"ProjectStoragePath: '{existing.ProjectStoragePath}' -> '{updateAssignmentRequest.ProjectStoragePath}'");
        }

        if (!string.Equals(existing.ScratchStoragePath, updateAssignmentRequest.ScratchStoragePath, StringComparison.Ordinal))
        {
            changes.Add($"ScratchStoragePath: '{existing.ScratchStoragePath}' -> '{updateAssignmentRequest.ScratchStoragePath}'");
        }


        if (changes.Count == 0)
        {
            return Result<ProjectAssignmentToClusterModel, Error>.Success(existing, State.Ok);
        }

        var updateAssignmentResult = await heappeService.UpdateProjectAssignmentToClusterAsync(serviceConnectionInfo, updateAssignmentRequest, ct);
        if (!updateAssignmentResult.IsSuccess)
        {
            logger.LogHeappeProjectAssignmentToClusterUpdateFailed(serviceConnectionInfo.Identifier, resourceName, existing.ClusterId);
            return Result<ProjectAssignmentToClusterModel, Error>.Failure(Errors.FailedToUpdateHeappeProjectAssignmentToCluster(serviceConnectionInfo.Identifier, resourceName, existing.ClusterId));
        }

        var changesSummary = string.Join("; ", changes);
        logger.LogHeappeProjectAssignmentToClusterUpdated(serviceConnectionInfo.Identifier, resourceName, existing.ClusterId, changesSummary);
        return updateAssignmentResult;
    }

    /// <summary>
    /// Initializes or updates the cluster node type aggregation for a project in Heappe based on the allocation
    /// specification. Skips initialization if no aggregation name is specified.
    /// </summary>
    /// <remarks>When <see cref="AllocationSpecificationEntry.NodeTypeAggregationName"/> is null or empty, the
    /// method returns success without performing any operations, allowing projects to opt out of this
    /// feature.</remarks>
    /// <param name="allocationSpecification">Allocation specification containing the node type aggregation name and allocation amount.</param>
    /// <param name="serviceConnectionInfo">Connection information for the Heappe service.</param>
    /// <param name="project">Project for which to initialize the cluster node type aggregation.</param>
    /// <param name="ct">Token to cancel the asynchronous operation.</param>
    /// <returns>Success if the aggregation was initialized, updated, or skipped; failure with error details if retrieval,
    /// creation, or update operations fail.</returns>
    private async Task<Result<Error>> EnsureProjectClusterNodeTypeAggregationAsync(AllocationSpecificationEntry allocationSpecification, BaseHeappeConnectionInfo serviceConnectionInfo, ProjectModel project, CancellationToken ct)
    {

        // If NodeTypeAggregationName is not specified, we skip the initialization of cluster node type aggregation as it's not required. This allows for flexibility in project specifications that do not need this feature.
        if (string.IsNullOrEmpty(allocationSpecification.NodeTypeAggregationName))
        {
            return Result<Error>.Success();
        }


        var clusterAggregationsResult = await heappeService.GetClusterNodeTypeAggregations(serviceConnectionInfo, ct);
        if (!clusterAggregationsResult.IsSuccess)
        {
            logger.LogHeappeClusterNodeTypeAggregationRetrievalFailed(serviceConnectionInfo.Identifier);
            return Result<Error>.Failure(Errors.FailedToRetrieveHeappeClusterNodeTypeAggregation(serviceConnectionInfo.Identifier));
        }

        var clusterNodeTypeAggregation = clusterAggregationsResult.Value.FirstOrDefault(w => w.Name.Equals(allocationSpecification.NodeTypeAggregationName, StringComparison.OrdinalIgnoreCase));
        if (clusterNodeTypeAggregation == null)
        {
            logger.LogHeappeClusterNodeTypeAggregationMissing(serviceConnectionInfo.Identifier, project.AccountingString, project.Id, allocationSpecification.NodeTypeAggregationName);
            return Result<Error>.Failure(Errors.HeappeClusterNodeTypeAggregationMissing(serviceConnectionInfo.Identifier, project.AccountingString, project.Id, allocationSpecification.NodeTypeAggregationName));
        }

        var projectClusterNodeTypeAggregationsResult = await heappeService.GetProjectClusterNodeTypeAggregationsAsync(serviceConnectionInfo, project.Id, ct);
        if (!projectClusterNodeTypeAggregationsResult.IsSuccess)
        {
            logger.LogHeappeProjectClusterNodeTypeAggregationRetrievalFailed(serviceConnectionInfo.Identifier, project.AccountingString, project.Id);
            return Result<Error>.Failure(Errors.FailedToRetrieveHeappeProjectClusterNodeTypeAggregation(serviceConnectionInfo.Identifier, project.AccountingString, project.Id));
        }

        var projectClusterNodeTypeAggregation = projectClusterNodeTypeAggregationsResult.Value.FirstOrDefault(f => f.ClusterNodeTypeAggregationId == clusterNodeTypeAggregation.Id);
        if (projectClusterNodeTypeAggregation != null)
        {
            var updateNodeTypeAggregationResult = await UpdateProjectClusterNodeTypeAggregationIfNeeded(
                projectClusterNodeTypeAggregation,
                project.AccountingString,
                allocationSpecification.AllocationAmount,
                serviceConnectionInfo,
                ct);

            if (!updateNodeTypeAggregationResult.IsSuccess)
            {
                return Result<Error>.Failure(updateNodeTypeAggregationResult.Error);
            }
        }
        else
        {
            var createProjectAggregationRequest = new CreateProjectClusterNodeTypeAggregationRequest(
                clusterNodeTypeAggregation.Id,
                project.Id,
                allocationSpecification.AllocationAmount
            );

            var createProjectClusterNodeTypeAggregationResult = await heappeService.CreateProjectClusterNodeTypeAggregationAsync(serviceConnectionInfo, createProjectAggregationRequest, ct);
            if (!createProjectClusterNodeTypeAggregationResult.IsSuccess)
            {
                logger.LogHeappeProjectClusterNodeTypeAggregationCreationFailed(serviceConnectionInfo.Identifier, project.AccountingString, createProjectAggregationRequest.ProjectId, createProjectAggregationRequest.ClusterNodeTypeAggregationId);
                return Result<Error>.Failure(Errors.FailedToCreateHeappeProjectClusterNodeTypeAggregation(serviceConnectionInfo.Identifier, project.AccountingString, createProjectAggregationRequest.ProjectId, createProjectAggregationRequest.ClusterNodeTypeAggregationId));
            }
        }

        return Result<Error>.Success();
    }

    /// <summary>
    /// Updates the project cluster node type aggregation allocation amount if it differs from the existing value.
    /// </summary>
    /// <param name="existing">The existing project cluster node type aggregation model.</param>
    /// <param name="resourceName">The resource name for logging purposes.</param>
    /// <param name="allocationAmmount">The new allocation amount to set.</param>
    /// <param name="serviceConnectionInfo">The Heappe service connection information.</param>
    /// <param name="ct">The cancellation token to observe.</param>
    /// <returns>A result containing the updated aggregation model on success, or an error on failure. Returns the existing model
    /// unchanged if the allocation amount has not changed.</returns>
    private async Task<Result<ProjectClusterNodeTypeAggregationModel, Error>> UpdateProjectClusterNodeTypeAggregationIfNeeded(
      ProjectClusterNodeTypeAggregationModel existing,
      string resourceName,
      long allocationAmmount,
      BaseHeappeConnectionInfo serviceConnectionInfo,
      CancellationToken ct)
    {
        var updateClusterNodeTypeAggregationRequest = new UpdateProjectClusterNodeTypeAggregationRequest(
            existing.ClusterNodeTypeAggregationId,
            existing.ProjectId,
            allocationAmmount
        );

        if (existing.AllocationAmount == updateClusterNodeTypeAggregationRequest.AllocationAmount)
        {
            return Result<ProjectClusterNodeTypeAggregationModel, Error>.Success(existing, State.Ok);
        }

        var updateProjectClusterNodeTypeAggregationResult = await heappeService.UpdateProjectClusterNodeTypeAggregationAsync(serviceConnectionInfo, updateClusterNodeTypeAggregationRequest, ct);
        if (!updateProjectClusterNodeTypeAggregationResult.IsSuccess)
        {
            logger.LogHeappeProjectClusterNodeTypeAggregationUpdateFailed(serviceConnectionInfo.Identifier, resourceName, existing.ProjectId, existing.ClusterNodeTypeAggregationId);
                return Result<ProjectClusterNodeTypeAggregationModel, Error>.Failure(Errors.FailedToUpdateHeappeProjectClusterNodeTypeAggregation(serviceConnectionInfo.Identifier, resourceName, existing.ProjectId, existing.ClusterNodeTypeAggregationId));
        }

        var changesSummary = $"AllocationAmount: '{existing.AllocationAmount}' -> '{updateClusterNodeTypeAggregationRequest.AllocationAmount}'";
        logger.LogHeappeProjectClusterNodeTypeAggregationUpdated(serviceConnectionInfo.Identifier, resourceName, existing.ProjectId, existing.ClusterNodeTypeAggregationId, changesSummary);
        return updateProjectClusterNodeTypeAggregationResult;
    }
}