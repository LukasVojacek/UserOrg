using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries.Sync;
using Heappe.Features.LocationPartitionsSynchronization.Projections;
using Heappe.Helpers;
using Heappe.Persistence;
using Heappe.Persistence.Entities;

using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using UserOrg.Shared;

using HeappeErrors = Heappe.Errors;

namespace Heappe.Features.LocationPartitionsSynchronization;

/// <summary>
/// Synchronizes location queue partitions between Heappe clusters and the database.
/// </summary>
/// <remarks>Validates Heappe instance configuration, retrieves credentials, queries clusters, and matches them by
/// name or login endpoint. Soft-deletes partitions and their associated project command template partitions when queues
/// no longer exist in Heappe. Reactivates previously deleted partitions if they reappear in Heappe.</remarks>
/// <param name="logger">Logger for recording synchronization operations and errors.</param>
/// <param name="heappeService">Service for querying Heappe cluster information.</param>
/// <param name="connectionFactory">Builds the service connection (Nexus proxy route and Vault key) of a Heappe instance.</param>
/// <param name="dbContext">Database context for persisting location partition changes.</param>
internal class LocationPartitionsSynchronizationService(
    ILogger<LocationPartitionsSynchronizationService> logger,
    IHeappeService heappeService,
    IHeappeConnectionFactory connectionFactory,
    HeappeIntegrationDbContext dbContext) : ILocationPartitionsSynchronizationService
{
    /// <summary>
    /// Synchronizes queue partitions for a location from the associated Heappe cluster.
    /// </summary>
    /// <remarks>Validates Heappe instance configuration, retrieves credentials, queries clusters from Heappe,
    /// matches clusters by name or login endpoint, and synchronizes extracted node type partitions.</remarks>
    /// <param name="location">Location sync entry containing Heappe instance details and location information.</param>
    /// <param name="projectResourceName">Project resource name used for error reporting and logging.</param>
    /// <param name="ct">Token to cancel the asynchronous operation.</param>
    /// <returns>Success result if partitions are synchronized successfully, or failure with error details.</returns>
    public async Task<Result<bool, Error>> SynchronizationLocationPartitionsAsync(LocationSyncEntry location, string projectResourceName, CancellationToken ct)
    {
        logger.LogLocationPartitionsStarted(location.Id);

        var serviceConnectionInfoResult = await GetHeappeConnectionInfoAsync(location.HeappeInstance, projectResourceName, ct);
        if (!serviceConnectionInfoResult.IsSuccess)
        {
            return Result<bool, Error>.Failure(serviceConnectionInfoResult.Error, State.Error);
        }

        var locationQueuePartitionsResult = await RetrieveLocationQueuePartitionsFromHeappeAsync(serviceConnectionInfoResult.Value, location.Name, location.LoginEndpoint, ct);
        if (!locationQueuePartitionsResult.IsSuccess)
        {
            return Result<bool, Error>.Failure(locationQueuePartitionsResult.Error, State.Error);
        }

        var result = await SynchronizationLocationQueuesAsync(locationQueuePartitionsResult.Value, location.Id, location.HeappeInstance.Identifier, ct);
        if (!result.IsSuccess)
        {
            return Result<bool, Error>.Failure(result.Error, State.Error);
        }

        logger.LogLocationPartitionsSynchronized(location.Id);
        return Result<bool, Error>.Success(result.Value);
    }

    /// <summary>
    /// Retrieves connection information for the specified Heappe instance.
    /// </summary>
    /// <param name="heappeInstance">The Heappe instance to retrieve connection information for.</param>
    /// <param name="projectResourceName">The project resource name used for error context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result containing the connection information if successful, or an error if validation or credential retrieval
    /// fails.</returns>
    private async Task<Result<BaseHeappeConnectionInfo, Error>> GetHeappeConnectionInfoAsync(HeappeInstanceSyncEntry heappeInstance, string projectResourceName, CancellationToken ct)
    {
        var connection = await connectionFactory.ForServiceAsync(heappeInstance.Identifier, ct);
        if (!connection.IsSuccess)
        {
            logger.LogServiceConnectionFailed(heappeInstance.Identifier, projectResourceName, connection.Error.Code);
        }

        return connection;
    }

    /// <summary>
    /// Retrieves queue partitions for a location from HEappe by matching cluster names or login endpoints.
    /// </summary>
    /// <param name="serviceConnectionInfo">Connection information for accessing the HEappe service.</param>
    /// <param name="locationName">Name of the location to match against cluster names.</param>
    /// <param name="locationLoginEndpoint">Login endpoint to match against cluster master node names.</param>
    /// <param name="ct">Token to cancel the asynchronous operation.</param>
    /// <returns>A result containing a set of queue partition projections if successful, or an error if no matching cluster is
    /// found.</returns>
    private async Task<Result<HashSet<QueuePartitionProjection>, Error>> RetrieveLocationQueuePartitionsFromHeappeAsync(BaseHeappeConnectionInfo serviceConnectionInfo, string locationName, string locationLoginEndpoint, CancellationToken ct)
    {
        var listClustersResult = await heappeService.GetClusters(serviceConnectionInfo, ct);
        if (!listClustersResult.IsSuccess)
        {
            logger.LogFailedToListHeappeClusters(serviceConnectionInfo.Identifier, listClustersResult.Error);
            return Result<HashSet<QueuePartitionProjection>, Error>.Failure(HeappeErrors.FailedToListHeappeClusters(serviceConnectionInfo.Identifier), State.Error);
        }

        var nodeTypesByCluster =
            listClustersResult.Value.Where(w => w.Name.Equals(locationName, StringComparison.OrdinalIgnoreCase) || ClusterEndpointMatcher.MatchesMasterNode(w.MasterNodeName, locationLoginEndpoint))
                .SelectMany(c => c.NodeTypes.Select(s => new QueuePartitionProjection(s.Name, s.ClusterNodeTypeAggregation?.Name))
                ).ToHashSet(new QueuePartitionProjectionComparer());

        if (nodeTypesByCluster.Count == 0)
        {
            var availableClusters = string.Join("; ", listClustersResult.Value.Select(c => $"{c.Name} (masterNode: {c.MasterNodeName})"));
            logger.LogFailedToFindHeappeCluster(serviceConnectionInfo.Identifier, locationName, locationLoginEndpoint, availableClusters);
            return Result<HashSet<QueuePartitionProjection>, Error>.Failure(HeappeErrors.FailedToFindHeappeCluster(serviceConnectionInfo.Identifier, locationName), State.Error);
        }

        return Result<HashSet<QueuePartitionProjection>, Error>.Success(nodeTypesByCluster);
    }

    /// <summary>
    /// Synchronizes location partitions with Heappe queue partitions by marking missing partitions as deleted and
    /// creating new partitions for queues present in Heappe.
    /// </summary>
    /// <param name="heappeQueuePartitions">Set of queue partitions retrieved from Heappe to synchronize against.</param>
    /// <param name="locationId">Identifier of the location whose partitions are being synchronized.</param>
    /// <param name="heappeIdentifier">Heappe system identifier for logging and error tracking.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>Success result with true if partitions were synchronized (created or updated), false if no changes occurred;
    /// failure result with error details if database persistence fails.</returns>
    private async Task<Result<bool, Error>> SynchronizationLocationQueuesAsync(HashSet<QueuePartitionProjection> heappeQueuePartitions, Guid locationId, HeappeIdentifier heappeIdentifier, CancellationToken ct)
    {
        try
        {
            bool synchronized = false;
            var locationPartitions = await dbContext.Set<LocationPartition>()
                .TagWith($"LocationPartitionsSynchronization: Synchronizing location partitions for location and Heappe identifier.")
                .Where(w => w.LocationId == locationId)
                .Include(i => i.ProjectCommandTemplatePartitions)
                .IgnoreQueryFilters()
                .ToListAsync(ct);

            foreach (var locationPartition in locationPartitions)
            {
                bool partitionDeleted = locationPartition.IsDeleted;
                var partitionExist = heappeQueuePartitions.Any(a => a.IsMatch(locationPartition.Name, locationPartition.AggregationName));

                if (RequiresPartitionStateUpdate(partitionDeleted, partitionExist))
                {
                    locationPartition.IsDeleted = !partitionExist;

                    foreach (var templatePartition in locationPartition.ProjectCommandTemplatePartitions)
                    {
                        templatePartition.IsDeleted = !partitionExist;
                    }

                    if (!partitionDeleted)
                    {
                        using var scope = logger.BeginImmediateScope();
                        logger.LogLocationPartitionMarkedAsDeleted(locationPartition.Name, locationPartition.AggregationName ?? ApplicationConstants.NotDefined, locationId, heappeIdentifier);
                    }

                    synchronized = true;
                }
            }

            var missingPartitions = heappeQueuePartitions.Except(
                    locationPartitions.Select(s => new QueuePartitionProjection(s.Name, s.AggregationName)),
                    new QueuePartitionProjectionComparer()
                ).ToList();

            synchronized |= missingPartitions.Count > 0;
            foreach (var missingPartition in missingPartitions)
            {
                var newPartition = new LocationPartition
                {
                    Name = missingPartition.QueueName,
                    LocationId = locationId,
                    AggregationName = missingPartition.AggregationName,
                    IsDeleted = false
                };

                dbContext.Set<LocationPartition>().Add(newPartition);
                using var scope = logger.BeginImmediateScope();
                logger.LogLocationPartitionCreated(missingPartition.QueueName, missingPartition.AggregationName ?? ApplicationConstants.NotDefined, locationId, heappeIdentifier);
            }

            if (synchronized)
            {
                await dbContext.SaveChangesAsync(ct);
            }

            return Result<bool, Error>.Success(synchronized);
        }
        catch (DbUpdateException ex)
        {
            logger.LogLocationPartitionsSaveChangesFailure(locationId, heappeIdentifier, ex);
            return Result<bool, Error>.Failure(Errors.NoChangesPersistedWhileSynchronizingLocationPartitions(locationId), State.Error);
        }


        static bool RequiresPartitionStateUpdate(bool partitionDeleted, bool partitionExist)
        {
            return partitionDeleted == partitionExist;
        }
    }
}