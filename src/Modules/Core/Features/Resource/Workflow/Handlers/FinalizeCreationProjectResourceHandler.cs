using System.Text.Json;

using Core.Features.Resource.Workflow.Steps;

using Google.Protobuf.WellKnownTypes;

using Heappe.Contracts;
using Heappe.Contracts.Connection;

using UserOrg.Shared;
using UserOrg.Shared.Extensions;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;

using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.Features.Resource.Workflow.Handlers;

internal sealed class FinalizeCreationProjectResourceHandler : IStepHandler<FinalizeCreationProjectResourceStep>
{
    private readonly IHeappeService _heappeService;
    private readonly IProjectCreationService _heappeProjectService;
    private readonly IRepository<ResourceAggregate, ProjectResourceKey> _repository;
    private readonly IUnitOfWork _unitOfWork;

    public FinalizeCreationProjectResourceHandler(IHeappeService heappeService,
        IRepository<ResourceAggregate, ProjectResourceKey> repository, IUnitOfWork unitOfWork, IProjectCreationService
        heappeProjectService)
    {
        _heappeService = heappeService ?? throw new ArgumentNullException(nameof(heappeService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _heappeProjectService = heappeProjectService ?? throw new ArgumentNullException(nameof(heappeProjectService));
    }

    public async Task<StepResult> HandleAsync(FinalizeCreationProjectResourceStep request, CancellationToken ct)
    {
        var heappeProjects = ReadHeappeProjects(request.Data);

        foreach (var location in request.AssignedLocations)
        {
            var resolvedLocation = request.ResolvedLocations.FirstOrDefault(f => string.Equals(f.LocationName, location.LocationName, StringComparison.OrdinalIgnoreCase));
            if (resolvedLocation == null || resolvedLocation.LocationType != Domain.Shared.ELocationType.HPC)
            {
                continue; // Skip non-HPC locations, as HEAppE is only relevant for HPC resources.
            }

            if (heappeProjects == null || !heappeProjects.Any(p => p.LocationName == location.LocationName))
            {
                continue;
            }

            var projects = heappeProjects.Where(p => p.LocationName == location.LocationName).ToList();
            foreach (var project in projects)
            {
                //only for non OneToOneMapping
                if (project.ProjectId == 0)
                    continue;

                await EnsureClusterAccessAsync(location, project.ProjectId, project.Username, ct);
            }
        }

        ResourceName.TryParse(request.Name, null, out var name);

        var resource = await _repository.FindAsync(new ProjectResourceKey(request.ProviderName, name), ct);
        if (resource is null)
            throw new PermanentException("RESOURCE_NOT_FOUND", $"Resource {request.Name} not found for provider {request.ProviderName}");

        resource.UpdateState(EProjectResourceState.FinalizeCreationProjectResourceStep);

        await _unitOfWork.SaveChangesAsync(ct);

        return new StepResult(null);
    }

    private static List<CreateResourceOnLocation.CreateResourceOnLocationEndpoint.UserSshKey> ReadHeappeProjects(JsonDocument? data)
    {
        List<CreateResourceOnLocation.CreateResourceOnLocationEndpoint.UserSshKey> heappeProjects = new();

        if (data is JsonDocument jsonDocument)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            heappeProjects = jsonDocument.Deserialize<List<CreateResourceOnLocation.CreateResourceOnLocationEndpoint.UserSshKey>>(options) ?? new List<CreateResourceOnLocation.CreateResourceOnLocationEndpoint.UserSshKey>();
            if (heappeProjects.Any(p => string.IsNullOrWhiteSpace(p.LocationName) || p.ProjectId == 0))
            {
                throw new KeyNotFoundException("Missing required fields in HeappeProjects.");
            }

            var locationGroups = heappeProjects.GroupBy(p => p.LocationName, StringComparer.OrdinalIgnoreCase);
            var duplicates = locationGroups.Where(g => g.Count() > 1).FirstOrDefault();

            if (duplicates != null)
            {
                throw new KeyNotFoundException($"Duplicate LocationName '{duplicates.Key}' found in HeappeProjects. Each LocationName must be unique.");
            }
        }

        return heappeProjects;
    }

    private async Task EnsureClusterAccessAsync(
        CreateResourceOnLocation.CreateResourceOnLocationEndpoint.LocationAssignment location,
        long projectId,
        string username,
        CancellationToken ct)
    {
        var clusterAvailabilityRequest = new Heappe.Contracts.HEappeApiDto.Requests.ClusterAvailabilityRequest(projectId, username);

        var heappeIdentifier = location.RequireIdentifier();

        var serviceConnectionInfo = await _heappeProjectService.GetServiceHeappeConnectionInfoAsync(heappeIdentifier, ct);
        if (!serviceConnectionInfo.IsSuccess)
        {
            throw new RetryableException("HEAPPE_FAILED_SERVICE_CONNECTION", $"Failed to get service connection info for HEAppE instance {heappeIdentifier}.");
        }

        var result = await _heappeService.TestClusterAccessForAccountAsync(
            serviceConnectionInfo.Value,
            clusterAvailabilityRequest,
            ct);
        if (!result.IsSuccess)
        {
            throw new RetryableException("CLUSTER_ACCESS_FAILED", result.Error.Description);
        }
        var notAccesibleItem = result.Value.FirstOrDefault(f => !f.IsClusterAccessible);
        if (notAccesibleItem != null)
        {
            throw new RetryableException("CLUSTER_ACCESS_FAILED", $"Cluster {notAccesibleItem.ClusterName} is not accessible");
        }
    }
}