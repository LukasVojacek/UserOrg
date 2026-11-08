using Heappe.Contracts;
using Heappe.Contracts.CommandTemplate;
using Heappe.Contracts.Connection;
using Heappe.Contracts.HEappeApiDto.Requests;
using Heappe.Contracts.HEappeApiDto.Responses;

using Lukdrasil.Error;
using Lukdrasil.Result;

namespace Waldur.Adapter.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IHeappeService"/> that always returns success without performing any real HTTP calls.
/// </summary>
internal sealed class FakeHeappeService : IHeappeService
{
    public ValueTask<Result<IList<ClusterDetailedModel>, Error>> GetClusters(BaseHeappeConnectionInfo heappeConnInfo, CancellationToken ct)
        => ValueTask.FromResult(Result<IList<ClusterDetailedModel>, Error>.Success([], State.Ok));

    public ValueTask<Result<IList<ClusterInfoModel>, Error>> GetListAvailableClusters(BaseHeappeConnectionInfo heappeConnInfo, IEnumerable<KeyValuePair<string, string?>>? queryParameters = null, CancellationToken ct = default)
        => ValueTask.FromResult(Result<IList<ClusterInfoModel>, Error>.Success([], State.Ok));

    public ValueTask<Result<IList<ClusterNodeTypeAggregationModel>, Error>> GetClusterNodeTypeAggregations(BaseHeappeConnectionInfo heappeConnInfo, CancellationToken ct)
        => ValueTask.FromResult(Result<IList<ClusterNodeTypeAggregationModel>, Error>.Success([], State.Ok));

    public ValueTask<Result<ProjectModel, Error>> GetProjectByResourceName(BaseHeappeConnectionInfo heappeConnInfo, string resourceName, CancellationToken ct = default)
        => ValueTask.FromResult(Result<ProjectModel, Error>.Success(CreateFakeProjectModel(), State.Ok));

    public ValueTask<Result<ProjectModel, Error>> CreateProject(BaseHeappeConnectionInfo heappeConnInfo, CreateProjectRequest projectRequest, CancellationToken ct)
        => ValueTask.FromResult(Result<ProjectModel, Error>.Success(CreateFakeProjectModel(), State.Ok));

    public ValueTask<Result<ProjectModel, Error>> UpdateProject(BaseHeappeConnectionInfo heappeConnInfo, UpdateProjectRequest projectRequest, CancellationToken ct)
        => ValueTask.FromResult(Result<ProjectModel, Error>.Success(CreateFakeProjectModel(), State.Ok));

    public ValueTask<Result<IList<ProjectAssignmentToClusterModel>, Error>> GetProjectAssignmentToClustersAsync(BaseHeappeConnectionInfo heappeConnInfo, long projectId, CancellationToken ct)
        => ValueTask.FromResult(Result<IList<ProjectAssignmentToClusterModel>, Error>.Success([], State.Ok));

    public ValueTask<Result<ProjectAssignmentToClusterModel, Error>> CreateProjectAssignmentToClusterAsync(BaseHeappeConnectionInfo heappeConnInfo, CreateProjectAssignmentToClusterRequest projectAssignmentClusterRequest, CancellationToken ct)
        => ValueTask.FromResult(Result<ProjectAssignmentToClusterModel, Error>.Success(
            new ProjectAssignmentToClusterModel(
                ClusterId: 1,
                ProjectId: 1,
                ScratchStoragePath: "/scratch",
                ProjectStoragePath: "/project",
                CreatedAt: DateTime.UtcNow,
                ModifiedAt: null
            ), State.Ok));

    public ValueTask<Result<ProjectAssignmentToClusterModel, Error>> UpdateProjectAssignmentToClusterAsync(BaseHeappeConnectionInfo heappeConnInfo, UpdateProjectAssignmentToClusterRequest projectAssignmentClusterRequest, CancellationToken ct)
        => ValueTask.FromResult(Result<ProjectAssignmentToClusterModel, Error>.Success(
            new ProjectAssignmentToClusterModel(
                ClusterId: 1,
                ProjectId: 1,
                ScratchStoragePath: "/scratch",
                ProjectStoragePath: "/project",
                CreatedAt: DateTime.UtcNow,
                ModifiedAt: DateTime.UtcNow
            ), State.Ok));

    public ValueTask<Result<IList<ProjectClusterNodeTypeAggregationModel>, Error>> GetProjectClusterNodeTypeAggregationsAsync(BaseHeappeConnectionInfo heappeConnInfo, long projectId, CancellationToken ct)
        => ValueTask.FromResult(Result<IList<ProjectClusterNodeTypeAggregationModel>, Error>.Success(
            [CreateFakeProjectClusterNodeTypeAggregationModel()], State.Ok));

    public ValueTask<Result<ProjectClusterNodeTypeAggregationModel, Error>> CreateProjectClusterNodeTypeAggregationAsync(BaseHeappeConnectionInfo heappeConnInfo, CreateProjectClusterNodeTypeAggregationRequest projectClusterNodeTypeAggregationRequest, CancellationToken ct)
        => ValueTask.FromResult(Result<ProjectClusterNodeTypeAggregationModel, Error>.Success(
            CreateFakeProjectClusterNodeTypeAggregationModel(), State.Ok));

