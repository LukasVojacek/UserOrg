using System.Reflection;
using System.Text.Json;

using Core.Features.Request.Notifications;
using Core.Features.Resource.Workflow.Steps;

using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries.Init;
using Heappe.Contracts.HEappeApiDto.Requests;

using Lukdrasil.Result;

using Microsoft.Extensions.Logging;

using Serilog.Core;

using UserOrg.Shared;
using UserOrg.Shared.Extensions;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;

using static Core.Shared.Auth.ReadScope;

namespace Core.Features.Resource.Workflow.Handlers;

internal sealed class InitializeHeappeHandler : IStepHandler<InitializeHeappeStep>
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    private readonly ILogger<InitializeHeappeHandler> _logger;
    private readonly IProjectCreationService _heappeProjectService;
    private readonly IHeappeService _heappeService;
    private readonly IHeappeConnectionFactory _connectionFactory;
    private readonly IRepository<ProjectResource, ProjectResourceKey> _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationSender _notificationSender;
    private readonly IProviderRepository _providerRepository;

    public InitializeHeappeHandler(ILogger<InitializeHeappeHandler> logger,
        IProjectCreationService heappeProjectService,
        IHeappeService heappeService, IHeappeConnectionFactory connectionFactory, IRepository<ProjectResource, ProjectResourceKey> repository, IUnitOfWork unitOfWork,
        INotificationSender notificationSender, IProviderRepository providerRepository)
    {
        _logger = logger;
        _heappeProjectService = heappeProjectService;
        _heappeService = heappeService;
        _connectionFactory = connectionFactory;
        _repository = repository;
        _unitOfWork = unitOfWork;
        _notificationSender = notificationSender;
        _providerRepository = providerRepository;
    }

    public async Task<StepResult> HandleAsync(InitializeHeappeStep request, CancellationToken ct)
    {
        List<CreateResourceOnLocation.CreateResourceOnLocationEndpoint.UserSshKey> heappeProjectIds = new();

        foreach (var assignedLocation in request.Data.AssignedLocations)
        {
            try
            {
                var resolvedLocation = request.ResolvedLocations.FirstOrDefault(f => string.Equals(f.LocationName, assignedLocation.LocationName, StringComparison.OrdinalIgnoreCase));
                if (resolvedLocation == null || resolvedLocation.LocationType != Domain.Shared.ELocationType.HPC)
                {
                    continue; // Skip non-HPC locations, as HEAppE is only relevant for HPC resources.
                }

                if (await InitializeLocationAsync(request, assignedLocation, ct) is { } heappeProject)
                    heappeProjectIds = heappeProject;
            }
            catch (PermanentException)
            {
                // A permanent failure (e.g. missing cluster, unknown auth type) must not be downgraded to a
                // retryable one by the catch-all below — retrying will not change the outcome.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize HEAppE project resource {resourceName} for location {locationName}", request.Data.Name, assignedLocation.LocationName);
                throw new RetryableException("HEAPPE_FAILED_INITIALIZATION", $"Failed to initialize HEAppE project resource {request.Data.Name} for location {assignedLocation.LocationName}", ex);
            }
        }

        ResourceName.TryParse(request.Data.Name, null, out var name);

        var resource = await _repository.FindAsync(new ProjectResourceKey(request.ProviderName, name), ct);
        if (resource is null)
            throw new PermanentException("RESOURCE_NOT_FOUND", $"Resource {request.Data.Name} not found for provider {request.ProviderName}");

        resource.UpdateState(EProjectResourceState.InitializeHeappeStep);

        await _unitOfWork.SaveChangesAsync(ct);

        if (!request.Data.MappingOneToOne!.Value)
        {
            List<(string, string, string, string?)> clusters = [];
            foreach (var cluster in heappeProjectIds)
            {
                clusters.Add((cluster.LocationName, cluster.AggregationName, cluster.Username, cluster.Key));
            }

            // Send notification to provider support email if available
            var providerEntity = await _providerRepository.FindAsync(request.ProviderName, ct)
                ?? throw new PermanentException("PROVIDER_NOT_FOUND", $"Provider {request.ProviderName} was not found.");
            if (providerEntity.SupportEmail.HasValue)
            {
                await _notificationSender.SendRequestToCreateAccountAsync(providerEntity.SupportEmail.Value, request.Data.Name, clusters, ct);
            }

            // Send notification to provider investigator
            if (resource.PrimaryInvestigatorEmail.HasValue)
            {
                await _notificationSender.SendRequestToCreateAccountAsync(resource.PrimaryInvestigatorEmail.Value, request.Data.Name, clusters, ct);
            }

            resource.UpdateState(EProjectResourceState.WaitingForCreationAccounts);

            await _unitOfWork.SaveChangesAsync(ct);

            return new StepResult(null, false);
        }

        var jsonString = JsonSerializer.Serialize(heappeProjectIds, IndentedJson);
        var data = JsonDocument.Parse(jsonString);

        return new StepResult(data);
    }

    private async Task<List<CreateResourceOnLocation.CreateResourceOnLocationEndpoint.UserSshKey>?> InitializeLocationAsync(
        InitializeHeappeStep request,
        CreateResourceOnLocation.CreateResourceOnLocationEndpoint.LocationAssignment assignedLocation,
        CancellationToken ct)
    {
        var cluster = request.Data.Clusters!.FirstOrDefault(x => x.ClusterName == assignedLocation.LocationName);
        if (cluster is null)
        {
            _logger.LogError("No cluster matches assigned location {LocationName}.", assignedLocation.LocationName);
            throw new PermanentException("HEAPPE_CLUSTER_NOT_FOUND", $"No cluster matches assigned location {assignedLocation.LocationName}.");
        }

        var heappeIdentifier = assignedLocation.RequireIdentifier();
        var heappeUri = assignedLocation.UriOrNull();
        var heappeUsageType = ToUsageType(assignedLocation.AllocationUnitType);

        var publicHeappeConnectionInfo = await _connectionFactory.ForPublicAsync(heappeIdentifier, ct);
        if (!publicHeappeConnectionInfo.IsSuccess)
        {
            _logger.LogError("Failed to resolve HEAppE instance {HeappeIdentifier} for cluster {ClusterName}.", heappeIdentifier, cluster.ClusterName);
            throw new RetryableException("HEAPPE_FAILED_SERVICE_CONNECTION", $"Failed to resolve HEAppE instance {heappeIdentifier}.");
        }

        var authTypes = await _heappeService.GetClusterAuthenticationCredentialsAuthTypesAsync(publicHeappeConnectionInfo.Value, ct);
        if (!authTypes.IsSuccess)
        {
            _logger.LogError("Failed to get authentication types for cluster {ClusterName}.", cluster.ClusterName);
            throw new PermanentException("HEAPPE_FAILED_AUTH_TYPES", $"Failed to get authentication types for the cluster: {cluster.ClusterName}");
        }
        var authType = authTypes.Value.FirstOrDefault(x => x.Name.Equals(assignedLocation.PreferredAuthenticationType, StringComparison.OrdinalIgnoreCase));
        if (authType == null)
        {
            _logger.LogError("Preferred authentication type {PreferredAuthenticationType} not found for cluster {ClusterName}.", assignedLocation.PreferredAuthenticationType, cluster.ClusterName);
            throw new PermanentException("HEAPPE_FAILED_AUTH_TYPE", $"Selected authentication type not found for the cluster: {cluster.ClusterName}");
        }
        var preferredAuthType = authType.Id;

        var endpoint = request.ResolvedLocations.FirstOrDefault(f => string.Equals(f.LocationName, assignedLocation.LocationName, StringComparison.OrdinalIgnoreCase))?.Endpoint ?? string.Empty;

        var spec = new HeappeProjectSpecificationEntry(
            heappeUri,
            heappeIdentifier,
            new ProjectSpecificationEntry(
                request.Data.Name,
                request.Data.AccountString!,
                request.Data.Description,
                request.Data.StartDate.DateTime,
                request.Data.EndDate.DateTime,
                heappeUsageType,
                request.Data.UseAccountingStringForScheduler!.Value,
                request.Data.PrimaryInvestigatorEmail,
                request.Data.MappingOneToOne!.Value,
                preferredAuthType),
            new ClusterSpecificationEntry(
                endpoint,
                cluster.ProjectPath,
                cluster.ScratchPath,
                new AllocationSpecificationEntry(assignedLocation.AggregationName, assignedLocation.AllocationUnitCount ?? 0)
            ));

        var serviceConnectionInfo = await _heappeProjectService.GetServiceHeappeConnectionInfoAsync(heappeIdentifier, ct);
        if (!serviceConnectionInfo.IsSuccess)
        {
            _logger.LogError("Failed to get service connection info for HEAppE instance {heappeIdentifier}.", heappeIdentifier);
            throw new RetryableException("HEAPPE_FAILED_SERVICE_CONNECTION", $"Failed to get service connection info for HEAppE instance {heappeIdentifier}.");
        }

        var result = await _heappeProjectService.InitializeHEAppEProjectWithClustersAsync(spec, ct);
        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to initialize HEAppE project resource {resourceName} at HEAppE instance {heappeIdentifier}: {errorDescription}", request.Data.Name, serviceConnectionInfo.Value.Identifier, result.Error.Description);
            throw new RetryableException("HEAPPE_FAILED_INITIALIZATION", $"Failed to initialize HEAppE project resource {request.Data.Name} at HEAppE instance {serviceConnectionInfo.Value.Identifier}: {result.Error.Description}");
        }

        if (request.Data.MappingOneToOne!.Value)
            return null;

        List<CreateResourceOnLocation.CreateResourceOnLocationEndpoint.UserSshKey> keys = [];
        foreach (var username in request.Data.Usernames ?? Enumerable.Empty<string>())
        {
            var keySSH = await GenerateSecureShellKeyAsync(request.Data.Name, username, serviceConnectionInfo.Value, result.Value, ct);
            keys.Add(new CreateResourceOnLocation.CreateResourceOnLocationEndpoint.UserSshKey(assignedLocation.LocationName.ToString(), assignedLocation.AggregationName ?? string.Empty, result.Value, username, keySSH));
        }

        return keys;
    }

    private async Task<string> GenerateSecureShellKeyAsync(
        string resourceName,
        string username,
        BaseHeappeConnectionInfo serviceConnectionInfo,
        long projectId,
        CancellationToken ct)
    {
        var key = await _heappeService.GenerateSecureShellKeyAsync(
            serviceConnectionInfo,
            new SecureShellKeyRequest(
                new List<Credentials>() { new Credentials(username, null) }, projectId), ct);
        if (!key.IsSuccess)
        {
            _logger.LogError("Failed to generate SSH key for HEAppE project resource {resourceName} at HEAppE instance {heappeIdentifier}: {errorDescription}", resourceName, serviceConnectionInfo.Identifier, key.Error.Description);
            throw new RetryableException("HEAPPE_FAILED_KEY_GENERATION", $"Failed to generate SSH key for HEAppE project resource {resourceName} at HEAppE instance {serviceConnectionInfo.Identifier}: {key.Error.Description}");
        }

        return key.Value.First().PublicKeyOpenSSH;
    }

    private static EUsageType ToUsageType(EAllocationUnitType allocationUnitType) => allocationUnitType switch
    {
        EAllocationUnitType.CoreHours => EUsageType.CoreHours,
        EAllocationUnitType.NodeHours => EUsageType.NodeHours,
        _ => EUsageType.Unspecified
    };
}