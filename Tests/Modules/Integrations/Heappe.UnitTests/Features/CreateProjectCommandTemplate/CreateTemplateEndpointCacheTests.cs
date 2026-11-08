using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries;
using Heappe.Contracts.Entries.Sync;
using Heappe.Contracts.HEappeApiDto.Responses;
using Heappe.Features.CreateProjectCommandTemplate;
using Heappe.Features.CreateProjectCommandTemplate.Request;
using Heappe.Features.Credentials;
using Heappe.Features.KillSwitch;
using Heappe.Helpers.Connection;
using Heappe.HostContracts;
using Heappe.Persistence;
using Heappe.Persistence.Entities;

using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using Xunit;

using Error = Lukdrasil.Error.Error;

namespace Heappe.UnitTests.Features.CreateProjectCommandTemplate;

/// <summary>
/// Verifies that <see cref="CreateTemplateEndpoint.HandleAsync"/> evicts the
/// <c>TemplatesInfo_{projectShortName}</c> cache tag after a successful template
/// creation, and does NOT evict it when the pipeline fails.
/// All external services are mocked; no real HTTP calls are made.
/// </summary>
public sealed class CreateTemplateEndpointCacheTests
{
    private static readonly Guid _projectId = Guid.Parse("11111111-0000-0000-0000-000000000000");
    private static readonly Guid _resourceId = Guid.Parse("22222222-0000-0000-0000-000000000000");
    private static readonly Guid _locationId = Guid.Parse("33333333-0000-0000-0000-000000000000");
    private static readonly Uri _heappeUri = new("http://heappe.test");
    private const long HeappeProjectId = 100L;
    private const string ProjectShortName = "tp";
    private const string ResourceName = "res1";
    private const string PartitionName = "queue1";
    private const string LocationName = "location1";
    private const string AggregationName = "agg1";
    private static readonly HeappeIdentifier HeappeIdentifier = new("heappe-test");

    [Fact(DisplayName = "Should evict TemplatesInfo cache when CreateTemplate succeeds")]
    public async Task Should_evict_TemplatesInfo_cache_When_CreateTemplate_succeeds()
    {
        // Arrange
        var cache = new FakeHybridCache();

        var callCount = 0;
        var tag = CacheTags.TemplatesInfo(ProjectShortName);
        await cache.GetOrCreateAsync(tag,
            _ => { callCount++; return ValueTask.FromResult("cached-data"); },
            tags: [tag]);
        Assert.Equal(1, callCount); // cache primed

        var (request, userorgService, dbContext, heappeService, loggerFactory,
            connectionFactory, createService) = BuildSuccessfulSetup();

        // Act
        await CreateTemplateEndpoint.HandleAsync(
            request, userorgService, dbContext, heappeService,
            loggerFactory, connectionFactory, createService,
            cache, new CommandTemplateWriteGuard(new KillSwitchStateService(dbContext), dbContext), CancellationToken.None);

        // Assert — cache was evicted; next read re-invokes the factory
        await cache.GetOrCreateAsync(tag,
            _ => { callCount++; return ValueTask.FromResult("refreshed-data"); },
            tags: [tag]);
        Assert.Equal(2, callCount);
    }

    [Fact(DisplayName = "Should not evict TemplatesInfo cache when CreateTemplate pipeline fails")]
    public async Task Should_not_evict_TemplatesInfo_cache_When_CreateTemplate_fails()
    {
        // Arrange
        var cache = new FakeHybridCache();

        var callCount = 0;
        var tag = CacheTags.TemplatesInfo(ProjectShortName);
        await cache.GetOrCreateAsync(tag,
            _ => { callCount++; return ValueTask.FromResult("cached-data"); },
            tags: [tag]);
        Assert.Equal(1, callCount);

        // Make the first pipeline step fail — no external service is reachable
        var userorgService = new Mock<IUserorgService>(MockBehavior.Loose);
        userorgService
            .Setup(s => s.GetProjectResourceAllocationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoEntry, Error>.Failure(
                new Lukdrasil.Error.ErrorBuilder()
                    .WithDescription("allocation not found")
                    .Build("test"),
                State.NotFound));

        var loggerFactory = CreateLoggerFactory();

        // Act
        var emptyDbContext = CreateEmptyDbContext();
        await CreateTemplateEndpoint.HandleAsync(
            CreateRequest(),
            userorgService.Object,
            emptyDbContext,
            Mock.Of<IHeappeService>(),
            loggerFactory,
            Mock.Of<IHeappeConnectionFactory>(),
            Mock.Of<IProjectCommandTemplateCreateService>(),
            cache,
            new CommandTemplateWriteGuard(new KillSwitchStateService(emptyDbContext), emptyDbContext),
            CancellationToken.None);

        // Assert — cache was NOT evicted; factory count stays at 1
        await cache.GetOrCreateAsync(tag,
            _ => { callCount++; return ValueTask.FromResult("still-cached"); },
            tags: [tag]);
        Assert.Equal(1, callCount);
    }

