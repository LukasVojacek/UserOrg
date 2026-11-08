using global::Heappe.HostContracts;
using global::Heappe.Persistence;

using Heappe.Contracts;
using Heappe.Contracts.CommandTemplate;
using Heappe.Contracts.Connection;
using Heappe.Contracts.HEappeApiDto.Responses;
using Heappe.Features.CreateProjectCommandTemplate.Request;
using Heappe.Features.KillSwitch;
using Heappe.Helpers.Connection;
using Heappe.Persistence.Entities;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;

using UserOrg.Shared;

using static Heappe.Features.CreateProjectCommandTemplate.CreateTemplateEndpoint;

namespace Heappe.Features.CreateProjectCommandTemplate;

/// <summary>
/// Extension methods for the fluent command template creation pipeline.
/// Provides validation, data preparation, and creation orchestration.
/// </summary>
internal static class CreateTemplateFlowExtensions
{
    // based on ELocationType
    public const int HpcLocationTypeId = 1;
    public const int QuantumLocationTypeId = 7;


    /// <summary>Validates and prepares resource data for command template creation.</summary>
    internal static Task<Result<PreparedResourceData, Error>> ValidateAndPrepareResourceDataAsync(
        this CreateTemplateRequest request,
        IUserorgService userorgService,
        HeappeIntegrationDbContext context,
        IHeappeConnectionFactory connectionFactory,
        CommandTemplateWriteGuard writeGuard,
        CancellationToken cancellationToken)
        => ValidateAndPrepareResourceDataInternalAsync(
            request,
            userorgService,
            context,
            connectionFactory,
            writeGuard,
            cancellationToken);

    /// <summary>Gets the Heappe project for the resource.</summary>
    internal static async Task<Result<(PreparedResourceData, ProjectModel), Error>> GetHeappeProjectAsync(
        this Task<Result<PreparedResourceData, Error>> preparedTask,
        CreateTemplateRequest request,
        IHeappeService heappeService,
        CancellationToken cancellationToken)
    {
        var prepared = await preparedTask;
        if (!prepared.IsSuccess)
        {
            return Result<(PreparedResourceData, ProjectModel), Error>.Failure(prepared.Error);
        }

        var heappeProject = await GetHeappeProjectInternalAsync(
            prepared.Value,
            request,
            heappeService,
            cancellationToken);

        if (!heappeProject.IsSuccess)
        {
            return Result<(PreparedResourceData, ProjectModel), Error>.Failure(heappeProject.Error);
        }

        return Result<(PreparedResourceData, ProjectModel), Error>.Success(
            (prepared.Value, heappeProject.Value));
    }

    /// <summary>Gets the node type matching the partition queue.</summary>
    internal static async Task<Result<(PreparedResourceData, ProjectModel, NodeTypeModel), Error>> GetNodeTypeAsync(
        this Task<Result<(PreparedResourceData, ProjectModel), Error>> dataTask,
        IHeappeService heappeService,
        CancellationToken cancellationToken)
    {
        var data = await dataTask;
        if (!data.IsSuccess)
        {
            return Result<(PreparedResourceData, ProjectModel, NodeTypeModel), Error>.Failure(data.Error);
        }

        var (prepared, heappeProject) = data.Value;

        var nodeType = await GetNodeTypeInternalAsync(
            prepared,
            heappeProject,
            heappeService,
            cancellationToken);

        if (!nodeType.IsSuccess)
        {
            return Result<(PreparedResourceData, ProjectModel, NodeTypeModel), Error>.Failure(nodeType.Error);
        }

        return Result<(PreparedResourceData, ProjectModel, NodeTypeModel), Error>.Success(
            (prepared, heappeProject, nodeType.Value));
    }

    /// <summary>Builds the <see cref="CommandTemplateCreationRequest"/> from aggregated pipeline data.</summary>
    /// <remarks>
    /// Checks for an existing HEAppE template (via <paramref name="request"/> name against the project's template list)
    /// and for an existing project template (in-memory via the already-loaded <see cref="PreparedResourceData.Partition"/> graph).
    /// Both results are set on the returned <see cref="CommandTemplateCreationRequest"/> so the creation service
    /// can decide which scenario to execute without issuing additional DB queries.
    /// </remarks>
    internal static async Task<Result<CommandTemplateCreationRequest, Error>> BuildCommandTemplateRequest(
        this Task<Result<(PreparedResourceData, ProjectModel, NodeTypeModel), Error>> dataTask,
        CreateTemplateRequest request)
    {
        var data = await dataTask;
        if (!data.IsSuccess)
        {
            return Result<CommandTemplateCreationRequest, Error>.Failure(data.Error);
        }

        var (prepared, heappeProject, nodeType) = data.Value;
        var nameUpdatedRequest = request with { Name = $"{prepared.Project.ShortName}_{request.Name}" };

        var existingProjectTemplate = prepared.Partition.ProjectCommandTemplatePartitions
            .Select(p => p.ProjectCommandTemplate)
            .FirstOrDefault(t =>
                t.Name.Equals(nameUpdatedRequest.Name, StringComparison.OrdinalIgnoreCase)
                && t.ProjectId == prepared.Project.Id
                && t.ProjectResourceId == prepared.Resource.Resource.Id);

        return Result<CommandTemplateCreationRequest, Error>.Success(
            BuildCommandTemplateRequestInternal(
                nameUpdatedRequest,
                heappeProject,
                nodeType,
                prepared.ConnectionInfo,
                prepared.LocationId,
                prepared.Project.Id,
                prepared.Resource.Resource.Id,
                existingProjectTemplate));
    }

