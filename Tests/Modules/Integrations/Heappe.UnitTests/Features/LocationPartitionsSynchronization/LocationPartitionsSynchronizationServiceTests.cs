using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries.Sync;
using Heappe.Contracts.HEappeApiDto.Responses;
using Heappe.Helpers.Connection;
using Heappe.Features.LocationPartitionsSynchronization;
using Heappe.Persistence;
using Heappe.Persistence.Entities;

using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using UserOrg.Shared;

using Xunit;

using Error = Lukdrasil.Error.Error;

namespace Heappe.UnitTests.Features.LocationPartitionsSynchronization;

/// <summary>
/// Unit tests for LocationPartitionsSynchronizationService.
/// Regression coverage for the production 500 caused by a node type without a cluster
/// aggregation (null ClusterNodeTypeAggregation) and for the missing GetClusters success guard.
/// Uses an in-memory database with fully mocked HEAppE and credentials services.
/// </summary>
public sealed class LocationPartitionsSynchronizationServiceTests
{
    private static readonly HeappeIdentifier Identifier = new("heappe-test");
    private const string LocationName = "Barbora";
    private const string LoginEndpoint = "barbora.login";
    private const string QueueName = "qcpu";
    private const string AggregationName = "BARBORA_CPU";
    private static readonly Uri HeappeUri = new("https://heappe.example/");

    private readonly Mock<ILogger<LocationPartitionsSynchronizationService>> _mockLogger = new(MockBehavior.Loose);
    private readonly Mock<IHeappeService> _mockHeappeService = new(MockBehavior.Strict);
    private readonly Mock<IHeappeConnectionFactory> _mockConnectionFactory = new(MockBehavior.Strict);

    /// <summary>
    /// Regression for the production NullReferenceException: a matched cluster exposes a node type whose
    /// ClusterNodeTypeAggregation is null. The sync must succeed and persist a partition with a null
    /// AggregationName instead of throwing.
    /// </summary>
    [Fact(DisplayName = "Sync succeeds and creates a null-aggregation partition when node type has no aggregation")]
    public async Task SyncCreatesPartitionWhenNodeTypeHasNoAggregation()
    {
        // Arrange
        var locationId = Guid.NewGuid();
        var nodeType = CreateNodeType(QueueName, aggregation: null);
        SetupCredentials();
        SetupGetClusters(Result<IList<ClusterDetailedModel>, Error>.Success([CreateCluster(nodeType)], State.Ok));

        using var dbContext = CreateInMemoryDbContext();
        var service = CreateService(dbContext);

        // Act
        var result = await service.SynchronizationLocationPartitionsAsync(CreateLocation(locationId), "resource", CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var partition = Assert.Single(dbContext.Set<LocationPartition>().IgnoreQueryFilters());
        Assert.Equal(QueueName, partition.Name);
        Assert.Null(partition.AggregationName);
        Assert.Equal(locationId, partition.LocationId);
    }

    /// <summary>
    /// Regression for the missing success guard: when GetClusters fails the service must return a failure
    /// result (FailedToListHeappeClusters) instead of dereferencing a null value.
    /// </summary>
    [Fact(DisplayName = "Sync fails gracefully when GetClusters returns a failure")]
    public async Task SyncFailsWhenGetClustersFails()
    {
        // Arrange
        SetupCredentials();
        SetupGetClusters(Result<IList<ClusterDetailedModel>, Error>.Failure(
            new Lukdrasil.Error.ErrorBuilder().WithDescription("heappe unreachable").Build(), State.Error));

        using var dbContext = CreateInMemoryDbContext();
        var service = CreateService(dbContext);

        // Act
        var result = await service.SynchronizationLocationPartitionsAsync(CreateLocation(Guid.NewGuid()), "resource", CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        // Pin the specific failure (FailedToListHeappeClusters) so the test cannot pass on a different failure path.
        Assert.Contains("Failed to list clusters", result.Error.Description, StringComparison.Ordinal);
        Assert.Contains(Identifier.Value, result.Error.Description, StringComparison.Ordinal);
        Assert.Empty(dbContext.Set<LocationPartition>().IgnoreQueryFilters());
    }

    /// <summary>
    /// Control case: a node type with an aggregation keeps producing a partition whose AggregationName is
    /// populated, confirming the null-aggregation fix did not collapse the aggregation into all cases.
    /// </summary>
    [Fact(DisplayName = "Sync creates a partition carrying the aggregation name when node type has an aggregation")]
    public async Task SyncCreatesPartitionWithAggregationWhenNodeTypeHasAggregation()
    {
        // Arrange
        var locationId = Guid.NewGuid();
        var nodeType = CreateNodeType(QueueName, aggregation: AggregationName);
        SetupCredentials();
        SetupGetClusters(Result<IList<ClusterDetailedModel>, Error>.Success([CreateCluster(nodeType)], State.Ok));

        using var dbContext = CreateInMemoryDbContext();
        var service = CreateService(dbContext);

        // Act
        var result = await service.SynchronizationLocationPartitionsAsync(CreateLocation(locationId), "resource", CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var partition = Assert.Single(dbContext.Set<LocationPartition>().IgnoreQueryFilters());
        Assert.Equal(QueueName, partition.Name);
        Assert.Equal(AggregationName, partition.AggregationName);
    }

    private LocationPartitionsSynchronizationService CreateService(HeappeIntegrationDbContext dbContext)
        => new(_mockLogger.Object, _mockHeappeService.Object, _mockConnectionFactory.Object, dbContext);

    private void SetupCredentials()
        => _mockConnectionFactory
            .Setup(f => f.ForServiceAsync(Identifier, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BaseHeappeConnectionInfo, Error>.Success(
                new ServiceTokenHeappeConnectionInfo(HeappeUri, Identifier, "test-api-key"), State.Ok));

    private void SetupGetClusters(Result<IList<ClusterDetailedModel>, Error> result)
        => _mockHeappeService
            .Setup(s => s.GetClusters(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private static LocationSyncEntry CreateLocation(Guid locationId)
        => new(locationId, LocationName, LoginEndpoint,
            new HeappeInstanceSyncEntry(HeappeUri, new Version(5, 0, 0), Identifier));

    private static NodeTypeModel CreateNodeType(string name, string? aggregation)
        => new(
            Id: 1L, Name: name, Description: "",
            NumberOfNodes: 1, CoresPerNode: 8, MaxWalltime: 3600,
            FileTransferMethodId: 0, Queue: name, QualityOfService: "",
            ClusterAllocationName: "",
            ClusterNodeTypeAggregation: aggregation is null
                ? null
                : new ClusterNodeTypeAggregationModel(1L, aggregation, "", "", DateTime.MinValue, null),
            Accounting: [],
            Projects: []);

    private static ClusterDetailedModel CreateCluster(NodeTypeModel nodeType)
        => new(
            Id: 1L, Name: LocationName, Description: null,
            MasterNodeName: "master", SchedulerType: 0, TimeZone: "UTC",
            Port: null, ConnectionProtocol: 0,
            UpdateJobStateByServiceAccount: false, DomainName: null,
            ProxyConnection: null, FileTransferMethodIds: [],
            NodeTypes: [nodeType]);

    private static HeappeIntegrationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;
        return new HeappeIntegrationDbContext(options);
    }
}