    private (
        CreateTemplateRequest Request,
        IUserorgService UserorgService,
        HeappeIntegrationDbContext DbContext,
        IHeappeService HeappeService,
        ILoggerFactory LoggerFactory,
        IHeappeConnectionFactory ConnectionFactory,
        IProjectCommandTemplateCreateService CreateService)
    BuildSuccessfulSetup()
    {
        var heappeProject = new ProjectModel(
            HeappeProjectId, "TestProject", "", "",
            DateTime.UtcNow, DateTime.UtcNow.AddYears(1),
            0, false, "/scripts", false, []);

        var nodeType = new NodeTypeModel(
            Id: 1L, Name: "compute", Description: "",
            NumberOfNodes: 1, CoresPerNode: 8, MaxWalltime: 3600,
            FileTransferMethodId: 0, Queue: PartitionName, QualityOfService: "",
            ClusterAllocationName: "",
            ClusterNodeTypeAggregation: new ClusterNodeTypeAggregationModel(
                1L, AggregationName, "", "", DateTime.MinValue, null),
            Accounting: [],
            Projects: [heappeProject]);

        var cluster = new ClusterDetailedModel(
            Id: 1L, Name: LocationName, Description: null,
            MasterNodeName: "master", SchedulerType: 0, TimeZone: "UTC",
            Port: null, ConnectionProtocol: 0,
            UpdateJobStateByServiceAccount: false, DomainName: null,
            ProxyConnection: null, FileTransferMethodIds: [],
            NodeTypes: [nodeType]);

        // IUserorgService
        var userorgService = new Mock<IUserorgService>(MockBehavior.Strict);
        userorgService
            .Setup(s => s.GetProjectResourceAllocationAsync(
                ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoEntry, Error>.Success(
                new ProjectResourceInfoEntry(
                    _projectId, "Project", ProjectShortName, null,
                    DateTime.UtcNow, DateTime.UtcNow.AddYears(1),
                    new ResourceInfoEntry(_resourceId, ResourceName, null, null, null)),
                State.Ok));

        userorgService
            .Setup(s => s.GetValidLocationNames(
                ProjectShortName, ResourceName,
                It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<HashSet<ProviderLocationInfo>, Error>.Success(
                [new ProviderLocationInfo(_locationId, LocationName, AggregationName, "prov1")],
                State.Ok));

        userorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(
                ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(
                new ProjectResourceInfoSyncEntry(
                    Guid.NewGuid(), "Project", ProjectShortName, null,
                    DateTime.UtcNow, DateTime.UtcNow.AddYears(1),
                    new ResourceInfoSyncEntry(
                        _resourceId, ResourceName, null, null, null,
                        [new LocationAllocationInfoSyncEntry(
                            _locationId, LocationName, "login",
                            ELocationType.HPC, AggregationName,
                            new HeappeInstanceSyncEntry(
                                _heappeUri, new Version(5, 0, 0), HeappeIdentifier))])),
                State.Ok));

        // IHeappeConnectionFactory
        var connectionFactory = new Mock<IHeappeConnectionFactory>(MockBehavior.Strict);
        connectionFactory
            .Setup(f => f.ForServiceAsync(HeappeIdentifier, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BaseHeappeConnectionInfo, Error>.Success(
                new ServiceTokenHeappeConnectionInfo(_heappeUri, HeappeIdentifier, "test-api-key"), State.Ok));

        // IHeappeService
        var heappeService = new Mock<IHeappeService>(MockBehavior.Strict);
        heappeService
            .Setup(s => s.GetProjectByResourceName(
                It.IsAny<BaseHeappeConnectionInfo>(), ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectModel, Error>.Success(heappeProject, State.Ok));

        heappeService
            .Setup(s => s.GetClusters(
                It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterDetailedModel>, Error>.Success(
                [cluster], State.Ok));

        // IProjectCommandTemplateCreateService
        var createService = new Mock<IProjectCommandTemplateCreateService>(MockBehavior.Strict);
        createService
            .Setup(s => s.CreateCommandTemplateAsync(
                It.IsAny<CommandTemplateCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Success());

        return (
            CreateRequest(),
            userorgService.Object,
            CreateDbContextWithPartition(),
            heappeService.Object,
            CreateLoggerFactory(),
            connectionFactory.Object,
            createService.Object);
    }

    private static CreateTemplateRequest CreateRequest() =>
        new(
            ProjectIdentifier: new ProjectResourceTarget(ProjectShortName, ResourceName),
            PartitionName: PartitionName,
            LocationName: LocationName,
            AggregationName: AggregationName,
            Name: "MyTemplate",
            Description: null,
            PreparationScriptPath: null,
            ExecutionScriptPath: "/scripts/run.sh",
            CommandTemplateParameters: []);

    private static HeappeIntegrationDbContext CreateDbContextWithPartition()
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase($"CreateTemplateCache_{Guid.NewGuid()}")
            .Options;
        var db = new HeappeIntegrationDbContext(options);

        db.LocationPartitions.Add(new LocationPartition
        {
            Id = Guid.NewGuid(),
            Name = PartitionName,
            AggregationName = AggregationName,
            LocationId = _locationId
        });
        db.SaveChanges();

        return db;
    }

    private static HeappeIntegrationDbContext CreateEmptyDbContext()
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase($"CreateTemplateCache_Empty_{Guid.NewGuid()}")
            .Options;
        return new HeappeIntegrationDbContext(options);
    }

    private static ILoggerFactory CreateLoggerFactory()
    {
        var factory = new Mock<ILoggerFactory>(MockBehavior.Loose);
        factory.Setup(f => f.CreateLogger(It.IsAny<string>()))
               .Returns(new Mock<ILogger>().Object);
        return factory.Object;
    }
}