    /// <summary>Creates the command template from a fully prepared <see cref="CommandTemplateCreationRequest"/>.</summary>
    internal static async Task<Result<Error>> CreateCommandTemplateAsync(
        this Task<Result<CommandTemplateCreationRequest, Error>> requestTask,
        IProjectCommandTemplateCreateService createService,
        Microsoft.Extensions.Caching.Hybrid.HybridCache cache,
        string lexisProjectShortName,
        CancellationToken cancellationToken)
    {
        var request = await requestTask;
        if (!request.IsSuccess)
        {
            return Result<Error>.Failure(request.Error, request.State);
        }

        var result = await createService.CreateCommandTemplateAsync(request.Value, cancellationToken);

        if (result.IsSuccess)
        {
            await cache.RemoveByTagAsync(CacheTags.TemplatesInfo(lexisProjectShortName), cancellationToken);
            await cache.RemoveByTagAsync(CacheTags.HeappeClusters(request.Value.ServiceConnectionInfo.Identifier), cancellationToken);
        }
        return result;
    }

    /// <summary>Validates and prepares resource data for creation.</summary>
    private static async Task<Result<PreparedResourceData, Error>> ValidateAndPrepareResourceDataInternalAsync(
        CreateTemplateRequest request,
        IUserorgService userorgService,
        HeappeIntegrationDbContext context,
        IHeappeConnectionFactory connectionFactory,
        CommandTemplateWriteGuard writeGuard,
        CancellationToken cancellationToken)
    {
        var project = await userorgService.GetProjectResourceAllocationAsync(
            request.ProjectIdentifier.ProjectShortName,
            request.ProjectIdentifier.ResourceName,
            cancellationToken);

        if (!project.IsSuccess)
        {
            return Result<PreparedResourceData, Error>.Failure(project.Error);
        }

        var locationNamesResult = await userorgService.GetValidLocationNames(
            request.ProjectIdentifier.ProjectShortName,
            request.ProjectIdentifier.ResourceName,
            [QuantumLocationTypeId, HpcLocationTypeId],
            cancellationToken);

        if (!locationNamesResult.IsSuccess)
        {
            return Result<PreparedResourceData, Error>.Failure(locationNamesResult.Error, locationNamesResult.State);
        }

        var locationNames = locationNamesResult.Value;
        var locationKV = locationNames.FirstOrDefault(x => x.LocationName == request.LocationName && x.AggregationName == request.AggregationName);
        if (locationKV is not { })
        {
            return Result<PreparedResourceData, Error>.Failure(Errors.InvalidLocation());
        }

        var locationId = locationKV.LocationId;

        // Location-grain write guard (ADR-0077 §5): Create acts on a Location BEFORE any
        // ProjectCommandTemplateId exists, so it is checked as soon as the Location is resolved —
        // before any HEAppE, credentials, or partition lookup — and ignores the whitelist
        // (whitelisting governs runnability on the read paths, not writability).
        var frozenError = await writeGuard.RejectIfFrozenAsync(
            new Dictionary<Guid, string> { [locationId] = request.LocationName },
            cancellationToken);
        if (frozenError is not null)
        {
            return Result<PreparedResourceData, Error>.Failure(frozenError);
        }

        var resource = await userorgService.GetProjectResourceAllocationWithLocationsAsync(
            request.ProjectIdentifier.ProjectShortName,
            request.ProjectIdentifier.ResourceName,
            cancellationToken);

        if (!resource.IsSuccess)
        {
            return Result<PreparedResourceData, Error>.Failure(resource.Error);
        }

        var resourceLocation = resource.Value.Resource.Locations
            .FirstOrDefault(l => l.LocationId == locationId);

        if (resourceLocation is null)
        {
            return Result<PreparedResourceData, Error>.Failure(Errors.ResourceLocationNotFound());
        }

        var connection = await connectionFactory.ForServiceAsync(
            resourceLocation.HeappeInstance.Identifier,
            cancellationToken);

        if (!connection.IsSuccess)
        {
            return Result<PreparedResourceData, Error>.Failure(connection.Error);
        }

        var connectionInfo = (ServiceTokenHeappeConnectionInfo)connection.Value;

        string? requestedAggregationName = string.IsNullOrEmpty(request.AggregationName)
            ? null
            : request.AggregationName;

        // Pre-compute filtered include parameters so only the matching partition record is loaded,
        // avoiding a cartesian product over all partitions for this location.
        var prefixedTemplateName = $"{project.Value.ShortName}_{request.Name}";
        var projectId = project.Value.Id;
        var resourceId = resource.Value.Resource.Id;

        var partition = await context.LocationPartitions
            .AsNoTracking()
            .Include(p => p.ProjectCommandTemplatePartitions
                .Where(pct =>
                    pct.ProjectCommandTemplate.Name == prefixedTemplateName
                    && pct.ProjectCommandTemplate.ProjectId == projectId
                    && pct.ProjectCommandTemplate.ProjectResourceId == resourceId))
                .ThenInclude(pct => pct.ProjectCommandTemplate)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Name == request.PartitionName
                && p.AggregationName == requestedAggregationName
                && p.LocationId == locationId,
                cancellationToken);

