using System.Data.Common;
using System.Globalization;

using Heappe.Contracts;
using Heappe.Contracts.CommandTemplate;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries.Sync;
using Heappe.Contracts.HEappeApiDto.Responses;
using Heappe.Features.Connection;
using Heappe.Features.CreateProjectCommandTemplate;
using Heappe.Features.CreateProjectCommandTemplate.Projections;
using Heappe.Features.KillSwitch;
using Heappe.Features.LocationPartitionsSynchronization;
using Heappe.Features.Nexus;
using Heappe.HostContracts;
using Heappe.Persistence;
using Heappe.Persistence.Entities;
using Heappe.Persistence.Enums;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using HeappeErrors = Heappe.Errors;
using ValidationError = Lukdrasil.Error.ValidationError;
namespace Heappe.Features.CommandTemplateSynchronization;

/// <summary>
/// Synchronizes project command templates with HEAppE clusters and their node types across multiple locations.
/// </summary>
/// <param name="logger">Logs synchronization operations and diagnostic information.</param>
/// <param name="userService">Retrieves project resource allocation and location data.</param>
/// <param name="heappeService">Communicates with HEAppE instances to retrieve clusters, projects, and command templates.</param>
/// <param name="connectionFactory">Builds the service connection (Nexus proxy route and Vault key) of a HEAppE instance.</param>
/// <param name="locationPartitionsSynchronizationService">Synchronizes location partition data for HEAppE clusters.</param>
/// <param name="createService">Creates and updates command template records.</param>
/// <param name="dbContext">Database context for accessing command template and location partition entities.</param>
/// <param name="killSwitchStateService">Determines whether a target Location is frozen by an enforced kill-switch
/// (location-grain, whitelist ignored) so sync can skip it (ADR-0077 §6).</param>
/// <param name="options">Supplies <c>TokenExchange:Nexus:BaseUrl</c>, the proxy base reported for an instance without a URL.</param>
internal class ProjectCommandTemplateSyncService(
    ILogger<ProjectCommandTemplateSyncService> logger,
    IUserorgService userService,
    IHeappeService heappeService,
    IHeappeConnectionFactory connectionFactory,
    ILocationPartitionsSynchronizationService locationPartitionsSynchronizationService,
    IProjectCommandTemplateCreateService createService,
    HeappeIntegrationDbContext dbContext,
    HybridCache cache,
    IKillSwitchStateService killSwitchStateService,
    IOptions<TokenExchangeOptions> options) : IProjectCommandTemplateSyncService
{
    /// <summary>
    /// Synchronizes command templates for a project resource across all associated HEAppE instances and their node
    /// types.
    /// </summary>
    /// <remarks>This method retrieves project allocation data, processes expiration, synchronizes location
    /// partitions, and creates or updates command templates for each HEAppE instance, cluster node type, and location
    /// combination. Errors are accumulated and returned if any synchronization step fails.</remarks>
    /// <param name="projectShortName">Short name of the project.</param>
    /// <param name="projectResourceName">Name of the project resource.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A validation result containing a <see cref="CommandTemplateSyncResult"/> describing the per-location
    /// outcome on success, or validation errors if an early (pre-location) synchronization step fails or no location
    /// succeeded.</returns>
    public async Task<ValidationResult<CommandTemplateSyncResult>> SyncCommandTemplatesForResource(string projectShortName, string projectResourceName, CancellationToken ct)
    {
        logger.LogProjectSynchronizationStart(projectShortName, projectResourceName);
        var projectAllocationResult = await userService.GetProjectResourceAllocationWithLocationsAsync(projectShortName, projectResourceName, ct);
        if (!projectAllocationResult.IsSuccess)
        {
            logger.LogProjectResourceAllocationObtainingError(projectShortName, projectResourceName, projectAllocationResult.Error);
            return ValidationResult<CommandTemplateSyncResult>.Failure(projectAllocationResult.MapError(x => new ValidationError(x)).Error, projectAllocationResult.State);
        }

        var projectAllocation = projectAllocationResult.Value;
        var allocationExpirationResult = await ProcessAllocationExpiration(projectAllocation, ct);
        if (!allocationExpirationResult.IsSuccess)
        {
            return ValidationResult<CommandTemplateSyncResult>.Failure(allocationExpirationResult.MapError(x => new ValidationError(x)).Error, allocationExpirationResult.State);
        }

        //Grouped by HEAppE Instance Uri, and Identifier
        var locationGroups = BuildLocationGroups(projectAllocation);
        if (locationGroups.Keys.Count == 0)
        {
            logger.LogProjectResourceNoHeappeLocations(projectShortName, projectResourceName);
            // Must be `new ValidationError(err)`, never the collection expression `[err]`:
            // ValidationError has a parameterless ctor, implements IEnumerable<BaseError> and has an
            // instance Add, so `[err]` binds to the collection-initializer pattern and lowers to
            // `new ValidationError(); .Add(err);` — and Add is immutable (returns a new instance),
            // so its result is discarded and the error is silently EMPTY.
            return ValidationResult<CommandTemplateSyncResult>.Failure(new ValidationError(Errors.MissingHEAppEUrlInResource(projectAllocation.Resource.Name)), projectAllocationResult.State);
        }

        locationGroups = await SkipFrozenLocationsAsync(locationGroups, projectShortName, projectResourceName, ct);
        if (locationGroups.Keys.Count == 0)
        {
            // Every location the resource has was frozen and skipped above (each already logged
            // its own Warning) — report a real, descriptive, reason-free error rather than an
            // empty one. Uses `new ValidationError(err)` for the reason given above.
            return ValidationResult<CommandTemplateSyncResult>.Failure(new ValidationError(Errors.AllLocationsFrozen(projectShortName, projectResourceName)), State.Error);
        }

        var assignedCmdTemplates = await LoadAssignedCommandTemplatesAsync(projectAllocation, ct);

        // Per-location error accumulator, keyed by (LocationId, AggregationName). A location is
        // considered successful iff no errors landed against its key. This dictionary is the single
        // source of truth for failures; the flat error list is derived from it at the end.
        var locationErrors = new Dictionary<(Guid LocationId, string AggregationName), List<Error>>();
        var locationsSkippedForPartitionFailure = new HashSet<(Guid LocationId, string AggregationName)>();

        void AddLocationError(LocationAggregationEntry loc, Error error)
        {
            var key = (loc.LocationId, loc.AggregationName ?? string.Empty);
            if (!locationErrors.TryGetValue(key, out var list))
            {
                list = [];
                locationErrors[key] = list;
            }

            list.Add(error);
        }

        // A failure affecting a whole HEAppE instance (bad connection, missing project, etc.)
        // blocks every location grouped under it — record it against each.
        void FailInstance(HeappeInstanceSyncEntry instance, Error error)
        {
            foreach (var loc in locationGroups[instance])
            {
                AddLocationError(loc, error);
            }
        }

        foreach (var heappeInstance in locationGroups.Keys)
        {
            // Synchronize location partitions for the HEAppE instance. A partition failure is
            // attributed to the affected location(s) and excludes them from per-template sync.
            await ProcessLocationPartitionSynchronizationAsync(heappeInstance, locationGroups[heappeInstance], projectResourceName, AddLocationError, locationsSkippedForPartitionFailure, ct);

            var serviceConnectionInfoResult = await GetHeappeConnectionInfoAsync(heappeInstance, projectShortName, projectResourceName, ct);
            if (!serviceConnectionInfoResult.IsSuccess)
            {
                FailInstance(heappeInstance, serviceConnectionInfoResult.Error);
                continue;
            }

            var serviceConnectionInfo = serviceConnectionInfoResult.Value;
            var heappeProject = await heappeService.GetProjectByResourceName(serviceConnectionInfo, projectAllocation.Resource.Name, ct);
            if (!heappeProject.IsSuccess)
            {
                logger.LogHeappeProjectNotFound(projectAllocation.Resource.Name, serviceConnectionInfo.Identifier, projectShortName);
                FailInstance(heappeInstance, Errors.HeappeProjectNotFound(projectAllocation.Resource.Name, serviceConnectionInfo.Identifier, projectShortName));
                continue;
            }

            var heappeCommandTemplatesPerProjectResponse = await heappeService.GetProjectCommandTemplatesAsync(serviceConnectionInfo, heappeProject.Value.Id, ct);
            if (!heappeCommandTemplatesPerProjectResponse.IsSuccess)
            {
                logger.LogFailedToRetrieveHeappeCommandTemplates(heappeProject.Value.Id, serviceConnectionInfo.Identifier, projectShortName, projectResourceName, heappeCommandTemplatesPerProjectResponse.Error);
                FailInstance(heappeInstance, Errors.FailedToRetrieveHeappeCommandTemplates(heappeProject.Value.Id, serviceConnectionInfo.Identifier, projectShortName, projectResourceName));
                continue;
            }

            var listClustersResult = await heappeService.GetClusters(serviceConnectionInfo, ct);
            if (!listClustersResult.IsSuccess)
            {
                logger.LogFailedToListHeappeClusters(serviceConnectionInfo.Identifier, projectShortName, projectResourceName, listClustersResult.Error);
                FailInstance(heappeInstance, Errors.FailedToListHeappeClusters(serviceConnectionInfo.Identifier, projectShortName, projectResourceName));
                continue;
            }
            var heappeCommandTemplatesPerProject = heappeCommandTemplatesPerProjectResponse.Value;

            var nodeTypesAggregationByCluster = BuildNodeTypesAggregationByCluster(listClustersResult.Value);
            foreach (var locationAggregation in locationGroups[heappeInstance])
            {
                // A location whose partition sync failed must not sync templates against stale partitions.
                if (locationsSkippedForPartitionFailure.Contains((locationAggregation.LocationId, locationAggregation.AggregationName ?? string.Empty)))
                {
                    continue;
                }

                if (!nodeTypesAggregationByCluster.TryGetValue(locationAggregation.LocationName, out var nodeTypesByAggregation))
                {
                    logger.LogClusterMissing(locationAggregation.LocationName, serviceConnectionInfo.Identifier, projectShortName, projectResourceName);
                    AddLocationError(locationAggregation, Errors.HeappeClusterMissing(locationAggregation.LocationName, serviceConnectionInfo.Identifier, projectShortName, projectResourceName));
                    continue;
                }

                if (!nodeTypesByAggregation.TryGetValue(locationAggregation.AggregationName, out var nodeTypes))
                {
                    logger.LogClusterWithAggregationMissing(locationAggregation.LocationName, locationAggregation.AggregationName, serviceConnectionInfo.Identifier, projectShortName, projectResourceName);
                    AddLocationError(locationAggregation, Errors.HeappeClusterAggregationMissing(locationAggregation.LocationName, locationAggregation.AggregationName, serviceConnectionInfo.Identifier, projectShortName, projectResourceName));
                    continue;
                }

                var commandTemplatesDefinitionResult = await GetCommandTemplateDefinitionsAsync(locationAggregation.LocationType, locationAggregation.LocationId, heappeProject.Value.KeyScriptsDirectoryPath, ct);
                if (!commandTemplatesDefinitionResult.IsSuccess)
                {
                    AddLocationError(locationAggregation, commandTemplatesDefinitionResult.Error);
                    continue;
                }

                foreach (var syncTemplateDefinition in commandTemplatesDefinitionResult.Value)
                {
                    var reducedNodeTypes = syncTemplateDefinition.LocationPartition != null
                        ? nodeTypes.Where(w =>
                                w.Name.Equals(syncTemplateDefinition.LocationPartition.Name, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(w.ClusterNodeTypeAggregation?.Name, syncTemplateDefinition.LocationPartition.AggregationName, StringComparison.OrdinalIgnoreCase))
                            .ToList()
                        : nodeTypes;

                    await SyncTemplateDefinitionForNodeTypesAsync(
                       syncTemplateDefinition,
                       reducedNodeTypes,
                       heappeCommandTemplatesPerProject,
                       assignedCmdTemplates,
                       serviceConnectionInfo,
                       heappeProject.Value,
                       locationAggregation,
                       projectAllocation,
                       projectShortName,
                       projectResourceName,
                       e => AddLocationError(locationAggregation, e),
                       ct);
                }
            }
        }

        // Command-template partitions/states may have changed during the sync, so evict the cached
        // permission responses for this project even if some per-template operations reported errors.
        await cache.RemoveByTagAsync(HeappeCacheKeys.PermissionProjectTag(projectAllocation.Id), ct);

        // The TemplatesInfo response for this project is built from these states; evict it too.
        await cache.RemoveByTagAsync(CacheTags.TemplatesInfo(projectShortName), ct);

        // Synchronization pulled fresh HEAppE data, so the raw cluster cache for the touched
        // instances may be stale — drop it per HEAppE identifier.
        foreach (var instanceForCacheEviction in locationGroups.Keys)
        {
            await cache.RemoveByTagAsync(CacheTags.HeappeClusters(instanceForCacheEviction.Identifier), ct);
        }

        var locationResults = BuildLocationResults(locationGroups, locationErrors);
        var syncResult = new CommandTemplateSyncResult(locationResults);

        // Derive the flat error list from the per-location results — the location error dictionary
        // is the single source of truth, so there is no separate error accumulator to keep in sync.
        ValidationError errors = [];
        foreach (var error in locationResults.SelectMany(r => r.Errors))
        {
            errors = errors.Add(error);
        }

        if (!syncResult.AnySucceeded)
        {
            logger.LogProjectSynchronizationErrors(projectShortName, projectResourceName, errors);
            return ValidationResult<CommandTemplateSyncResult>.Failure(errors);
        }

        if (!syncResult.AllSucceeded)
        {
            logger.LogProjectSynchronizationErrors(projectShortName, projectResourceName, errors);
        }
        else
        {
            logger.LogProjectSynchronizationFinish(projectShortName, projectResourceName);
        }

        return ValidationResult<CommandTemplateSyncResult>.Success(syncResult);
    }

    private List<LocationSyncResultEntry> BuildLocationResults(
        Dictionary<HeappeInstanceSyncEntry, List<LocationAggregationEntry>> locationGroups,
        Dictionary<(Guid LocationId, string AggregationName), List<Error>> locationErrors)
    {
        var locationResults = new List<LocationSyncResultEntry>();
        foreach (var (heappeInstance, locations) in locationGroups)
        {
            foreach (var loc in locations)
            {
                locationErrors.TryGetValue((loc.LocationId, loc.AggregationName ?? string.Empty), out var locErrors);
                locationResults.Add(new LocationSyncResultEntry(
                    loc.LocationName,
                    string.IsNullOrEmpty(loc.AggregationName) ? null : loc.AggregationName,
                    heappeInstance.Uri ?? NexusEndpoints.ProxyBaseFor(options.Value.Nexus.BaseUrl!, heappeInstance.Identifier),
                    IsSuccess: locErrors is null or { Count: 0 },
                    Errors: locErrors ?? []));
            }
        }

        return locationResults;
    }


    /// <summary>
    /// Checks if a project allocation has expired and deactivates associated command template partitions if the
    /// allocation or resource is outside its valid date range.
    /// </summary>
    /// <param name="projectAllocation">Project allocation entry to evaluate for expiration.</param>
    /// <param name="ct">Token to observe while waiting for the task to complete.</param>
    /// <returns>Success with true if the allocation is active; failure with an error if expired or if command template state
    /// update fails.</returns>
    private async Task<Result<bool, Error>> ProcessAllocationExpiration(ProjectResourceInfoSyncEntry projectAllocation, CancellationToken ct)
    {
        var actualDate = DateTime.UtcNow;
        var isActive = projectAllocation.StartDate <= actualDate && actualDate <= projectAllocation.EndDate
                            && projectAllocation.Resource.StartDate <= actualDate && actualDate <= projectAllocation.Resource.EndDate
            ? true
            : false;

        if (isActive)
        {
            return Result<bool, Error>.Success(true);
        }

        var containsProjectCmdTemplates = await dbContext.Set<ProjectCommandTemplate>().AnyAsync(a => a.ProjectId == projectAllocation.Id && a.ProjectResourceId == projectAllocation.Resource.Id, ct);
        if (containsProjectCmdTemplates)
        {
            var changes = await dbContext.Set<ProjectCommandTemplatePartition>()
                .Where(p => p.ProjectCommandTemplate.ProjectId == projectAllocation.Id
                                                    && p.ProjectCommandTemplate.ProjectResourceId == projectAllocation.Resource.Id)
                .ExecuteUpdateAsync(upd => upd.SetProperty(p =>
                    p.State,
                    _ => ECommandTemplateState.Inactive), ct);

            if (changes == 0)
            {
                logger.LogProjectResourceExpirationStateUpdateFailure(projectAllocation.ShortName, projectAllocation.Resource.Name);
                return Result<bool, Error>.Failure(Errors.FailedToUpdateCommandTemplateState(projectAllocation.Name, projectAllocation.Resource.Name), State.Error);
            }
        }

        logger.LogProjectOrResourceExpiration(projectAllocation.ShortName, projectAllocation.Resource.Name);
        return Result<bool, Error>.Failure(Errors.ProjectOrResourceExpiredWarning(projectAllocation.Name, projectAllocation.Resource.Name), State.Error);
    }

    /// <summary>
    /// Loads command templates assigned to a project allocation including partition and location details.
    /// </summary>
    /// <param name="projectAllocation">Project allocation containing the project and resource identifiers.</param>
    /// <param name="ct">Token to monitor for cancellation requests.</param>
    /// <returns>List of assigned command templates with their associated partitions and location data.</returns>
    private async Task<List<ProjectCommandTemplate>> LoadAssignedCommandTemplatesAsync(ProjectResourceInfoSyncEntry projectAllocation, CancellationToken ct)
    {
        var assignedCmdTemplates = await dbContext.Set<ProjectCommandTemplate>()
           .TagWith($"ProjectCommandTemplateSync: Retrieving assigned command templates with definition for project allocation.")
           .AsNoTracking()
           .Include(i => i.CommandTemplateDefinition)
           .Where(w => w.CommandTemplateDefinition != null)
           .Include(i => i.ProjectCommandTemplatePartitions)
               .ThenInclude(p => p.LocationPartition)
           .AsSplitQuery()
           .Where(w => w.ProjectId == projectAllocation.Id && w.ProjectResourceId == projectAllocation.Resource.Id)
           .ToListAsync(ct);

        return assignedCmdTemplates;
    }

    /// <summary>
    /// Groups resource locations by Heappe instance and creates location aggregation entries.
    /// </summary>
    /// <remarks>Aggregation names are normalized to uppercase using invariant culture. Null aggregation names
    /// are converted to empty strings.</remarks>
    /// <param name="projectAllocation">The project allocation containing resource locations to group.</param>
    /// <returns>A dictionary mapping each Heappe instance to its associated location aggregation entries.</returns>
    private static Dictionary<HeappeInstanceSyncEntry, List<LocationAggregationEntry>> BuildLocationGroups(ProjectResourceInfoSyncEntry projectAllocation)
    {
        return projectAllocation.Resource.Locations
            .GroupBy(g => g.HeappeInstance)
            .ToDictionary(
                k => k.Key,
                v => v.Select(s =>
                    new LocationAggregationEntry
                        (
                            s.LocationId,
                            s.LocationName,
                            s.LoginEndpoint,
                            s.LocationType,
                            s.AggregationName?.ToUpper(CultureInfo.InvariantCulture) ?? string.Empty
                        )
                 ).ToList()
             );
    }

    /// <summary>
    /// Removes locations frozen by an enforced kill-switch from the location groups (ADR-0077 §6):
    /// a frozen location must not be re-synced during an incident. This is location-grain — the
    /// whitelist is deliberately ignored, since sync acts on the Location itself, often before any
    /// specific command template exists (B02 SHIPPED API). A frozen location is excluded from the
    /// run entirely rather than reported as a failed <see cref="LocationSyncResultEntry"/>, because
    /// ADR-0075's per-location contract has no "skipped" state and a deliberate skip is not a failure.
    /// </summary>
    /// <param name="locationGroups">The location groups built from the resource's assigned locations.</param>
    /// <param name="projectShortName">Short name of the project, for the skip log.</param>
    /// <param name="projectResourceName">Name of the project resource, for the skip log.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The location groups with every frozen location removed; instances left with no
    /// remaining locations are dropped entirely.</returns>
    private async Task<Dictionary<HeappeInstanceSyncEntry, List<LocationAggregationEntry>>> SkipFrozenLocationsAsync(
        Dictionary<HeappeInstanceSyncEntry, List<LocationAggregationEntry>> locationGroups,
        string projectShortName,
        string projectResourceName,
        CancellationToken ct)
    {
        var distinctLocations = locationGroups.Values.SelectMany(v => v).DistinctBy(l => l.LocationId).ToList();
        var frozenLocationIds = await killSwitchStateService.GetEnforcedKilledLocationIdsAsync(
            distinctLocations.Select(l => l.LocationId).ToList(), ct);
        if (frozenLocationIds.Count == 0)
        {
            return locationGroups;
        }

        foreach (var frozen in distinctLocations.Where(l => frozenLocationIds.Contains(l.LocationId)))
        {
            logger.LogLocationFrozenSkipped(frozen.LocationName, frozen.LocationId, projectShortName, projectResourceName);
        }

        return locationGroups
            .Select(kv => (Instance: kv.Key, Locations: kv.Value.Where(l => !frozenLocationIds.Contains(l.LocationId)).ToList()))
            .Where(g => g.Locations.Count > 0)
            .ToDictionary(g => g.Instance, g => g.Locations);
    }

    /// <summary>
    /// Groups node types by cluster name and aggregation name, removing duplicate aggregations.
    /// </summary>
    /// <param name="listClusters">Cluster details containing node types to aggregate.</param>
    /// <returns>Dictionary mapping cluster names to aggregation name dictionaries, each containing distinct node type lists.</returns>
    private static Dictionary<string, Dictionary<string, List<NodeTypeModel>>> BuildNodeTypesAggregationByCluster(IList<ClusterDetailedModel> listClusters)
    {
        return listClusters
            .GroupBy(g => g.Name)
            .ToDictionary(
                k => k.Key,
                v => v.SelectMany(se => se.NodeTypes)
                    .GroupBy(g => g.ClusterNodeTypeAggregation?.Name ?? string.Empty)
                    .ToDictionary(
                        k => k.Key,
                        v => v.DistinctBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                        StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Retrieves connection information for the specified Heappe instance.
    /// </summary>
    /// <param name="heappeInstance">The Heappe instance to retrieve connection information for.</param>
    /// <param name="projectShortName">The short name of the project used for error context.</param>
    /// <param name="projectResourceName">The project resource name used for error context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result containing the connection information if successful, or an error if validation or credential retrieval
    /// fails.</returns>
    private async Task<Result<BaseHeappeConnectionInfo, Error>> GetHeappeConnectionInfoAsync(HeappeInstanceSyncEntry heappeInstance, string projectShortName, string projectResourceName, CancellationToken ct)
    {
        var connection = await connectionFactory.ForServiceAsync(heappeInstance.Identifier, ct);
        if (!connection.IsSuccess)
        {
            logger.LogServiceConnectionFailed(heappeInstance.Identifier, projectShortName, projectResourceName, connection.Error.Code);
        }

        return connection;
    }

    // <summary>
    /// Creates or updates a command template for each node type matching the given template definition.
    /// </summary>
    private async Task SyncTemplateDefinitionForNodeTypesAsync(
        CommandTemplateDefinitionEntry syncTemplateDefinition,
        IList<NodeTypeModel> nodeTypes,
        IList<CommandTemplateModel> heappeCommandTemplatesPerProject,
        List<ProjectCommandTemplate> assignedCmdTemplates,
        BaseHeappeConnectionInfo serviceConnectionInfo,
        ProjectModel heappeProject,
        LocationAggregationEntry locationAggregation,
        ProjectResourceInfoSyncEntry projectAllocation,
        string projectShortName,
        string projectResourceName,
        Action<Error> onLocationError,
        CancellationToken ct)
    {
        foreach (var nodeType in nodeTypes)
        {
            var heappeCommandTemplate = heappeCommandTemplatesPerProject.FirstOrDefault(w =>
                w.ClusterNodeTypeId == nodeType.Id && syncTemplateDefinition.Name == w.Name && w.IsEnabled);

            var heappeCommandTemplateModel = heappeCommandTemplate != null
                ? CommandTemplateCompactModel.FromCommandTemplateModel(heappeCommandTemplate)
                : default(CommandTemplateCompactModel);

            var projectCommandTemplate = assignedCmdTemplates.FirstOrDefault(w =>
                w.Name == syncTemplateDefinition.Name &&
                w.ProjectCommandTemplatePartitions.Any(a =>
                    a.LocationPartition.Name == nodeType.Name
                    && a.LocationPartition.AggregationName == nodeType.ClusterNodeTypeAggregation?.Name
                    && a.LocationPartition.LocationId == locationAggregation.LocationId));

            var request = CommandTemplateCreationRequest.WithDefinition(
                syncTemplateDefinition,
                serviceConnectionInfo,
                heappeProject,
                nodeType,
                locationAggregation.LocationId,
                projectAllocation.Id,
                projectAllocation.Resource.Id,
                projectCommandTemplate,
                heappeCommandTemplateModel);

            var createResult = await createService.CreateCommandTemplateAsync(request, ct);
            if (!createResult.IsSuccess)
            {
                logger.LogCommandTemplateSyncFailure(syncTemplateDefinition.Name, serviceConnectionInfo.Identifier, nodeType.Name, locationAggregation.LocationId, projectShortName, projectResourceName);
                onLocationError(Errors.CommandTemplateSyncFailure(syncTemplateDefinition.Name, serviceConnectionInfo.Identifier, nodeType.Name, locationAggregation.LocationId, projectShortName, projectResourceName));
            }
        }
    }

    /// <summary>
    /// Synchronizes location partitions for each distinct location in the provided aggregations.
    /// </summary>
    /// <remarks>Continues processing remaining locations when individual synchronization operations fail. All
    /// failures are accumulated in the errors collection.</remarks>
    /// <param name="heappeInstance">Heappe instance containing configuration for synchronization.</param>
    /// <param name="locationAggregations">Collection of location aggregation entries to extract locations from.</param>
    /// <param name="projectResourceName">Name of the project resource being synchronized.</param>
    /// <param name="addLocationError">Callback attributing a partition-sync error to a specific location aggregation.</param>
    /// <param name="locationsSkippedForPartitionFailure">Set of (LocationId, AggregationName) keys whose partition sync
    /// failed; these locations must be skipped for per-template sync.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    private async Task ProcessLocationPartitionSynchronizationAsync(
        HeappeInstanceSyncEntry heappeInstance,
        IList<LocationAggregationEntry> locationAggregations,
        string projectResourceName,
        Action<LocationAggregationEntry, Error> addLocationError,
        HashSet<(Guid LocationId, string AggregationName)> locationsSkippedForPartitionFailure,
        CancellationToken ct)
    {
        var locationsToSync = locationAggregations
            .Select(s => new LocationSyncEntry(s.LocationId, s.LocationName, s.LoginEndpoint, heappeInstance))
            .Distinct()
            .ToList();

        foreach (var locationToSync in locationsToSync)
        {
            var locationPartitionsResult = await locationPartitionsSynchronizationService.SynchronizationLocationPartitionsAsync(locationToSync, projectResourceName, ct);
            if (locationPartitionsResult.IsSuccess)
            {
                continue;
            }

            // A single location may back several aggregations; the partition failure blocks every one of them.
            foreach (var affected in locationAggregations.Where(l => l.LocationId == locationToSync.Id))
            {
                addLocationError(affected, locationPartitionsResult.Error);
                locationsSkippedForPartitionFailure.Add((affected.LocationId, affected.AggregationName ?? string.Empty));
            }
        }
    }

    /// <summary>
    /// Retrieves command template definitions for a specified location, prioritizing partition-specific templates when
    /// available.
    /// </summary>
    /// <param name="locationType">Type of the location to retrieve templates for.</param>
    /// <param name="locationId">Unique identifier of the location.</param>
    /// <param name="heappeBaseScriptPath">Base script path used to construct complete execution command paths.</param>
    /// <param name="ct">Token to cancel the asynchronous operation.</param>
    /// <returns>Success result containing the list of command template definitions, or failure result with an error if no
    /// templates are found or a database operation fails.</returns>
    private async Task<Result<IList<CommandTemplateDefinitionEntry>, Error>> GetCommandTemplateDefinitionsAsync(ELocationType locationType, Guid locationId, string heappeBaseScriptPath, CancellationToken ct)
    {
        try
        {
            var locationPartitions = await dbContext.Set<LocationPartition>()
                .TagWith($"ProjectCommandTemplateSync: Retrieving location partition for location to get command template definitions.")
                .AsNoTracking()
                .Where(w => w.LocationId == locationId)
                .ToDictionaryAsync(
                    k => k.Id,
                    v => new LocationPartitionEntry(v.Id, v.Name, v.AggregationName),
                    ct
                );

            var locationPartitionIds = locationPartitions.Keys.ToHashSet();
            var rawTemplateDefinitions = await dbContext.Set<CommandTemplateLocationDefinition>()
                .TagWith($"ProjectCommandTemplateSync: Retrieving command template definitions for location.")
                .AsNoTracking()
                .Where(w =>
                    w.LocationType == locationType ||
                    (w.LocationPartitionId.HasValue && locationPartitionIds.Contains(w.LocationPartitionId.Value)))
                .Select(s =>
                    new CommandTemplateDefinitionEntry
                    (
                        s.CommandTemplateDefinition.Id,
                        s.CommandTemplateDefinition.Name,
                        s.CommandTemplateDefinition.Description,
                        s.CommandTemplateDefinition.Type,
                        s.PreparationCommand,
                        BuildCompletePath(s.ExecutionCommand, heappeBaseScriptPath),
                        s.LocationPartitionId.HasValue ? locationPartitions[s.LocationPartitionId.Value] : default,
                        s.CommandTemplateDefinition.ComandTemplateParameterDefinitions
                            .Select(se => new CommandTemplateDefinitionParametersEntry(se.Name, se.Description))
                            .ToList()
                    ))
                .ToListAsync(ct);

            if (rawTemplateDefinitions.Count == 0)
            {
                logger.LogNoCommandTemplateDefinitionsFound(locationType, locationId);
                return Result<IList<CommandTemplateDefinitionEntry>, Error>.Failure(Errors.NoCommandTemplateDefinitionsForLocation(locationType, locationId), State.Error);
            }

            var templateDefinitions = rawTemplateDefinitions.Any(a => a.LocationPartition != null)
                ? rawTemplateDefinitions.Where(w => w.LocationPartition != null).ToList()
                : rawTemplateDefinitions;

            return Result<IList<CommandTemplateDefinitionEntry>, Error>.Success(templateDefinitions);
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is DbException)
        {
            logger.LogGetCommandTemplateDefinitionFailure(ex, locationType, locationId);
            return Result<IList<CommandTemplateDefinitionEntry>, Error>.Failure(Errors.FailedToRetrieveCommandTemplatesForLocation(locationType, locationId), State.CriticalError);
        }
    }


    /// <summary>
    /// Builds a complete path for a command. If the command already contains path separators, assumes it's complete.
    /// Otherwise, combines it with the base directory.
    /// </summary>
    internal static string? BuildCompletePath(string? command, string baseDirectory)
    {
        if (string.IsNullOrEmpty(baseDirectory) || string.IsNullOrEmpty(command) || command.Contains('/', StringComparison.Ordinal) || command.Contains('\\', StringComparison.Ordinal))
        {
            return command;
        }

        return $"{baseDirectory.TrimEnd('/', '\\')}/{command}";
    }

    /// <summary>
    /// Represents a location entry with aggregation information.
    /// </summary>
    /// <param name="LocationId">Unique identifier of the location.</param>
    /// <param name="LocationName">Name of the location.</param>
    /// <param name="LoginEndpoint">Authentication endpoint URL for the location.</param>
    /// <param name="LocationType">Type classification of the location.</param>
    /// <param name="AggregationName">Optional name of the aggregation group the location belongs to.</param>
    private sealed record LocationAggregationEntry
    (
           Guid LocationId,
           string LocationName,
           string LoginEndpoint,
           ELocationType LocationType,
           string? AggregationName
    );

}