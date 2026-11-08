using Heappe.Contracts.CommandTemplate;
using Heappe.Contracts.Connection;
using Heappe.Contracts.HEappeApiDto.Requests;
using Heappe.Contracts.HEappeApiDto.Responses;

using Lukdrasil.Error;
using Lukdrasil.Result;

namespace Heappe.Contracts;

public interface IHeappeService
{
    ValueTask<Result<IList<ClusterDetailedModel>, Error>> GetClusters(BaseHeappeConnectionInfo heappeConnInfo, CancellationToken ct);
    ValueTask<Result<IList<ClusterInfoModel>, Error>> GetListAvailableClusters(BaseHeappeConnectionInfo heappeConnInfo, IEnumerable<KeyValuePair<string, string?>>? queryParameters = null, CancellationToken ct = default);
    ValueTask<Result<IList<ClusterNodeTypeAggregationModel>, Error>> GetClusterNodeTypeAggregations(BaseHeappeConnectionInfo heappeConnInfo, CancellationToken ct);
    ValueTask<Result<ProjectModel, Error>> GetProjectByResourceName(BaseHeappeConnectionInfo heappeConnInfo, string resourceName, CancellationToken ct = default);
    ValueTask<Result<ProjectModel, Error>> CreateProject(BaseHeappeConnectionInfo heappeConnInfo, CreateProjectRequest projectRequest, CancellationToken ct);
    ValueTask<Result<ProjectModel, Error>> UpdateProject(BaseHeappeConnectionInfo heappeConnInfo, UpdateProjectRequest projectRequest, CancellationToken ct);
    ValueTask<Result<IList<ProjectAssignmentToClusterModel>, Error>> GetProjectAssignmentToClustersAsync(BaseHeappeConnectionInfo heappeConnInfo, long projectId, CancellationToken ct);
    ValueTask<Result<ProjectAssignmentToClusterModel, Error>> CreateProjectAssignmentToClusterAsync(BaseHeappeConnectionInfo heappeConnInfo, CreateProjectAssignmentToClusterRequest projectAssignmentClusterRequest, CancellationToken ct);
    ValueTask<Result<ProjectAssignmentToClusterModel, Error>> UpdateProjectAssignmentToClusterAsync(BaseHeappeConnectionInfo heappeConnInfo, UpdateProjectAssignmentToClusterRequest projectAssignmentClusterRequest, CancellationToken ct);
    ValueTask<Result<IList<ProjectClusterNodeTypeAggregationModel>, Error>> GetProjectClusterNodeTypeAggregationsAsync(BaseHeappeConnectionInfo heappeConnInfo, long projectId, CancellationToken ct);
    ValueTask<Result<ProjectClusterNodeTypeAggregationModel, Error>> CreateProjectClusterNodeTypeAggregationAsync(BaseHeappeConnectionInfo heappeConnInfo, CreateProjectClusterNodeTypeAggregationRequest projectClusterNodeTypeAggregationRequest, CancellationToken ct);
    ValueTask<Result<ProjectClusterNodeTypeAggregationModel, Error>> UpdateProjectClusterNodeTypeAggregationAsync(BaseHeappeConnectionInfo heappeConnInfo, UpdateProjectClusterNodeTypeAggregationRequest projectClusterNodeTypeAggregationRequest, CancellationToken ct);
    ValueTask<Result<IList<CommandTemplateModel>, Error>> GetProjectCommandTemplatesAsync(BaseHeappeConnectionInfo heappeConnInfo, long projectId, CancellationToken ct = default);
    ValueTask<Result<CommandTemplateModel, Error>> CreateGenericCommandTemplateAsync(BaseHeappeConnectionInfo heappeConnInfo, GenericCommandTemplateCreateModel createModel, CancellationToken ct = default);
    ValueTask<Result<CommandTemplateModel, Error>> CreateCommandTemplateAsync(BaseHeappeConnectionInfo heappeConnInfo, CommandTemplateCreateModel createModel, CancellationToken ct = default);
    ValueTask<Result<bool, Error>> ModifyComandTemplateAsync(BaseHeappeConnectionInfo heappeConnInfo, CommandTemplateUpdateModel modifyModel, CancellationToken ct = default);
    ValueTask<Result<Dictionary<string, DownloadedFileModel>, Error>> DownloadChangedFilesAsync(BaseHeappeConnectionInfo heappeConnInfo, long submittedJobInfoId, CancellationToken ct = default);
    Task<Result<HashSet<CommandTemplatePartitionResponse>, Error>> GetActiveCommandTemplatesAsync(Guid projectId, CancellationToken ct);
    ValueTask<Result<IList<ClusterAuthenticationCredentialAuthType>, Error>> GetClusterAuthenticationCredentialsAuthTypesAsync(BaseHeappeConnectionInfo heappeConnInfo, CancellationToken ct = default);
    ValueTask<Result<IList<ClusterAvailabilityModel>, Error>> TestClusterAccessForAccountAsync(BaseHeappeConnectionInfo heappeConnInfo, ClusterAvailabilityRequest clusterAvailabilityRequest, CancellationToken ct = default);
    ValueTask<Result<IList<SecureShellKeyModel>, Error>> GenerateSecureShellKeyAsync(BaseHeappeConnectionInfo heappeConnInfo, SecureShellKeyRequest secureShellKeyRequest, CancellationToken ct = default);
}