        if (partition is null)
        {
            return Result<PreparedResourceData, Error>.Failure(Errors.PartitionNotFound());
        }

        return Result<PreparedResourceData, Error>.Success(
            new PreparedResourceData(
                project.Value,
                resource.Value,
                locationId,
                request.LocationName,
                connectionInfo,
                partition));
    }

    /// <summary>Gets the Heappe project by resource name.</summary>
    private static async Task<Result<ProjectModel, Error>> GetHeappeProjectInternalAsync(
        PreparedResourceData resourceData,
        CreateTemplateRequest request,
        IHeappeService heappeService,
        CancellationToken cancellationToken)
        => await heappeService.GetProjectByResourceName(
            resourceData.ConnectionInfo,
            request.ProjectIdentifier.ResourceName,
            cancellationToken);

    /// <summary>Gets the node type matching the queue and project.</summary>
    private static async Task<Result<NodeTypeModel, Error>> GetNodeTypeInternalAsync(
        PreparedResourceData resourceData,
        ProjectModel heappeProject,
        IHeappeService heappeService,
        CancellationToken cancellationToken)
    {
        var nodeTypesResult = await heappeService.GetClusters(
            resourceData.ConnectionInfo,
            ct: cancellationToken);

        if (!nodeTypesResult.IsSuccess)
        {
            return Result<NodeTypeModel, Error>.Failure(nodeTypesResult.Error);
        }

        var nodeType = nodeTypesResult.Value
            .Where(x => x.Name.Equals(resourceData.LocationName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.NodeTypes.Where(n => n.Projects.Any(p => p.Id == heappeProject.Id)))
            .Where(x => x.Queue.Equals(resourceData.Partition.Name, StringComparison.OrdinalIgnoreCase)
                && (x.ClusterNodeTypeAggregation.Name != null && x.ClusterNodeTypeAggregation.Name.Equals(resourceData.Partition.AggregationName, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault();

        return nodeType is null
            ? Result<NodeTypeModel, Error>.Failure(Errors.NodeTypeForQueueNotFound())
            : Result<NodeTypeModel, Error>.Success(nodeType);
    }

    /// <summary>Builds the creation request from aggregated pipeline data.</summary>
    /// <remarks>
    /// The ExecutionScriptPath and PreparationScriptPath provided in the request should be COMPLETE paths.
    /// No path construction or prefix addition is performed by this method - the paths are used as-is.
    /// </remarks>
    private static CommandTemplateCreationRequest BuildCommandTemplateRequestInternal(
        CreateTemplateRequest request,
        ProjectModel heappeProject,
        NodeTypeModel nodeType,
        BaseHeappeConnectionInfo connectionInfo,
        Guid locationId,
        Guid projectId,
        Guid resourceId,
        ProjectCommandTemplate? existingProjectTemplate = null)
    {
        var executablePath = NormalizeNullableStringInternal(request.ExecutionScriptPath);
        var parameters = request.CommandTemplateParameters
            .Select(p => new CommandTemplateParameterCreateModel(p.Name, p.Description, null))
            .ToList();

        var cmdModel = new CommandTemplateCreateModel(
            request.Name,
            request.Description,
            default,
            executablePath,
            NormalizeNullableStringInternal(request.PreparationScriptPath),
            heappeProject.Id,
            nodeType.Id,
            parameters,
            default);

        var existingHeappeTemplate = heappeProject.CommandTemplates
            .FirstOrDefault(ct => ct.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase));

        return CommandTemplateCreationRequest.WithCreateModel(
            cmdModel,
            connectionInfo,
            heappeProject,
            nodeType,
            locationId,
            projectId,
            resourceId,
            existingProjectTemplate: existingProjectTemplate,
            existingHeappeTemplate: existingHeappeTemplate);
    }

    /// <summary>Normalizes empty strings to null.</summary>
    private static string? NormalizeNullableStringInternal(string? value)
        => string.IsNullOrEmpty(value) ? null : value;
}