    public ValueTask<Result<ProjectClusterNodeTypeAggregationModel, Error>> UpdateProjectClusterNodeTypeAggregationAsync(BaseHeappeConnectionInfo heappeConnInfo, UpdateProjectClusterNodeTypeAggregationRequest projectClusterNodeTypeAggregationRequest, CancellationToken ct)
        => ValueTask.FromResult(Result<ProjectClusterNodeTypeAggregationModel, Error>.Success(
            CreateFakeProjectClusterNodeTypeAggregationModel(modifiedAt: DateTime.UtcNow), State.Ok));

    public ValueTask<Result<IList<CommandTemplateModel>, Error>> GetProjectCommandTemplatesAsync(BaseHeappeConnectionInfo heappeConnInfo, long projectId, CancellationToken ct = default)
        => ValueTask.FromResult(Result<IList<CommandTemplateModel>, Error>.Success([], State.Ok));

    public ValueTask<Result<CommandTemplateModel, Error>> CreateGenericCommandTemplateAsync(BaseHeappeConnectionInfo heappeConnInfo, GenericCommandTemplateCreateModel createModel, CancellationToken ct = default)
        => ValueTask.FromResult(Result<CommandTemplateModel, Error>.Success(CreateFakeCommandTemplate(), State.Ok));

    public ValueTask<Result<CommandTemplateModel, Error>> CreateCommandTemplateAsync(BaseHeappeConnectionInfo heappeConnInfo, CommandTemplateCreateModel createModel, CancellationToken ct = default)
        => ValueTask.FromResult(Result<CommandTemplateModel, Error>.Success(CreateFakeCommandTemplate(), State.Ok));

    public ValueTask<Result<bool, Error>> ModifyComandTemplateAsync(BaseHeappeConnectionInfo heappeConnInfo, CommandTemplateUpdateModel modifyModel, CancellationToken ct = default)
        => ValueTask.FromResult(Result<bool, Error>.Success(true, State.Ok));

    public ValueTask<Result<Dictionary<string, DownloadedFileModel>, Error>> DownloadChangedFilesAsync(BaseHeappeConnectionInfo heappeConnInfo, long submittedJobInfoId, CancellationToken ct = default)
        => ValueTask.FromResult(Result<Dictionary<string, DownloadedFileModel>, Error>.Success(new Dictionary<string, DownloadedFileModel>(), State.Ok));

    public Task<Result<HashSet<CommandTemplatePartitionResponse>, Error>> GetActiveCommandTemplatesAsync(Guid projectId, CancellationToken ct)
        => Task.FromResult(Result<HashSet<CommandTemplatePartitionResponse>, Error>.Success([]));

    public ValueTask<Result<IList<ClusterAuthenticationCredentialAuthType>, Error>> GetClusterAuthenticationCredentialsAuthTypesAsync(BaseHeappeConnectionInfo heappeConnInfo, CancellationToken ct = default)
        => ValueTask.FromResult(Result<IList<ClusterAuthenticationCredentialAuthType>, Error>.Success(
            [new ClusterAuthenticationCredentialAuthType { Id = 1, Name = "SshCertificate" }], State.Ok));

    private static ProjectModel CreateFakeProjectModel() =>
        new(
            Id: 1,
            Name: "fake-project",
            Description: "Fake project for testing",
            AccountingString: "fake-account",
            StartDate: DateTime.UtcNow,
            EndDate: DateTime.UtcNow.AddYears(1),
            UsageType: 0,
            UseAccountingStringForScheduler: false,
            KeyScriptsDirectoryPath: "/scripts",
            IsOneToOneMapping: false,
            CommandTemplates: []
        );

    private static CommandTemplateModel CreateFakeCommandTemplate() =>
        new(
            Id: 1,
            Name: "fake-template",
            Description: null,
            ExtendedAllocationCommand: null,
            ExecutableFile: "script.sh",
            PreparationScript: null,
            CommandParameters: "",
            IsGeneric: false,
            IsEnabled: true,
            ProjectId: 1,
            ClusterNodeTypeId: 1,
            CreatedFromGenericTemplateId: null,
            TemplateParameters: []
        );

    private static ProjectClusterNodeTypeAggregationModel CreateFakeProjectClusterNodeTypeAggregationModel(DateTime? modifiedAt = null) =>
        new(
            ProjectId: 1,
            ClusterNodeTypeAggregationId: 1,
            AllocationAmount: 0,
            CreatedAt: DateTime.UtcNow,
            ModifiedAt: modifiedAt
        );

    public ValueTask<Result<IList<ClusterAvailabilityModel>, Error>> TestClusterAccessForAccountAsync(BaseHeappeConnectionInfo heappeConnInfo, ClusterAvailabilityRequest clusterAvailabilityRequest, CancellationToken ct = default)
     => ValueTask.FromResult(Result<IList<ClusterAvailabilityModel>, Error>.Success(
            [new ClusterAvailabilityModel(
                ClusterName: "fake-cluster",
                IsClusterAccessible: true
            )], State.Ok));

    public ValueTask<Result<IList<SecureShellKeyModel>, Error>> GenerateSecureShellKeyAsync(BaseHeappeConnectionInfo heappeConnInfo, SecureShellKeyRequest secureShellKeyRequest, CancellationToken ct = default)
    => ValueTask.FromResult(Result<IList<SecureShellKeyModel>, Error>.Success(
            [new SecureShellKeyModel(
                Username: "fake-user",
                KeyType: 1,
                PublicKeyOpenSSH: "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQC...",
                PublicKeyPEM: "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAr..."
                )], State.Ok));
}


