using System.Data.Common;
using System.Runtime.ExceptionServices;
using System.Globalization;
using System.Text;
using System.Text.Json;

using Heappe.Contracts;
using Heappe.Contracts.CommandTemplate;
using Heappe.Contracts.Connection;
using Heappe.Contracts.HEappeApiDto.Requests;
using Heappe.Contracts.HEappeApiDto.Responses;
using Heappe.Features.Connection;
using Heappe.Features.InitializationProjectWithClusters;
using Heappe.Features.KillSwitch;
using Heappe.Features.LogRetrieval.Response;
using Heappe.Features.Nexus;
using Heappe.Features.VersionResolver;
using Heappe.Helpers;
using Heappe.Helpers.Connection;
using Heappe.Persistence;
using Heappe.Persistence.Entities;
using Heappe.Persistence.Enums;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;

namespace Heappe;

internal class HeappeService(ILogger<HeappeService> logger,
    IHttpClientFactory httpClientFactory,
    IFeatureManager featureManager,
    IDbContextFactory<HeappeIntegrationDbContext> dbContextFactory,
    IKillSwitchStateService killSwitchStateService,
    IHeappeVersionResolver heappeVersionResolver,
    IOptions<ClusterAuthTypeOptions> authTypeOptions
    ) : IHeappeService, IHeappeUserAuthenticator
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string HEAppEAuthHeaderFeatureFlagName = "HEAppEAuthHeader";
    private readonly ClusterAuthTypeOptions _authTypeOptions = authTypeOptions?.Value ?? throw new ArgumentNullException(nameof(authTypeOptions));

    public async ValueTask<Result<IList<ClusterDetailedModel>, Error>> GetClusters(BaseHeappeConnectionInfo heappeConnInfo, CancellationToken ct)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(heappeConnInfo);
            using var builder = new HeappeHttpRequestBuilder();
            using var httpRequest = builder.WithMethod(HttpMethod.Get)
                .WithBaseAddressAndPath(heappeConnInfo.BaseAddress, "/heappe/Management/Clusters")
                .WithAuthorization(heappeConnInfo)
                .Build();

            return await SendRequestAsync<IList<ClusterDetailedModel>>(httpRequest, ct);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occurs while retrieving list of clusters from HEAppE instance at '{HeappeAddress}'.", heappeConnInfo.BaseAddress);
            return Result<IList<ClusterDetailedModel>, Error>.Failure(Errors.FailedToRetrieveListClustersMethod(heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }

    public async ValueTask<Result<IList<ClusterInfoModel>, Error>> GetListAvailableClusters(
        BaseHeappeConnectionInfo heappeConnInfo,
        IEnumerable<KeyValuePair<string, string?>> queryParameters = null,
        CancellationToken ct = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(heappeConnInfo);
            using var builder = new HeappeHttpRequestBuilder();
            var requestMessage = builder.WithMethod(HttpMethod.Get)
                .WithBaseAddressAndPath(heappeConnInfo.BaseAddress, "/heappe/ClusterInformation/ListAvailableClusters")
                .WithQueryParameter("ForceRefresh", "true")
                .WithAuthorization(heappeConnInfo);

            requestMessage = queryParameters is not null
                ? requestMessage.WithQueryParameters(queryParameters)
                : requestMessage;

            using var httpRequest = requestMessage.Build();
            return await SendRequestAsync<IList<ClusterInfoModel>>(httpRequest, ct);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occurs while retrieving list of available clusters from HEAppE instance at '{HeappeAddress}'.", heappeConnInfo.BaseAddress);
            return Result<IList<ClusterInfoModel>, Error>.Failure(Errors.FailedToRetrieveListAvailableClustersMethod(heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }

    public async ValueTask<Result<IList<ClusterNodeTypeAggregationModel>, Error>> GetClusterNodeTypeAggregations(BaseHeappeConnectionInfo heappeConnInfo, CancellationToken ct)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(heappeConnInfo);
            using var builder = new HeappeHttpRequestBuilder();
            using var httpRequest = builder.WithMethod(HttpMethod.Get)
                .WithBaseAddressAndPath(heappeConnInfo.BaseAddress, "/heappe/Management/ClusterNodeTypeAggregations")
                .WithAuthorization(heappeConnInfo)
                .Build();
            return await SendRequestAsync<IList<ClusterNodeTypeAggregationModel>>(httpRequest, ct);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occurs while retrieving list of cluster node type aggregations from HEAppE instance at '{HeappeAddress}'.", heappeConnInfo.BaseAddress);
            return Result<IList<ClusterNodeTypeAggregationModel>, Error>.Failure(Errors.FailedToRetrieveListClusterNodeTypeAggregationsMethod(heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }

    public async ValueTask<Result<ProjectModel, Error>> GetProjectByResourceName(BaseHeappeConnectionInfo heappeConnInfo, string resourceName, CancellationToken ct = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(heappeConnInfo);
            using var builder = new HeappeHttpRequestBuilder();
            using var requestMessage = builder.WithMethod(HttpMethod.Get)
                .WithBaseAddressAndPath(heappeConnInfo.BaseAddress, "/heappe/Management/ProjectsByAccountingStrings")
                .WithAuthorization(heappeConnInfo)
                .WithQueryParameter("AccountingString", resourceName)
                .Build();
            var data = await SendRequestAsync<IList<ProjectModel>>(requestMessage, ct);

            return data switch
            {
                { IsSuccess: true, Value.Count: 0 } => Result<ProjectModel, Error>.Failure(Errors.FailedToRetrieveProjectByResourceName(resourceName, heappeConnInfo.BaseAddress.AbsoluteUri), State.NotFound),
                { IsSuccess: true, Value.Count: > 0 } => Result<ProjectModel, Error>.Success(data.Value.FirstOrDefault(), State.Ok),
                _ => Result<ProjectModel, Error>.Failure(data.Error, data.State)
            };

        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occurs while retrieving Project '{ResourceName}' from HEAppE instance at '{HeappeAddress}'.", resourceName, heappeConnInfo.BaseAddress);
            return Result<ProjectModel, Error>.Failure(Errors.FailedToRetrieveProjectByResourceName(resourceName, heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }

    public async ValueTask<Result<ProjectModel, Error>> CreateProject(BaseHeappeConnectionInfo heappeConnInfo, CreateProjectRequest projectRequest, CancellationToken ct)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(heappeConnInfo);
            return await SendWithBodyAsync<ProjectModel, CreateProjectRequest>(heappeConnInfo, HttpMethod.Post, "/heappe/Management/Project", projectRequest, ct);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occurs while creating Project '{ResourceName}' from HEAppE instance at '{HeappeAddress}'.", projectRequest.Name, heappeConnInfo.BaseAddress);
            return Result<ProjectModel, Error>.Failure(Errors.FailedToCreateProject(projectRequest.Name, heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }

    public async ValueTask<Result<ProjectModel, Error>> UpdateProject(BaseHeappeConnectionInfo heappeConnInfo, UpdateProjectRequest projectRequest, CancellationToken ct)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(heappeConnInfo);
            return await SendWithBodyAsync<ProjectModel, UpdateProjectRequest>(heappeConnInfo, HttpMethod.Put, "/heappe/Management/Project", projectRequest, ct);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occurs while updating Project '{ResourceName}' from HEAppE instance at '{HeappeAddress}'.", projectRequest.Name, heappeConnInfo.BaseAddress);
            return Result<ProjectModel, Error>.Failure(Errors.FailedToCreateProject(projectRequest.Name, heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }


    public async ValueTask<Result<IList<ProjectAssignmentToClusterModel>, Error>> GetProjectAssignmentToClustersAsync(BaseHeappeConnectionInfo heappeConnInfo, long projectId, CancellationToken ct)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(heappeConnInfo);
            using var builder = new HeappeHttpRequestBuilder();
            using var requestMessage = builder.WithMethod(HttpMethod.Get)
                .WithBaseAddressAndPath(heappeConnInfo.BaseAddress, "/heappe/Management/ProjectAssignmentToClusters")
                .WithAuthorization(heappeConnInfo)
                .WithQueryParameter("ProjectId", projectId.ToString(CultureInfo.InvariantCulture))
                .Build();

            return await SendRequestAsync<IList<ProjectAssignmentToClusterModel>>(requestMessage, ct);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occurs while retrieving assignment clusters for project {ProjectId} from HEAppE instance at '{HeappeAddress}'.", projectId, heappeConnInfo.BaseAddress);
            return Result<IList<ProjectAssignmentToClusterModel>, Error>.Failure(Errors.FailedToRetrieveAssignmentClustersForProject(projectId, heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }

    public async ValueTask<Result<ProjectAssignmentToClusterModel, Error>> CreateProjectAssignmentToClusterAsync(BaseHeappeConnectionInfo heappeConnInfo, CreateProjectAssignmentToClusterRequest projectAssignmentClusterRequest, CancellationToken ct)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(heappeConnInfo);
            return await SendWithBodyAsync<ProjectAssignmentToClusterModel, CreateProjectAssignmentToClusterRequest>(heappeConnInfo, HttpMethod.Post, "/heappe/Management/ProjectAssignmentToCluster", projectAssignmentClusterRequest, ct);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occurs while creating project assignment cluster for Project '{ProjectId}' and Cluster '{ClusterId}' from HEAppE instance at '{HeappeAddress}'.", projectAssignmentClusterRequest.ProjectId, projectAssignmentClusterRequest.ClusterId, heappeConnInfo.BaseAddress);
            return Result<ProjectAssignmentToClusterModel, Error>.Failure(Errors.FailedToCreateProjectAssignmentCluster(projectAssignmentClusterRequest.ProjectId, projectAssignmentClusterRequest.ClusterId, heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }

    public async ValueTask<Result<ProjectAssignmentToClusterModel, Error>> UpdateProjectAssignmentToClusterAsync(BaseHeappeConnectionInfo heappeConnInfo, UpdateProjectAssignmentToClusterRequest projectAssignmentClusterRequest, CancellationToken ct)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(heappeConnInfo);
            return await SendWithBodyAsync<ProjectAssignmentToClusterModel, UpdateProjectAssignmentToClusterRequest>(heappeConnInfo, HttpMethod.Put, "/heappe/Management/ProjectAssignmentToCluster", projectAssignmentClusterRequest, ct);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occurs while updating project assignment cluster for Project '{ProjectId}' and Cluster '{ClusterId}' from HEAppE instance at '{HeappeAddress}'.", projectAssignmentClusterRequest.ProjectId, projectAssignmentClusterRequest.ClusterId, heappeConnInfo.BaseAddress);
            return Result<ProjectAssignmentToClusterModel, Error>.Failure(Errors.FailedToUpdateProjectAssignmentCluster(projectAssignmentClusterRequest.ProjectId, projectAssignmentClusterRequest.ClusterId, heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }

    public async ValueTask<Result<IList<ProjectClusterNodeTypeAggregationModel>, Error>> GetProjectClusterNodeTypeAggregationsAsync(BaseHeappeConnectionInfo heappeConnInfo, long projectId, CancellationToken ct)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(heappeConnInfo);
            using var builder = new HeappeHttpRequestBuilder();
            using var requestMessage = builder.WithMethod(HttpMethod.Get)
                .WithBaseAddressAndPath(heappeConnInfo.BaseAddress, "/heappe/Management/ProjectClusterNodeTypeAggregations")
                .WithAuthorization(heappeConnInfo)
                .WithQueryParameters(new List<KeyValuePair<string, string>>
                {
                  new("ProjectId", projectId.ToString(CultureInfo.InvariantCulture)),
                })
                .Build();

            return await SendRequestAsync<IList<ProjectClusterNodeTypeAggregationModel>>(requestMessage, ct);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occurs while retrieving project cluster node type aggregation for Project '{ProjectId}' from HEAppE instance at '{HeappeAddress}'.", projectId, heappeConnInfo.BaseAddress);
            return Result<IList<ProjectClusterNodeTypeAggregationModel>, Error>.Failure(Errors.FailedToRetrieveProjectClusterNodeTypeAggregation(projectId, heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }

    public async ValueTask<Result<ProjectClusterNodeTypeAggregationModel, Error>> CreateProjectClusterNodeTypeAggregationAsync(BaseHeappeConnectionInfo heappeConnInfo, CreateProjectClusterNodeTypeAggregationRequest projectClusterNodeTypeAggregationRequest, CancellationToken ct)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(heappeConnInfo);
            return await SendWithBodyAsync<ProjectClusterNodeTypeAggregationModel, CreateProjectClusterNodeTypeAggregationRequest>(heappeConnInfo, HttpMethod.Post, "/heappe/Management/ProjectClusterNodeTypeAggregation", projectClusterNodeTypeAggregationRequest, ct);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occurs while creating project cluster node type aggregation for ClusterNodeTypeAggregationId '{ClusterNodeTypeAggregationId}' and Project '{ProjectId}' from HEAppE instance at '{HeappeAddress}'.", projectClusterNodeTypeAggregationRequest.ClusterNodeTypeAggregationId, projectClusterNodeTypeAggregationRequest.ProjectId, heappeConnInfo.BaseAddress);
            return Result<ProjectClusterNodeTypeAggregationModel, Error>.Failure(Errors.FailedToCreateProjectClusterNodeTypeAggregation(projectClusterNodeTypeAggregationRequest.ClusterNodeTypeAggregationId, projectClusterNodeTypeAggregationRequest.ProjectId, heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }

    public async ValueTask<Result<ProjectClusterNodeTypeAggregationModel, Error>> UpdateProjectClusterNodeTypeAggregationAsync(BaseHeappeConnectionInfo heappeConnInfo, UpdateProjectClusterNodeTypeAggregationRequest projectClusterNodeTypeAggregationRequest, CancellationToken ct)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(heappeConnInfo);
            return await SendWithBodyAsync<ProjectClusterNodeTypeAggregationModel, UpdateProjectClusterNodeTypeAggregationRequest>(heappeConnInfo, HttpMethod.Put, "/heappe/Management/ProjectClusterNodeTypeAggregation", projectClusterNodeTypeAggregationRequest, ct);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occurs while updating project cluster node type aggregation for ClusterNodeTypeAggregationId '{ClusterNodeTypeAggregationId}' and Project '{ProjectId}' from HEAppE instance at '{HeappeAddress}'.", projectClusterNodeTypeAggregationRequest.ClusterNodeTypeAggregationId, projectClusterNodeTypeAggregationRequest.ProjectId, heappeConnInfo.BaseAddress);
            return Result<ProjectClusterNodeTypeAggregationModel, Error>.Failure(Errors.FailedToUpdateProjectClusterNodeTypeAggregation(projectClusterNodeTypeAggregationRequest.ClusterNodeTypeAggregationId, projectClusterNodeTypeAggregationRequest.ProjectId, heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }




    public async ValueTask<Result<IList<CommandTemplateModel>, Error>> GetProjectCommandTemplatesAsync(BaseHeappeConnectionInfo heappeConnInfo, long projectId, CancellationToken ct = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(heappeConnInfo);
            using var builder = new HeappeHttpRequestBuilder();
            using var requestMessage = builder.WithMethod(HttpMethod.Get)
                .WithBaseAddressAndPath(heappeConnInfo.BaseAddress, "/heappe/Management/CommandTemplates")
                .WithAuthorization(heappeConnInfo)
                .WithQueryParameter("ProjectId", projectId.ToString(CultureInfo.InvariantCulture))
                .Build();

            return await SendRequestAsync<IList<CommandTemplateModel>>(requestMessage, ct);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occurs while retrieving list of project command templates for project {ProjectId} from HEAppE instance at '{HeappeAddress}'.", projectId, heappeConnInfo.BaseAddress);
            return Result<IList<CommandTemplateModel>, Error>.Failure(Errors.FailedToRetrieveCommandTemplatesForProject(projectId, heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }

    public async ValueTask<Result<CommandTemplateModel, Error>> CreateGenericCommandTemplateAsync(BaseHeappeConnectionInfo heappeConnInfo, GenericCommandTemplateCreateModel createModel, CancellationToken ct = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(heappeConnInfo);
            ArgumentNullException.ThrowIfNull(createModel);
            return await SendWithBodyAsync<CommandTemplateModel, GenericCommandTemplateCreateModel>(heappeConnInfo, HttpMethod.Post, "/heappe/Management/GenericCommandTemplate", createModel, ct);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occuers while creating generic command template for project {ProjectId} from HEAppE instance at '{HeappeAddress}'.", createModel.ProjectId, heappeConnInfo.BaseAddress);
            return Result<CommandTemplateModel, Error>.Failure(Errors.FailedToCreateCommandTemplateForProject(createModel.ProjectId, heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }

    public async ValueTask<Result<CommandTemplateModel, Error>> CreateCommandTemplateAsync(BaseHeappeConnectionInfo heappeConnInfo, CommandTemplateCreateModel createModel, CancellationToken ct = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(heappeConnInfo);
            ArgumentNullException.ThrowIfNull(createModel);
            return await SendWithBodyAsync<CommandTemplateModel, CommandTemplateCreateModel>(heappeConnInfo, HttpMethod.Post, "/heappe/Management/CommandTemplate", createModel, ct);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occuers while creating command template for project {ProjectId} from HEAppE instance at '{HeappeAddress}'.", createModel.ProjectId, heappeConnInfo.BaseAddress);
            return Result<CommandTemplateModel, Error>.Failure(Errors.FailedToCreateCommandTemplateForProject(createModel.ProjectId, heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }

    public async ValueTask<Result<bool, Error>> ModifyComandTemplateAsync(BaseHeappeConnectionInfo heappeConnInfo, CommandTemplateUpdateModel modifyModel, CancellationToken ct = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(heappeConnInfo);
            ArgumentNullException.ThrowIfNull(modifyModel);

            using var builder = new HeappeHttpRequestBuilder();
            using var requestMessage = builder.WithMethod(HttpMethod.Put)
                .WithBaseAddressAndPath(heappeConnInfo.BaseAddress, $"/heappe/Management/CommandTemplate")
                .WithAuthorization(heappeConnInfo)
                .WithBody(modifyModel)
                .Build();

            var result = await SendRequestAsync<CommandTemplateModel>(requestMessage, ct);
            return result.IsSuccess
                ? Result<bool, Error>.Success(true, State.Ok)
                : Result<bool, Error>.Failure(Errors.FailedToModifyCommandTemplate(modifyModel.Id, heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occuers while modifying command template {TemplateId} from HEAppE instance at '{HeappeAddress}'.", modifyModel.Id, heappeConnInfo.BaseAddress);
            return Result<bool, Error>.Failure(Errors.FailedToModifyCommandTemplate(modifyModel.Id, heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }

    public async ValueTask<Result<Dictionary<string, DownloadedFileModel>, Error>> DownloadChangedFilesAsync(BaseHeappeConnectionInfo heappeConnInfo, long submittedJobInfoId, CancellationToken ct = default)
    {
        var result = new Dictionary<string, DownloadedFileModel>(StringComparer.Ordinal);
        var changed = await ListChangedFilesAsync(heappeConnInfo, submittedJobInfoId, ct);

        if (!changed.IsSuccess)
        {
            return Result<Dictionary<string, DownloadedFileModel>, Error>.Failure(changed.Error, changed.State);
        }

        foreach (var item in changed.Value)
        {
            if (string.IsNullOrWhiteSpace(item.FileName))
            {
                continue;
            }

            var file = await DownloadFileAsync(heappeConnInfo, submittedJobInfoId, item.FileName, ct);

            if (!file.IsSuccess)
            {
                return Result<Dictionary<string, DownloadedFileModel>, Error>.Failure(file.Error, file.State);
            }

            result[item.FileName] = file.Value;
        }

        return Result<Dictionary<string, DownloadedFileModel>, Error>.Success(result, State.Ok);
    }

    private async ValueTask<Result<List<ChangedFileModel>, Error>> ListChangedFilesAsync(BaseHeappeConnectionInfo heappeConnInfo, long submittedJobInfoId, CancellationToken ct = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(heappeConnInfo);
            using var builder = new HeappeHttpRequestBuilder();
            using var requestMessage = builder.WithMethod(HttpMethod.Get)
                .WithBaseAddressAndPath(heappeConnInfo.BaseAddress, "/heappe/FileTransfer/ListChangedFilesForJob")
                .WithAuthorization(heappeConnInfo)
                .WithQueryParameter("SubmittedJobInfoId", submittedJobInfoId.ToString(CultureInfo.InvariantCulture))
                .Build();

            return await SendRequestAsync<List<ChangedFileModel>>(requestMessage, ct);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occuers while retrieving list of changed files for job '{JobId}' from HEAppE instance at '{HeappeAddress}'.", submittedJobInfoId, heappeConnInfo.BaseAddress);
            return Result<List<ChangedFileModel>, Error>.Failure(Errors.FailedToRetrieveListChangedFilesForJob(submittedJobInfoId, heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }

    private async ValueTask<Result<DownloadedFileModel, Error>> DownloadFileAsync(BaseHeappeConnectionInfo heappeConnInfo, long submittedJobInfoId, string relativeFilePath, CancellationToken ct = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(heappeConnInfo);
            using var builder = new HeappeHttpRequestBuilder();
            using var requestMessage = builder.WithMethod(HttpMethod.Post)
                .WithBaseAddressAndPath(heappeConnInfo.BaseAddress, "/heappe/FileTransfer/DownloadFileFromCluster")
                .WithAuthorization(heappeConnInfo)
                .WithBodyAsPairs(new List<KeyValuePair<string, string>>
                {
                    new("RelativeFilePath", relativeFilePath),
                    new("SubmittedJobInfoId", submittedJobInfoId.ToString(CultureInfo.InvariantCulture))
                })
                .Build();

            var result = await SendRequestAsync<string>(requestMessage, ct);
            if (result.IsSuccess)
            {
                // The response is expected to be a Base64 string (possibly JSON-encoded string with quotes). Clean it up.
                var base64 = result.Value.Trim() ?? string.Empty;
                if (base64.StartsWith('"') && base64.EndsWith('"'))
                {
                    base64 = base64[1..^1];
                }

                base64 = base64.Replace("\n", string.Empty, StringComparison.Ordinal).Replace("\r", string.Empty, StringComparison.Ordinal);

                byte[] bytes;
                bytes = Convert.FromBase64String(base64);

                string? text = null;
                text = Encoding.UTF8.GetString(bytes);

                var downloadFile = new DownloadedFileModel(relativeFilePath, text);
                return Result<DownloadedFileModel, Error>.Success(downloadFile, State.Ok);
            }

            return Result<DownloadedFileModel, Error>.Failure(Errors.FailedToDownloadFileFromJob(submittedJobInfoId, heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occuers during downloading file for job '{JobId}' from HEAppE instance at '{HeappeAddress}'.", submittedJobInfoId, heappeConnInfo.BaseAddress);
            return Result<DownloadedFileModel, Error>.Failure(Errors.FailedToDownloadFileFromJob(submittedJobInfoId, heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }

    public async Task<BaseHeappeConnectionInfo> AuthenticateAsync(HeappeRoute route, string bearerToken, bool useToken, CancellationToken ct)
    {
        var heapeAuthFeatureEnabled = await featureManager.IsEnabledAsync(HEAppEAuthHeaderFeatureFlagName, ct);
        return (useToken, heapeAuthFeatureEnabled) switch
        {
            (false, false) => await GetSessionCodeConnectionInfoAsync(route, bearerToken, ct),
            (_, _) => new BearerTokenHeappeConnectionInfo(route.ProxyBaseAddress, route.Identifier, bearerToken)
        };
    }

    public async Task<Result<HashSet<CommandTemplatePartitionResponse>, Error>> GetActiveCommandTemplatesAsync(Guid projectId, CancellationToken ct)
    {
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
            var activePartitions = await dbContext.Set<ProjectCommandTemplatePartition>()
                .TagWith("Get active command templates for specific project")
                .AsNoTracking()
                .Where(w => w.ProjectCommandTemplate.ProjectId == projectId && w.State == ECommandTemplateState.Active)
                .Select(s => new
                {
                    Name = s.ProjectCommandTemplate.Name,
                    CommandTemplateId = s.CommandTemplateId.Value,
                    KillKey = new PartitionKillKey(s.LocationPartition.LocationId, s.ProjectCommandTemplate.Id)
                })
                .ToListAsync(ct);

            var killedPartitions = await killSwitchStateService.GetKilledPartitionsAsync(
                activePartitions.Select(p => p.KillKey).ToHashSet(), ct);

            var result = activePartitions
                .Where(p => !killedPartitions.Contains(p.KillKey))
                .Select(p => new CommandTemplatePartitionResponse(p.Name, p.CommandTemplateId))
                .ToHashSet();

            // The allow-list this returns decides which templates survive pruning downstream, so an empty or
            // unexpectedly small result is the single most useful fact when TemplatesInfo comes back thin.
            logger.LogInformation(
                "Active command templates for project {ProjectId}: {ActivePartitions} Active partition(s), {KilledPartitions} suppressed by the kill switch, {AllowedTemplates} allowed key(s)",
                projectId, activePartitions.Count, killedPartitions.Count, result.Count);

            return Result<HashSet<CommandTemplatePartitionResponse>, Error>.Success(result, State.Ok);

        }
        catch (Exception ex) when (ex is InvalidOperationException or DbException)
        {
            logger.LogError(ex, "Failed to retrieve active command templates for project {ProjectId}.", projectId);
            return Result<HashSet<CommandTemplatePartitionResponse>, Error>.Failure(Errors.FailedToRetrieveActiveCommandTemplates(projectId), State.Error);
        }
    }

    public async ValueTask<Result<IList<ClusterAuthenticationCredentialAuthType>, Error>> GetClusterAuthenticationCredentialsAuthTypesAsync(BaseHeappeConnectionInfo heappeConnInfo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(heappeConnInfo);
        try
        {
            var version = await heappeVersionResolver.GetVersion(heappeConnInfo.Identifier, ct);

            // The ClusterAuthenticationCredentialAuthType is only supported in HEAppE version 6.4.0 and above,
            // return empty list for older versions to maintain compatibility.
            if (version < ProjectCreationService.MinimalVersionForPreferredAuthTypeProperty)
            {
                return Result<IList<ClusterAuthenticationCredentialAuthType>, Error>.Failure(Errors.VersionNotSupported(
                    heappeConnInfo.BaseAddress.AbsoluteUri,
                    ProjectCreationService.MinimalVersionForPreferredAuthTypeProperty), State.Invalid);
            }


            using var builder = new HeappeHttpRequestBuilder();
            using var requestMessage = builder.WithMethod(HttpMethod.Get)
                .WithBaseAddressAndPath(heappeConnInfo.BaseAddress, "/heappe/Dictionary/GetClusterAuthenticationCredentialsAuthTypes")
                .WithInstance(heappeConnInfo.Identifier)
                .Build();

            var result = await SendRequestAsync<IList<ClusterAuthenticationCredentialAuthType>>(requestMessage, ct);

            if (!result.IsSuccess)
            {
                return result;
            }

            var filtered = result.Value
                .Where(x => _authTypeOptions.AllowedAuthenticationTypeNames
                    .Any(allowed => string.Equals(x.Name, allowed, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            return Result<IList<ClusterAuthenticationCredentialAuthType>, Error>.Success(filtered, State.Ok);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occurs while retrieving cluster authentication credential authentication types from HEAppE instance at '{HeappeAddress}'.", heappeConnInfo.BaseAddress);
            return Result<IList<ClusterAuthenticationCredentialAuthType>, Error>.Failure(Errors.FailedToRetrieveClusterAuthenticationCredentialsAuthTypes(heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }

    public async ValueTask<Result<IList<ClusterAvailabilityModel>, Error>> TestClusterAccessForAccountAsync(BaseHeappeConnectionInfo heappeConnInfo, ClusterAvailabilityRequest clusterAvailabilityRequest, CancellationToken ct = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(heappeConnInfo);
            ArgumentNullException.ThrowIfNull(clusterAvailabilityRequest);

            using var builder = new HeappeHttpRequestBuilder();
            using var requestMessage = builder.WithMethod(HttpMethod.Get)
                .WithBaseAddressAndPath(heappeConnInfo.BaseAddress, $"/heappe/Management/TestClusterAccessForAccount")
                .WithAuthorization(heappeConnInfo)
                .WithQueryParameters(new[]
                 {
                    new KeyValuePair<string, string?>( "UserName", clusterAvailabilityRequest.Username ),
                    new KeyValuePair<string, string?>( "ProjectId", clusterAvailabilityRequest.ProjectId.ToString() )
                 })
                .Build();

            return await SendRequestAsync<IList<ClusterAvailabilityModel>>(requestMessage, ct);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occurs while testing cluster access for account {Username} in project {ProjectId} from HEAppE instance at '{HeappeAddress}'.", clusterAvailabilityRequest.Username, clusterAvailabilityRequest.ProjectId, heappeConnInfo.BaseAddress);
            return Result<IList<ClusterAvailabilityModel>, Error>.Failure(Errors.FailedToTestClusterAccessForAccount(clusterAvailabilityRequest.Username, clusterAvailabilityRequest.ProjectId, heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }

    public async ValueTask<Result<IList<SecureShellKeyModel>, Error>> GenerateSecureShellKeyAsync(BaseHeappeConnectionInfo heappeConnInfo, SecureShellKeyRequest secureShellKeyRequest, CancellationToken ct = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(secureShellKeyRequest);
            using var builder = new HeappeHttpRequestBuilder();
            using var requestMessage = builder.WithMethod(HttpMethod.Post)
                .WithBaseAddressAndPath(heappeConnInfo.BaseAddress, $"/heappe/Management/GenerateSecureShellKey")
                .WithAuthorization(heappeConnInfo)
                .WithBody(secureShellKeyRequest)
                .Build();
            return await SendRequestAsync<IList<SecureShellKeyModel>>(requestMessage, ct);
        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occurs while generating secure shell key for account {Username} in project {ProjectId} from HEAppE instance at '{HeappeAddress}'.", secureShellKeyRequest.Credentials.First().Username, secureShellKeyRequest.ProjectId, heappeConnInfo.BaseAddress);
            return Result<IList<SecureShellKeyModel>, Error>.Failure(Errors.FailedToGenerateSecureShellKey(secureShellKeyRequest.Credentials.First().Username, secureShellKeyRequest.ProjectId, heappeConnInfo.BaseAddress.AbsoluteUri), State.Error);
        }
    }

    private async ValueTask<Result<SessionCodeHEAppEConnectionInfo, Error>> GetHeappeSessionCode(HeappeRoute route, string bearerToken, CancellationToken ct = default)
    {
        var baseAddress = route.ProxyBaseAddress;
        try
        {
            if (string.IsNullOrWhiteSpace(bearerToken))
            {
                throw new ArgumentNullException(nameof(bearerToken));
            }

            using var builder = new HeappeHttpRequestBuilder();
            var credentials = new
            {
                Credentials = new
                {
                    Username = "",
                    OpenIdAccessToken = bearerToken
                }
            };
            using var requestMessage = builder.WithMethod(HttpMethod.Post)
                .WithBaseAddressAndPath(baseAddress, "/heappe/UserAndLimitationManagement/AuthenticateLexisToken")
                .WithInstance(route.Identifier)
                .WithBody(credentials)
                .Build();

            var sessionCode = await SendRequestAsync<string>(requestMessage, ct);

            return !sessionCode.IsSuccess
                ? Result<SessionCodeHEAppEConnectionInfo, Error>.Failure(Errors.FailedToRetrieveSessionCode(baseAddress.AbsoluteUri), State.Error)
                : Result<SessionCodeHEAppEConnectionInfo, Error>.Success(new SessionCodeHEAppEConnectionInfo(baseAddress, route.Identifier, sessionCode.Value), State.Ok);

        }
        catch (Exception ex)
        {
            ThrowIfCallerCancelled(ex, ct);
            logger.LogError(ex, "Unexpected error occuers while retrieving session code from HEAppE instance at '{HeappeAddress}'.", baseAddress);
            return Result<SessionCodeHEAppEConnectionInfo, Error>.Failure(Errors.FailedToRetrieveSessionCode(baseAddress.AbsoluteUri), State.Error);
        }
    }

    private async Task<BaseHeappeConnectionInfo> GetSessionCodeConnectionInfoAsync(HeappeRoute route, string bearerToken, CancellationToken ct = default)
    {
        var result = await GetHeappeSessionCode(route, bearerToken, ct);

        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException($"Failed to retrieve session code from HEAppE instance '{route.Identifier}', Message: '{result.Error.Description}'");
    }

    // The request every management write shares: authorised for the connection, body serialised as JSON.
    private async Task<Result<TResponse, Error>> SendWithBodyAsync<TResponse, TBody>(
        BaseHeappeConnectionInfo heappeConnInfo,
        HttpMethod method,
        string path,
        TBody body,
        CancellationToken ct)
        where TResponse : class
    {
        using var builder = new HeappeHttpRequestBuilder();
        using var requestMessage = builder.WithMethod(method)
            .WithBaseAddressAndPath(heappeConnInfo.BaseAddress, path)
            .WithAuthorization(heappeConnInfo)
            .WithBody(body)
            .Build();

        return await SendRequestAsync<TResponse>(requestMessage, ct);
    }

    private async Task<Result<T, Error>> SendRequestAsync<T>(HttpRequestMessage requestMessage, CancellationToken ct = default) where T : class
    {
        ct.ThrowIfCancellationRequested();
        var requestUrl = requestMessage.RequestUri!.ToString();
        try
        {
            using var httpClient = httpClientFactory.CreateClient(HeappeIntegrationExtensions.HeappeClientName);
            using var response = await httpClient.SendAsync(requestMessage, ct);

            return response.IsSuccessStatusCode
                ? await ReadSuccessAsync<T>(response, requestUrl, ct)
                : await ReadFailureAsync<T>(response, requestUrl, ct);
        }
        catch (Exception ex) when (HeappeTransportFailure.Is(ex, ct))
        {
            logger.LogError(ex, "HEAppE call '{RequestUrl}' got no usable answer.", requestUrl);
            return Result<T, Error>.Failure(TransportError(ex, requestUrl, typeof(T).Name), State.Error);
        }
    }

    private async Task<Result<T, Error>> ReadSuccessAsync<T>(HttpResponseMessage response, string requestUrl, CancellationToken ct) where T : class
    {
        var responseContent = await response.Content.ReadAsStringAsync(ct);
        try
        {
            var responseData = JsonSerializer.Deserialize<T>(responseContent, _jsonOptions);

            return responseData is not null
                ? Result<T, Error>.Success(responseData, State.Ok)
                : Result<T, Error>.Failure(Errors.FailedToDeserializeResponse(requestUrl, typeof(T).Name, responseContent), State.Error);
        }
        catch (JsonException ex)
        {
            // Log deserialization failure along with response payload to aid debugging. Response may be empty or not match expected shape.
            logger.LogError(ex, "Failed to deserialize response from HEAppE '{RequestUrl}'. Response text: '{ResponseText}'.", requestUrl, responseContent);
            return Result<T, Error>.Failure(Errors.FailedToDeserializeResponse(requestUrl, typeof(T).Name, responseContent), State.Error);
        }
    }

    // The proxy answers its own failures as problem+json; its type, not the bare status, names the cause.
    private async Task<Result<T, Error>> ReadFailureAsync<T>(HttpResponseMessage response, string requestUrl, CancellationToken ct) where T : class
    {
        var responseContent = await response.Content.ReadAsStringAsync(ct);
        var problemType = NexusProblemMapper.ProblemTypeOf(response.Content.Headers.ContentType?.MediaType, responseContent);
        var cause = NexusProblemMapper.CauseFor(response.StatusCode, problemType);
        logger.LogError("HTTP request error while getting from HEAppE '{RequestUrl}'. Status code: '{StatusCode}'. Cause: '{Cause}'. Response text: '{ResponseText}'.", requestUrl, response.StatusCode, cause, responseContent);
        return Result<T, Error>.Failure(Errors.FailedToRetrieveResponse(requestUrl, response.StatusCode, responseContent, cause), State.Error);
    }

    private static Error TransportError(Exception ex, string requestUrl, string responseTypeName) =>
        HeappeTransportFailure.IsTimeout(ex)
            ? Errors.FailedToRetrieveResponseTimeout(requestUrl, responseTypeName)
            : Errors.FailedToRetrieveResponse(requestUrl, responseTypeName);

    // The caller giving up is not a failure of the call: let it reach the caller instead of turning it into a Result.
    private static void ThrowIfCallerCancelled(Exception ex, CancellationToken ct)
    {
        if (ex is OperationCanceledException && ct.IsCancellationRequested)
        {
            ExceptionDispatchInfo.Capture(ex).Throw();
        }
    }
}