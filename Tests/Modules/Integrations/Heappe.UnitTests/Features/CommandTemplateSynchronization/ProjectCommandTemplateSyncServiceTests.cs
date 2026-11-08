using Heappe;
using Heappe.Contracts;
using Heappe.Contracts.CommandTemplate;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries.Sync;
using Heappe.Contracts.HEappeApiDto.Responses;
using Heappe.Features.CommandTemplateSynchronization;
using Heappe.Features.Connection;
using Heappe.Features.CreateProjectCommandTemplate;
using Heappe.Features.KillSwitch;
using Heappe.Features.LocationPartitionsSynchronization;
using Heappe.Helpers.Connection;
using Heappe.HostContracts;
using Heappe.Persistence;
using Heappe.Persistence.Entities;
using Heappe.Persistence.Enums;

using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using Xunit;

using Error = Lukdrasil.Error.Error;

namespace Heappe.UnitTests.Features.CommandTemplateSynchronization;

/// <summary>
/// Unit tests for ProjectCommandTemplateSyncService.
/// Tests verify sync flow and error handling using fully mocked dependencies.
/// All tests are executable locally without external dependencies.
/// </summary>
public sealed class ProjectCommandTemplateSyncServiceTests
{
    private readonly Mock<ILogger<ProjectCommandTemplateSyncService>> _mockLogger = new(MockBehavior.Loose);
    private readonly Mock<IUserorgService> _mockUserorgService = new(MockBehavior.Strict);
    private readonly Mock<IHeappeService> _mockHeappeService = new(MockBehavior.Strict);
    private readonly Mock<IHeappeConnectionFactory> _mockConnectionFactory = new(MockBehavior.Strict);
    private readonly Mock<IProjectCommandTemplateCreateService> _mockCreateService = new(MockBehavior.Strict);
    private readonly Mock<ILocationPartitionsSynchronizationService> _mockLocationPartitionsSynchronizationService = new(MockBehavior.Strict);
    private readonly Mock<IKillSwitchStateService> _mockKillSwitchStateService = new(MockBehavior.Strict);

    public ProjectCommandTemplateSyncServiceTests()
    {
        // Default: no location is frozen — preserves today's behavior for every
        // characterization test that does not care about the kill-switch.
        _mockKillSwitchStateService
            .Setup(s => s.GetEnforcedKilledLocationIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlySet<Guid>)new HashSet<Guid>());

        // The service's log calls are source-generated ([LoggerMessage]), which guard on
        // IsEnabled before formatting — a Loose mock otherwise returns false, silently
        // dropping every log call a test needs to observe.
        _mockLogger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
    }

    /// <summary>
    /// Starts capturing log messages formatted at the given level, and returns an accessor for the
    /// last captured one. Source-generated <c>[LoggerMessage]</c> calls pass a pooled <c>TState</c>
    /// that is cleared once the underlying <c>ILogger.Log&lt;TState&gt;</c> call returns, so the
    /// formatted text must be read via the invocation's own formatter delegate inside a callback
    /// (at invocation time), not inspected afterwards from <c>Invocations</c>.
    /// </summary>
    private Func<string?> SetupLogCapture(LogLevel level)
    {
        string? captured = null;
        _mockLogger
            .Setup(x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()))
            .Callback(new InvocationAction(invocation =>
            {
                var formatter = invocation.Arguments[4];
                var invoke = formatter.GetType().GetMethod("Invoke")!;
                captured = (string?)invoke.Invoke(formatter, [invocation.Arguments[2], invocation.Arguments[3]]);
            }));
        return () => captured;
    }

    /// <summary>
    /// Verifies BuildCompletePath combines base directory with command when command has no path separators.
    /// This is the common case for simple filenames like "jobscript.sh".
    /// </summary>
    [Theory(DisplayName = "BuildCompletePath combines base and command when command has no path separators")]
    [InlineData("jobscript.sh", "/usr/scripts", "/usr/scripts/jobscript.sh")]
    [InlineData("test.sh", "/home/user/bin", "/home/user/bin/test.sh")]
    [InlineData("setup.sh", "C:\\scripts", "C:\\scripts/setup.sh")]
    public void BuildCompletePathCombinesBaseAndCommand(string command, string baseDirectory, string expected)
    {
        // Act
        var result = ProjectCommandTemplateSyncService.BuildCompletePath(command, baseDirectory);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies BuildCompletePath returns command as-is when it already contains forward slash separators.
    /// This indicates the command is already a complete path.
    /// </summary>
    [Theory(DisplayName = "BuildCompletePath returns command as-is when it contains forward slashes")]
    [InlineData("/usr/scripts/jobscript.sh")]
    [InlineData("./scripts/test.sh")]
    [InlineData("../scripts/setup.sh")]
    public void BuildCompletePathReturnsCommandAsIsWithForwardSlashes(string command)
    {
        // Act
        var result = ProjectCommandTemplateSyncService.BuildCompletePath(command, "/usr/scripts");

        // Assert
        Assert.Equal(command, result);
    }

    /// <summary>
    /// Verifies BuildCompletePath returns command as-is when it already contains backslash separators (Windows paths).
    /// This indicates the command is already a complete path.
    /// </summary>
    [Theory(DisplayName = "BuildCompletePath returns command as-is when it contains backslashes")]
    [InlineData("C:\\Scripts\\jobscript.bat")]
    [InlineData(".\\scripts\\test.bat")]
    [InlineData("..\\scripts\\setup.cmd")]
    public void BuildCompletePathReturnsCommandAsIsWithBackslashes(string command)
    {
        // Act
        var result = ProjectCommandTemplateSyncService.BuildCompletePath(command, "C:\\scripts");

        // Assert
        Assert.Equal(command, result);
    }

    /// <summary>
    /// Verifies BuildCompletePath returns command as-is when base directory is empty or null.
    /// Without base directory, command cannot be completed.
    /// </summary>
    [Theory(DisplayName = "BuildCompletePath returns command as-is when base directory is null or empty")]
    [InlineData("jobscript.sh", null)]
    [InlineData("jobscript.sh", "")]
    public void BuildCompletePathReturnsCommandWhenBaseDirectoryEmpty(string command, string baseDirectory)
    {
        // Act
        var result = ProjectCommandTemplateSyncService.BuildCompletePath(command, baseDirectory);

        // Assert
        Assert.Equal(command, result);
    }

    /// <summary>
    /// Verifies BuildCompletePath returns null or empty when command is null or empty.
    /// Cannot construct path from empty command.
    /// </summary>
    [Theory(DisplayName = "BuildCompletePath returns null/empty when command is null or empty")]
    [InlineData(null, "/usr/scripts")]
    [InlineData("", "/usr/scripts")]
    public void BuildCompletePathReturnsEmptyWhenCommandEmpty(string command, string baseDirectory)
    {
        // Act
        var result = ProjectCommandTemplateSyncService.BuildCompletePath(command, baseDirectory);

        // Assert
        Assert.Equal(command, result);
    }

    /// <summary>
    /// Verifies BuildCompletePath correctly trims trailing slashes from base directory.
    /// This prevents double slashes in the final path (e.g., "/usr/scripts//jobscript.sh").
    /// </summary>
    [Theory(DisplayName = "BuildCompletePath trims trailing slashes from base directory")]
    [InlineData("jobscript.sh", "/usr/scripts/", "/usr/scripts/jobscript.sh")]
    [InlineData("jobscript.sh", "/usr/scripts///", "/usr/scripts/jobscript.sh")]
    [InlineData("jobscript.sh", "C:\\scripts\\", "C:\\scripts/jobscript.sh")]
    [InlineData("jobscript.sh", "C:\\scripts\\\\", "C:\\scripts/jobscript.sh")]
    public void BuildCompletePathTrimsTrailingSlashes(string command, string baseDirectory, string expected)
    {
        // Act
        var result = ProjectCommandTemplateSyncService.BuildCompletePath(command, baseDirectory);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that sync returns failure when project allocation cannot be retrieved.
    /// This tests the service's error handling at the first step of the sync flow.
    /// </summary>
    [Fact(DisplayName = "Returns Failure when project allocation cannot be retrieved")]
    public async Task SyncCommandTemplatesForResourceReturnsFailureWhenAllocationFails()
    {
        // Arrange
        var error = new Error("ERR001", Lukdrasil.Error.ErrorSeverity.Error, "Allocation not found");
        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Failure(error));

        using var dbContext = CreateInMemoryDbContext();
        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            new FakeHybridCache(),
            _mockKillSwitchStateService.Object,
            _nexusSettings);

        // Act
        var result = await service.SyncCommandTemplatesForResource(
            "test-project", "test-resource", CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    /// <summary>
    /// Verifies that sync returns failure when no Heappe locations exist.
    /// This tests the service's validation logic for HPC resource availability.
    /// </summary>
    [Fact(DisplayName = "Returns Failure when no Heappe locations exist")]
    public async Task SyncCommandTemplatesForResourceReturnsFailureWhenNoHeappeLocations()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        // Resource has no locations with a HeappeInstance → locationGroups will be empty
        var resource = new ResourceInfoSyncEntry(
            Id: resourceId,
            Name: "test-resource",
            Description: null,
            StartDate: DateTime.UtcNow,
            EndDate: DateTime.UtcNow.AddYears(1),
            Locations: []);

        var projectAllocation = new ProjectResourceInfoSyncEntry(
            Id: projectId,
            Name: "test-project-resource",
            ShortName: "tpr",
            Description: null,
            StartDate: DateTime.UtcNow,
            EndDate: DateTime.UtcNow.AddYears(1),
            Resource: resource);

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocation));

        using var dbContext = CreateInMemoryDbContext();
        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            new FakeHybridCache(),
            _mockKillSwitchStateService.Object,
            _nexusSettings);

        // Act
        var result = await service.SyncCommandTemplatesForResource(
            "test-project", "test-resource", CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);

        // The failure must CARRY its cause, not merely be a failure. Assert.NotNull passes for an
        // EMPTY ValidationError, which is exactly how the empty-error defect stayed hidden: callers
        // (WorkItemProcessor) logged "Background sync FAILED for {Key}: []" on every retry with no
        // cause to act on. Assert on content, not just on failure.
        Assert.NotEmpty(result.Error);
        Assert.Contains(result.Error, e => e.Description.Contains("test-resource", StringComparison.Ordinal));
    }

    // ====== Characterization Tests (Phase 0 baseline — today's ValidationResult<bool> contract) ======
    //
    // These tests lock in TODAY'S behavior of SyncCommandTemplatesForResource before the
    // partial-result refactor (see .ai/plan-ready.md Phase 0, items 1-9). They must keep
    // passing unmodified except where explicitly called out (item 4's bug characterization
    // is expected to flip to a different assertion once the bug is fixed in a later block).

    private static readonly Uri _instanceAUri = new("http://heappe-a.test");
    private static readonly Uri _instanceBUri = new("http://heappe-b.test");
    private static readonly HeappeIdentifier HeappeIdentifierA = new("heappe-a");
    private static readonly HeappeIdentifier HeappeIdentifierB = new("heappe-b");

    private static readonly IOptions<TokenExchangeOptions> _nexusSettings =
        Options.Create(new TokenExchangeOptions { Nexus = { BaseUrl = new Uri("http://nexus.test/") } });

    private static Result<BaseHeappeConnectionInfo, Error> ServiceConnection(HeappeIdentifier identifier, string key) =>
        Result<BaseHeappeConnectionInfo, Error>.Success(
            new ServiceTokenHeappeConnectionInfo(new Uri($"http://nexus.test/api/heappe/{identifier}/"), identifier, key));
    private const string ProjectShortName = "proj";
    private const string ResourceName = "res";

    /// <summary>
    /// Item 1: the outer loop over HEAppE instances continues past one failed instance.
    /// Instance A fails at GetClusters; instance B succeeds fully, including reaching
    /// createService.CreateCommandTemplateAsync for its location. Overall result today
    /// is Failure containing instance A's error, but B's location was still processed.
    /// </summary>
    [Fact(DisplayName = "Outer loop continues past one failed HEAppE instance and still processes the healthy one")]
    public async Task SyncCommandTemplatesForResource_ContinuesPastFailedInstance_AndProcessesHealthyInstance()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var locationAId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();

        var instanceA = new HeappeInstanceSyncEntry(_instanceAUri, new Version(5, 0, 0), HeappeIdentifierA);
        var instanceB = new HeappeInstanceSyncEntry(_instanceBUri, new Version(5, 0, 0), HeappeIdentifierB);

        var projectAllocation = BuildProjectAllocation(projectId, resourceId,
        [
            new LocationAllocationInfoSyncEntry(locationAId, "cluster-a", "login-a", ELocationType.HPC, "AGG-A", instanceA),
            new LocationAllocationInfoSyncEntry(locationBId, "cluster-b", "login-b", ELocationType.HPC, "AGG-B", instanceB)
        ]);

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocation));

        _mockLocationPartitionsSynchronizationService
            .Setup(s => s.SynchronizationLocationPartitionsAsync(It.IsAny<LocationSyncEntry>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));

        _mockConnectionFactory
            .Setup(f => f.ForServiceAsync(HeappeIdentifierA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceConnection(HeappeIdentifierA, "key-a"));
        _mockConnectionFactory
            .Setup(f => f.ForServiceAsync(HeappeIdentifierB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceConnection(HeappeIdentifierB, "key-b"));

        var heappeProjectA = BuildHeappeProject(1L);
        var heappeProjectB = BuildHeappeProject(2L);

        _mockHeappeService
            .Setup(s => s.GetProjectByResourceName(It.IsAny<BaseHeappeConnectionInfo>(), "test-resource", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectModel, Error>.Success(heappeProjectA));
        // Both instances share the resource name lookup — differentiate by connection base address
        _mockHeappeService
            .Setup(s => s.GetProjectByResourceName(
                It.Is<BaseHeappeConnectionInfo>(c => c.Identifier == HeappeIdentifierA), "test-resource", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectModel, Error>.Success(heappeProjectA));
        _mockHeappeService
            .Setup(s => s.GetProjectByResourceName(
                It.Is<BaseHeappeConnectionInfo>(c => c.Identifier == HeappeIdentifierB), "test-resource", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectModel, Error>.Success(heappeProjectB));

        _mockHeappeService
            .Setup(s => s.GetProjectCommandTemplatesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<CommandTemplateModel>, Error>.Success([]));

        // Instance A fails at GetClusters
        _mockHeappeService
            .Setup(s => s.GetClusters(
                It.Is<BaseHeappeConnectionInfo>(c => c.Identifier == HeappeIdentifierA), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterDetailedModel>, Error>.Failure(new Error("A001", Lukdrasil.Error.ErrorSeverity.Error, "clusters unreachable")));

        var nodeTypeB = BuildNodeType(1L, "cluster-b", "AGG-B");
        var clusterB = BuildCluster("cluster-b", [nodeTypeB]);
        _mockHeappeService
            .Setup(s => s.GetClusters(
                It.Is<BaseHeappeConnectionInfo>(c => c.Identifier == HeappeIdentifierB), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterDetailedModel>, Error>.Success([clusterB]));

        _mockCreateService
            .Setup(s => s.CreateCommandTemplateAsync(It.IsAny<CommandTemplateCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Success());

        using var dbContext = CreateInMemoryDbContext();
        SeedCommandTemplateLocationDefinition(dbContext, locationBId, ELocationType.HPC);

        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            new FakeHybridCache(),
            _mockKillSwitchStateService.Object,
            _nexusSettings);

        var result = await service.SyncCommandTemplatesForResource(ProjectShortName, ResourceName, CancellationToken.None);

        // New contract (B03): instance B succeeded, so the overall result is Success carrying a
        // per-location breakdown — one entry per instance's location. Instance A's location is
        // IsSuccess=false with its cluster-listing error; instance B's location is IsSuccess=true.
        Assert.True(result.IsSuccess);
        var syncResult = result.Value;
        Assert.True(syncResult.AnySucceeded);
        Assert.False(syncResult.AllSucceeded);

        var locationA = Assert.Single(syncResult.Locations, l => l.LocationName == "cluster-a");
        Assert.False(locationA.IsSuccess);
        Assert.Contains(locationA.Errors, e => e.Description.Contains("Failed to list clusters", StringComparison.OrdinalIgnoreCase));

        var locationB = Assert.Single(syncResult.Locations, l => l.LocationName == "cluster-b");
        Assert.True(locationB.IsSuccess);
        Assert.Empty(locationB.Errors);

        _mockCreateService.Verify(
            s => s.CreateCommandTemplateAsync(
                It.Is<CommandTemplateCreationRequest>(r => r.LocationId == locationBId),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// Item 2a: an active allocation passes through ProcessAllocationExpiration without
    /// touching the database and without short-circuiting the sync.
    /// </summary>
    [Fact(DisplayName = "ProcessAllocationExpiration passes through for an active allocation")]
    public async Task SyncCommandTemplatesForResource_ActiveAllocation_PassesThroughExpirationCheck()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        // Active allocation, but with no HEAppE locations — so we reach the
        // "no locations" failure, proving expiration didn't short-circuit with its own error.
        var projectAllocation = new ProjectResourceInfoSyncEntry(
            Id: projectId,
            Name: "test-project-resource",
            ShortName: "tpr",
            Description: null,
            StartDate: DateTime.UtcNow.AddDays(-1),
            EndDate: DateTime.UtcNow.AddYears(1),
            Resource: new ResourceInfoSyncEntry(
                Id: resourceId,
                Name: "test-resource",
                Description: null,
                StartDate: DateTime.UtcNow.AddDays(-1),
                EndDate: DateTime.UtcNow.AddYears(1),
                Locations: []));

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocation));

        using var dbContext = CreateInMemoryDbContext();
        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            new FakeHybridCache(),
            _mockKillSwitchStateService.Object,
            _nexusSettings);

        var result = await service.SyncCommandTemplatesForResource("test-project", "test-resource", CancellationToken.None);

        // Reaches the "no HEAppE locations" failure — NOT the expiration failure —
        // proving the active allocation passed through ProcessAllocationExpiration.
        // Distinguished two ways: by State (ProcessAllocationExpiration's failure always carries
        // State.Error, while "no locations" carries the allocation lookup's own Ok state) and by
        // error content, which is the more direct signal.
        Assert.False(result.IsSuccess);
        Assert.NotEqual(Lukdrasil.Result.State.Error, result.State);
        Assert.Contains(result.Error, e => e.Description.Contains("test-resource", StringComparison.Ordinal));
    }

    /// <summary>
    /// Item 2b: an expired allocation with existing ProjectCommandTemplate rows sets their
    /// partitions to Inactive via ExecuteUpdateAsync and returns Failure(ProjectOrResourceExpiredWarning).
    /// </summary>
    [Fact(DisplayName = "Expired allocation with existing templates deactivates partitions and returns expiration failure")]
    public async Task SyncCommandTemplatesForResource_ExpiredAllocation_WithExistingTemplates_DeactivatesAndFails()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        var projectAllocation = new ProjectResourceInfoSyncEntry(
            Id: projectId,
            Name: "test-project-resource",
            ShortName: "tpr",
            Description: null,
            StartDate: DateTime.UtcNow.AddYears(-2),
            EndDate: DateTime.UtcNow.AddDays(-1), // expired
            Resource: new ResourceInfoSyncEntry(
                Id: resourceId,
                Name: "test-resource",
                Description: null,
                StartDate: DateTime.UtcNow.AddYears(-2),
                EndDate: DateTime.UtcNow.AddYears(1),
                Locations: []));

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocation));

        // ProcessAllocationExpiration uses ExecuteUpdateAsync, which the EF Core InMemory
        // provider does not support ("not supported by the current database provider") —
        // use a real (SQLite, in-memory-mode) relational provider for this scenario only.
        using var connection = CreateOpenSqliteConnection();
        using var dbContext = CreateSqliteDbContext(connection);

        // CreatedAt must be set explicitly (not left at default(DateTime)) — EF Core treats an
        // unset (CLR-default) value on a ValueGeneratedOnAdd property as "use the store default",
        // which is a SQL Server-only function (SYSUTCDATETIME()) unsupported on SQLite.
        var now = DateTime.UtcNow;

        var locationPartition = new LocationPartition
        {
            Id = Guid.NewGuid(),
            Name = "queue1",
            AggregationName = "AGG",
            LocationId = Guid.NewGuid(),
            CreatedAt = now
        };
        dbContext.Set<LocationPartition>().Add(locationPartition);

        var projectCommandTemplate = ProjectCommandTemplate.Create("tmpl1", ECommandTemplateType.Unknown, projectId, resourceId);
        dbContext.Set<ProjectCommandTemplate>().Add(projectCommandTemplate);

        var partition = new ProjectCommandTemplatePartition
        {
            Id = Guid.NewGuid(),
            State = ECommandTemplateState.Active,
            LocationPartitionId = locationPartition.Id,
            LocationPartition = locationPartition,
            ProjectCommandTemplate = projectCommandTemplate,
            CreatedAt = now
        };
        dbContext.Set<ProjectCommandTemplatePartition>().Add(partition);
        dbContext.SaveChanges();

        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            new FakeHybridCache(),
            _mockKillSwitchStateService.Object,
            _nexusSettings);

        var result = await service.SyncCommandTemplatesForResource("test-project", "test-resource", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Error, e => e.Description.Contains("expired", StringComparison.OrdinalIgnoreCase));

        var updatedPartition = await dbContext.Set<ProjectCommandTemplatePartition>().AsNoTracking().SingleAsync(p => p.Id == partition.Id);
        Assert.Equal(ECommandTemplateState.Inactive, updatedPartition.State);
    }

    /// <summary>
    /// Item 2c: an expired allocation whose ExecuteUpdateAsync reports zero changed rows
    /// (e.g. partitions vanished concurrently) returns Failure(FailedToUpdateCommandTemplateState)
    /// instead of the generic expiration warning.
    /// </summary>
    [Fact(DisplayName = "Expired allocation where the state update reports zero changes returns the update-failure error")]
    public async Task SyncCommandTemplatesForResource_ExpiredAllocation_UpdateReportsZeroChanges_ReturnsUpdateFailure()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        var projectAllocation = new ProjectResourceInfoSyncEntry(
            Id: projectId,
            Name: "test-project-resource",
            ShortName: "tpr",
            Description: null,
            StartDate: DateTime.UtcNow.AddYears(-2),
            EndDate: DateTime.UtcNow.AddDays(-1), // expired
            Resource: new ResourceInfoSyncEntry(
                Id: resourceId,
                Name: "test-resource",
                Description: null,
                StartDate: DateTime.UtcNow.AddYears(-2),
                EndDate: DateTime.UtcNow.AddYears(1),
                Locations: []));

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocation));

        // See comment in the sibling test above: ExecuteUpdateAsync requires a relational provider.
        using var connection = CreateOpenSqliteConnection();
        using var dbContext = CreateSqliteDbContext(connection);

        // A ProjectCommandTemplate row exists for this project/resource, but it has NO
        // ProjectCommandTemplatePartitions rows — so the ExecuteUpdateAsync affects 0 rows.
        var projectCommandTemplate = ProjectCommandTemplate.Create("tmpl1", ECommandTemplateType.Unknown, projectId, resourceId);
        dbContext.Set<ProjectCommandTemplate>().Add(projectCommandTemplate);
        dbContext.SaveChanges();

        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            new FakeHybridCache(),
            _mockKillSwitchStateService.Object,
            _nexusSettings);

        var result = await service.SyncCommandTemplatesForResource("test-project", "test-resource", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Error, e => e.Description.Contains("Failed to update command template state", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Item 3: BuildLocationGroups groups multiple locations under one instance together,
    /// splits locations across two distinct instances into separate groups, and normalizes
    /// aggregation names (uppercase + null-to-empty-string). Exercised indirectly through
    /// the public sync method by observing which locations get attempted against which
    /// instance's mocked HEAppE calls.
    /// </summary>
    [Fact(DisplayName = "BuildLocationGroups groups multiple locations per instance and splits across instances")]
    public async Task SyncCommandTemplatesForResource_GroupsMultipleLocationsPerInstance_AndSplitsAcrossInstances()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var locationA1 = Guid.NewGuid();
        var locationA2 = Guid.NewGuid();
        var locationBId = Guid.NewGuid();

        var instanceA = new HeappeInstanceSyncEntry(_instanceAUri, new Version(5, 0, 0), HeappeIdentifierA);
        var instanceB = new HeappeInstanceSyncEntry(_instanceBUri, new Version(5, 0, 0), HeappeIdentifierB);

        // Instance A gets two locations (one with a null aggregation name, one lowercase),
        // instance B gets one location — proving the grouping-by-instance split.
        var projectAllocation = BuildProjectAllocation(projectId, resourceId,
        [
            new LocationAllocationInfoSyncEntry(locationA1, "cluster-a1", "login-a1", ELocationType.HPC, null, instanceA),
            new LocationAllocationInfoSyncEntry(locationA2, "cluster-a2", "login-a2", ELocationType.HPC, "agg-lower", instanceA),
            new LocationAllocationInfoSyncEntry(locationBId, "cluster-b", "login-b", ELocationType.HPC, "AGG-B", instanceB)
        ]);

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocation));

        _mockLocationPartitionsSynchronizationService
            .Setup(s => s.SynchronizationLocationPartitionsAsync(It.IsAny<LocationSyncEntry>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));

        _mockConnectionFactory
            .Setup(f => f.ForServiceAsync(It.IsAny<HeappeIdentifier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceConnection(HeappeIdentifierA, "key"));

        _mockHeappeService
            .Setup(s => s.GetProjectByResourceName(It.IsAny<BaseHeappeConnectionInfo>(), "test-resource", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectModel, Error>.Success(BuildHeappeProject(1L)));

        _mockHeappeService
            .Setup(s => s.GetProjectCommandTemplatesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<CommandTemplateModel>, Error>.Success([]));

        // Clusters known to HEAppE do not include any of the three location names, so every
        // location fails at the "missing cluster" step — the point of this test is only to
        // observe that BOTH cluster-lookup calls for instance A's TWO locations happen, and
        // ONE call for instance B's ONE location, proving the per-instance grouping.
        _mockHeappeService
            .Setup(s => s.GetClusters(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterDetailedModel>, Error>.Success([]));

        using var dbContext = CreateInMemoryDbContext();
        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            new FakeHybridCache(),
            _mockKillSwitchStateService.Object,
            _nexusSettings);

        var result = await service.SyncCommandTemplatesForResource(ProjectShortName, ResourceName, CancellationToken.None);

        Assert.False(result.IsSuccess);
        // All three locations report "cluster missing" — one error per location.
        Assert.Equal(3, result.Error.Count());
        Assert.Contains(result.Error, e => e.Description.Contains("cluster-a1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Error, e => e.Description.Contains("cluster-a2", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Error, e => e.Description.Contains("cluster-b", StringComparison.OrdinalIgnoreCase));

        // GetClusters is called once per instance (not once per location) — instance A has
        // 2 locations sharing 1 call, instance B has 1 location with its own call: 2 total.
        _mockHeappeService.Verify(
            s => s.GetClusters(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    /// <summary>
    /// Item 4 (B03 flip): the pre-existing bug in ProcessLocationPartitionSynchronizationAsync
    /// is now FIXED. That method previously received `ValidationError errors` BY VALUE and
    /// reassigned its own local copy, so a partition-sync failure was silently dropped. B03
    /// changes it to RETURN the accumulated ValidationError (reassigned at the call site) and
    /// threads the failure through AddLocationError. This test — which formerly characterized
    /// the lost-error bug — now asserts the partition failure IS present in the result and is
    /// attributed to the failing location.
    /// </summary>
    [Fact(DisplayName = "Partition sync failure for one location now flows back into the result (bug fixed)")]
    public async Task SyncCommandTemplatesForResource_PartitionSyncFailure_IsReportedForFailingLocation()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var failingLocationId = Guid.NewGuid();
        var succeedingLocationId = Guid.NewGuid();

        var instanceA = new HeappeInstanceSyncEntry(_instanceAUri, new Version(5, 0, 0), HeappeIdentifierA);

        var projectAllocation = BuildProjectAllocation(projectId, resourceId,
        [
            new LocationAllocationInfoSyncEntry(failingLocationId, "cluster-fail", "login-fail", ELocationType.HPC, "AGG", instanceA),
            new LocationAllocationInfoSyncEntry(succeedingLocationId, "cluster-ok", "login-ok", ELocationType.HPC, "AGG", instanceA)
        ]);

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocation));

        // One location's partition sync fails, the sibling succeeds.
        _mockLocationPartitionsSynchronizationService
            .Setup(s => s.SynchronizationLocationPartitionsAsync(
                It.Is<LocationSyncEntry>(l => l.Id == failingLocationId), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Failure(new Error("P001", Lukdrasil.Error.ErrorSeverity.Error, "partition sync failed")));
        _mockLocationPartitionsSynchronizationService
            .Setup(s => s.SynchronizationLocationPartitionsAsync(
                It.Is<LocationSyncEntry>(l => l.Id == succeedingLocationId), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));

        _mockConnectionFactory
            .Setup(f => f.ForServiceAsync(HeappeIdentifierA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceConnection(HeappeIdentifierA, "key-a"));

        _mockHeappeService
            .Setup(s => s.GetProjectByResourceName(It.IsAny<BaseHeappeConnectionInfo>(), "test-resource", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectModel, Error>.Success(BuildHeappeProject(1L)));
        _mockHeappeService
            .Setup(s => s.GetProjectCommandTemplatesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<CommandTemplateModel>, Error>.Success([]));
        // No clusters known -> both locations end with "missing cluster" errors that DO
        // flow through the normal `errors = errors.Add(...)` accumulator (only the
        // partition-sync error is lost — that's the bug under test).
        _mockHeappeService
            .Setup(s => s.GetClusters(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterDetailedModel>, Error>.Success([]));

        using var dbContext = CreateInMemoryDbContext();
        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            new FakeHybridCache(),
            _mockKillSwitchStateService.Object,
            _nexusSettings);

        var result = await service.SyncCommandTemplatesForResource(ProjectShortName, ResourceName, CancellationToken.None);

        // Both locations end up failed (no cluster known for either), so !AnySucceeded → top-level Failure.
        Assert.False(result.IsSuccess);
        // The partition-sync failure text IS now present in the flat ValidationError (the bug is fixed) —
        // previously it was silently dropped by the by-value `ValidationError errors` parameter.
        Assert.Contains(result.Error, e => e.Description.Contains("partition sync failed", StringComparison.OrdinalIgnoreCase));

        // The failing location was SKIPPED for template sync (partition failure) — CreateCommandTemplateAsync
        // was never attempted for it, proving both the skip rule and that the error threaded back.
        _mockCreateService.Verify(
            s => s.CreateCommandTemplateAsync(
                It.Is<CommandTemplateCreationRequest>(r => r.LocationId == failingLocationId),
                It.IsAny<CancellationToken>()),
            Times.Never);

        // The sibling location's partition sync was still attempted (continuation proven).
        _mockLocationPartitionsSynchronizationService.Verify(
            s => s.SynchronizationLocationPartitionsAsync(
                It.Is<LocationSyncEntry>(l => l.Id == succeedingLocationId), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Item 5a: a missing cluster (nodeTypesAggregationByCluster lookup miss) for one
    /// location does not stop processing of a sibling location under the same instance.
    /// </summary>
    [Fact(DisplayName = "Missing cluster for one location does not stop a sibling location from succeeding")]
    public async Task SyncCommandTemplatesForResource_MissingCluster_OnOneLocation_SiblingLocationStillSucceeds()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var missingClusterLocationId = Guid.NewGuid();
        var okLocationId = Guid.NewGuid();

        var instanceA = new HeappeInstanceSyncEntry(_instanceAUri, new Version(5, 0, 0), HeappeIdentifierA);

        var projectAllocation = BuildProjectAllocation(projectId, resourceId,
        [
            new LocationAllocationInfoSyncEntry(missingClusterLocationId, "cluster-missing", "login-1", ELocationType.HPC, "AGG", instanceA),
            new LocationAllocationInfoSyncEntry(okLocationId, "cluster-ok", "login-2", ELocationType.HPC, "AGG", instanceA)
        ]);

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocation));

        _mockLocationPartitionsSynchronizationService
            .Setup(s => s.SynchronizationLocationPartitionsAsync(It.IsAny<LocationSyncEntry>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));

        _mockConnectionFactory
            .Setup(f => f.ForServiceAsync(HeappeIdentifierA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceConnection(HeappeIdentifierA, "key-a"));

        _mockHeappeService
            .Setup(s => s.GetProjectByResourceName(It.IsAny<BaseHeappeConnectionInfo>(), "test-resource", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectModel, Error>.Success(BuildHeappeProject(1L)));
        _mockHeappeService
            .Setup(s => s.GetProjectCommandTemplatesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<CommandTemplateModel>, Error>.Success([]));

        // Only "cluster-ok" is known to HEAppE — "cluster-missing" isn't, so its location
        // fails at the nodeTypesAggregationByCluster.TryGetValue miss.
        var nodeTypeOk = BuildNodeType(1L, "cluster-ok", "AGG");
        var clusterOk = BuildCluster("cluster-ok", [nodeTypeOk]);
        _mockHeappeService
            .Setup(s => s.GetClusters(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterDetailedModel>, Error>.Success([clusterOk]));

        _mockCreateService
            .Setup(s => s.CreateCommandTemplateAsync(It.IsAny<CommandTemplateCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Success());

        using var dbContext = CreateInMemoryDbContext();
        SeedCommandTemplateLocationDefinition(dbContext, okLocationId, ELocationType.HPC);

        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            new FakeHybridCache(),
            _mockKillSwitchStateService.Object,
            _nexusSettings);

        var result = await service.SyncCommandTemplatesForResource(ProjectShortName, ResourceName, CancellationToken.None);

        // New contract (B03): the sibling succeeded, so overall is Success with a partial breakdown.
        // The accumulation + continuation this item characterizes is preserved: the missing-cluster
        // error is attributed to its location, and the sibling's template creation still ran.
        Assert.True(result.IsSuccess);
        var missingEntry = Assert.Single(result.Value.Locations, l => l.LocationName == "cluster-missing");
        Assert.False(missingEntry.IsSuccess);
        Assert.Contains(missingEntry.Errors, e => e.Description.Contains("cluster-missing", StringComparison.OrdinalIgnoreCase));
        Assert.True(Assert.Single(result.Value.Locations, l => l.LocationName == "cluster-ok").IsSuccess);

        // Sibling location's template creation was still attempted despite the other location's failure.
        _mockCreateService.Verify(
            s => s.CreateCommandTemplateAsync(
                It.Is<CommandTemplateCreationRequest>(r => r.LocationId == okLocationId),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// Item 5b: a missing aggregation (nodeTypesByAggregation lookup miss) for one location
    /// does not stop processing of a sibling location under the same instance.
    /// </summary>
    [Fact(DisplayName = "Missing aggregation for one location does not stop a sibling location from succeeding")]
    public async Task SyncCommandTemplatesForResource_MissingAggregation_OnOneLocation_SiblingLocationStillSucceeds()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var missingAggLocationId = Guid.NewGuid();
        var okLocationId = Guid.NewGuid();

        var instanceA = new HeappeInstanceSyncEntry(_instanceAUri, new Version(5, 0, 0), HeappeIdentifierA);

        var projectAllocation = BuildProjectAllocation(projectId, resourceId,
        [
            new LocationAllocationInfoSyncEntry(missingAggLocationId, "cluster-shared", "login-1", ELocationType.HPC, "AGG-MISSING", instanceA),
            new LocationAllocationInfoSyncEntry(okLocationId, "cluster-shared", "login-2", ELocationType.HPC, "AGG-OK", instanceA)
        ]);

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocation));

        _mockLocationPartitionsSynchronizationService
            .Setup(s => s.SynchronizationLocationPartitionsAsync(It.IsAny<LocationSyncEntry>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));

        _mockConnectionFactory
            .Setup(f => f.ForServiceAsync(HeappeIdentifierA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceConnection(HeappeIdentifierA, "key-a"));

        _mockHeappeService
            .Setup(s => s.GetProjectByResourceName(It.IsAny<BaseHeappeConnectionInfo>(), "test-resource", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectModel, Error>.Success(BuildHeappeProject(1L)));
        _mockHeappeService
            .Setup(s => s.GetProjectCommandTemplatesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<CommandTemplateModel>, Error>.Success([]));

        // The cluster "cluster-shared" exists, but its only node type is aggregated under
        // "AGG-OK" — so the "AGG-MISSING" location fails at nodeTypesByAggregation.TryGetValue.
        var nodeTypeOk = BuildNodeType(1L, "cluster-shared", "AGG-OK");
        var clusterShared = BuildCluster("cluster-shared", [nodeTypeOk]);
        _mockHeappeService
            .Setup(s => s.GetClusters(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterDetailedModel>, Error>.Success([clusterShared]));

        _mockCreateService
            .Setup(s => s.CreateCommandTemplateAsync(It.IsAny<CommandTemplateCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Success());

        using var dbContext = CreateInMemoryDbContext();
        SeedCommandTemplateLocationDefinition(dbContext, okLocationId, ELocationType.HPC);

        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            new FakeHybridCache(),
            _mockKillSwitchStateService.Object,
            _nexusSettings);

        var result = await service.SyncCommandTemplatesForResource(ProjectShortName, ResourceName, CancellationToken.None);

        // New contract (B03): the sibling succeeded, so overall is Success with a partial breakdown.
        Assert.True(result.IsSuccess);
        var missingEntry = Assert.Single(result.Value.Locations, l => l.LocationName == "cluster-shared" && l.AggregationName == "AGG-MISSING");
        Assert.False(missingEntry.IsSuccess);
        Assert.Contains(missingEntry.Errors, e => e.Description.Contains("AGG-MISSING", StringComparison.OrdinalIgnoreCase));
        Assert.True(Assert.Single(result.Value.Locations, l => l.LocationName == "cluster-shared" && l.AggregationName == "AGG-OK").IsSuccess);

        // Sibling location's template creation was still attempted.
        _mockCreateService.Verify(
            s => s.CreateCommandTemplateAsync(
                It.Is<CommandTemplateCreationRequest>(r => r.LocationId == okLocationId),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// Item 6: cache eviction (RemoveByTagAsync for PermissionProjectTag, TemplatesInfo, and
    /// one HeappeClusters tag per touched instance) runs unconditionally BEFORE the final
    /// errors check — today's code evicts even when errors.Any() is true. Verified via the
    /// FakeHybridCache's tag-bag semantics: priming a tag then confirming it was evicted.
    /// </summary>
    [Fact(DisplayName = "Cache eviction runs unconditionally even when the sync run has accumulated errors")]
    public async Task SyncCommandTemplatesForResource_CacheEvictionRunsUnconditionally_EvenWithErrors()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var instanceA = new HeappeInstanceSyncEntry(_instanceAUri, new Version(5, 0, 0), HeappeIdentifierA);

        var projectAllocation = BuildProjectAllocation(projectId, resourceId,
        [
            new LocationAllocationInfoSyncEntry(locationId, "cluster-missing", "login-1", ELocationType.HPC, "AGG", instanceA)
        ]);

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocation));

        _mockLocationPartitionsSynchronizationService
            .Setup(s => s.SynchronizationLocationPartitionsAsync(It.IsAny<LocationSyncEntry>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));

        _mockConnectionFactory
            .Setup(f => f.ForServiceAsync(HeappeIdentifierA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceConnection(HeappeIdentifierA, "key-a"));

        _mockHeappeService
            .Setup(s => s.GetProjectByResourceName(It.IsAny<BaseHeappeConnectionInfo>(), "test-resource", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectModel, Error>.Success(BuildHeappeProject(1L)));
        _mockHeappeService
            .Setup(s => s.GetProjectCommandTemplatesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<CommandTemplateModel>, Error>.Success([]));
        // No clusters known -> the single location fails ("missing cluster"), guaranteeing errors.Any() == true.
        _mockHeappeService
            .Setup(s => s.GetClusters(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterDetailedModel>, Error>.Success([]));

        using var dbContext = CreateInMemoryDbContext();
        var cache = new FakeHybridCache();

        var permissionTag = HeappeCacheKeys.PermissionProjectTag(projectId);
        var templatesInfoTag = CacheTags.TemplatesInfo(ProjectShortName);
        var clustersTag = CacheTags.HeappeClusters(HeappeIdentifierA);

        var permissionCalls = 0;
        var templatesInfoCalls = 0;
        var clustersCalls = 0;
        await cache.GetOrCreateAsync(permissionTag, _ => { permissionCalls++; return ValueTask.FromResult("cached"); }, tags: [permissionTag]);
        await cache.GetOrCreateAsync(templatesInfoTag, _ => { templatesInfoCalls++; return ValueTask.FromResult("cached"); }, tags: [templatesInfoTag]);
        await cache.GetOrCreateAsync(clustersTag, _ => { clustersCalls++; return ValueTask.FromResult("cached"); }, tags: [clustersTag]);
        Assert.Equal(1, permissionCalls);
        Assert.Equal(1, templatesInfoCalls);
        Assert.Equal(1, clustersCalls);

        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            cache,
            _mockKillSwitchStateService.Object,
            _nexusSettings);

        var result = await service.SyncCommandTemplatesForResource(ProjectShortName, ResourceName, CancellationToken.None);

        // The run had accumulated errors...
        Assert.False(result.IsSuccess);
        Assert.True(result.Error.Any());

        // ...but all three cache tags were still evicted (unconditional eviction).
        await cache.GetOrCreateAsync(permissionTag, _ => { permissionCalls++; return ValueTask.FromResult("refreshed"); }, tags: [permissionTag]);
        await cache.GetOrCreateAsync(templatesInfoTag, _ => { templatesInfoCalls++; return ValueTask.FromResult("refreshed"); }, tags: [templatesInfoTag]);
        await cache.GetOrCreateAsync(clustersTag, _ => { clustersCalls++; return ValueTask.FromResult("refreshed"); }, tags: [clustersTag]);
        Assert.Equal(2, permissionCalls);
        Assert.Equal(2, templatesInfoCalls);
        Assert.Equal(2, clustersCalls);
    }

    /// <summary>
    /// Item 7a: when every location succeeds, the final branch returns Success(true).
    /// </summary>
    [Fact(DisplayName = "All locations succeeding returns Success(true)")]
    public async Task SyncCommandTemplatesForResource_AllLocationsSucceed_ReturnsSuccessTrue()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var instanceA = new HeappeInstanceSyncEntry(_instanceAUri, new Version(5, 0, 0), HeappeIdentifierA);

        var projectAllocation = BuildProjectAllocation(projectId, resourceId,
        [
            new LocationAllocationInfoSyncEntry(locationId, "cluster-a", "login-a", ELocationType.HPC, "AGG", instanceA)
        ]);

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocation));

        _mockLocationPartitionsSynchronizationService
            .Setup(s => s.SynchronizationLocationPartitionsAsync(It.IsAny<LocationSyncEntry>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));

        _mockConnectionFactory
            .Setup(f => f.ForServiceAsync(HeappeIdentifierA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceConnection(HeappeIdentifierA, "key-a"));

        _mockHeappeService
            .Setup(s => s.GetProjectByResourceName(It.IsAny<BaseHeappeConnectionInfo>(), "test-resource", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectModel, Error>.Success(BuildHeappeProject(1L)));
        _mockHeappeService
            .Setup(s => s.GetProjectCommandTemplatesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<CommandTemplateModel>, Error>.Success([]));

        var nodeType = BuildNodeType(1L, "cluster-a", "AGG");
        var cluster = BuildCluster("cluster-a", [nodeType]);
        _mockHeappeService
            .Setup(s => s.GetClusters(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterDetailedModel>, Error>.Success([cluster]));

        _mockCreateService
            .Setup(s => s.CreateCommandTemplateAsync(It.IsAny<CommandTemplateCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Success());

        using var dbContext = CreateInMemoryDbContext();
        SeedCommandTemplateLocationDefinition(dbContext, locationId, ELocationType.HPC);

        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            new FakeHybridCache(),
            _mockKillSwitchStateService.Object,
            _nexusSettings);

        var result = await service.SyncCommandTemplatesForResource(ProjectShortName, ResourceName, CancellationToken.None);

        // New contract (B03): all locations succeeded — Success with an all-succeeded result.
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.AllSucceeded);
        Assert.True(result.Value.AnySucceeded);
        Assert.All(result.Value.Locations, l => Assert.True(l.IsSuccess));
    }

    /// <summary>
    /// Item 7b: when EVERY location fails (no location succeeds), the overall result is a
    /// top-level Failure whose ValidationError carries every accumulated error — not just the
    /// last one. (Under the B03 partial-result contract, a Failure requires !AnySucceeded.)
    /// </summary>
    [Fact(DisplayName = "Accumulated errors from multiple failure sites are all present in the Failure result")]
    public async Task SyncCommandTemplatesForResource_MultipleFailures_AllErrorsAccumulatedInResult()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var locationAId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();

        var instanceA = new HeappeInstanceSyncEntry(_instanceAUri, new Version(5, 0, 0), HeappeIdentifierA);
        var instanceB = new HeappeInstanceSyncEntry(_instanceBUri, new Version(5, 0, 0), HeappeIdentifierB);

        var projectAllocation = BuildProjectAllocation(projectId, resourceId,
        [
            new LocationAllocationInfoSyncEntry(locationAId, "cluster-a", "login-a", ELocationType.HPC, "AGG-A", instanceA),
            new LocationAllocationInfoSyncEntry(locationBId, "cluster-b", "login-b", ELocationType.HPC, "AGG-B", instanceB)
        ]);

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocation));

        _mockLocationPartitionsSynchronizationService
            .Setup(s => s.SynchronizationLocationPartitionsAsync(It.IsAny<LocationSyncEntry>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));

        // Instance A fails at connection info (NexusApi lists no instance with its identifier) — broadcast-style early failure.
        var unlistedIdentifier = new HeappeIdentifier("heappe-unlisted");
        _mockConnectionFactory
            .Setup(f => f.ForServiceAsync(unlistedIdentifier, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BaseHeappeConnectionInfo, Error>.Failure(Heappe.Errors.HeappeInstanceNotFound(unlistedIdentifier)));
        var instanceANoIdentifier = new HeappeInstanceSyncEntry(_instanceAUri, new Version(5, 0, 0), unlistedIdentifier);
        var projectAllocationWithBrokenInstanceA = BuildProjectAllocation(projectId, resourceId,
        [
            new LocationAllocationInfoSyncEntry(locationAId, "cluster-a", "login-a", ELocationType.HPC, "AGG-A", instanceANoIdentifier),
            new LocationAllocationInfoSyncEntry(locationBId, "cluster-b", "login-b", ELocationType.HPC, "AGG-B", instanceB)
        ]);

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocationWithBrokenInstanceA));

        _mockConnectionFactory
            .Setup(f => f.ForServiceAsync(HeappeIdentifierB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceConnection(HeappeIdentifierB, "key-b"));

        _mockHeappeService
            .Setup(s => s.GetProjectByResourceName(It.IsAny<BaseHeappeConnectionInfo>(), "test-resource", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectModel, Error>.Success(BuildHeappeProject(1L)));
        _mockHeappeService
            .Setup(s => s.GetProjectCommandTemplatesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<CommandTemplateModel>, Error>.Success([]));
        // Instance B also fails, at GetClusters — so we get TWO distinct-origin errors.
        _mockHeappeService
            .Setup(s => s.GetClusters(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterDetailedModel>, Error>.Failure(new Error("B001", Lukdrasil.Error.ErrorSeverity.Error, "instance B clusters unreachable")));

        using var dbContext = CreateInMemoryDbContext();
        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            new FakeHybridCache(),
            _mockKillSwitchStateService.Object,
            _nexusSettings);

        var result = await service.SyncCommandTemplatesForResource(ProjectShortName, ResourceName, CancellationToken.None);

        Assert.False(result.IsSuccess);
        // Both instance A's connection-info error AND instance B's cluster-listing error
        // are present — not just the last one accumulated. (The service wraps the mocked
        // GetClusters failure in its own Errors.FailedToListHeappeClusters(...) message —
        // only that wrapped text, not the mock's raw error, ends up in the ValidationError.)
        Assert.Equal(2, result.Error.Count());
        Assert.Contains(result.Error, e => e.Description.Contains("identifier", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Error, e => e.Description.Contains("Failed to list clusters", StringComparison.OrdinalIgnoreCase));
    }

    // ====== Phase 3: new per-location partial-result behavior (plan-ready.md test plan items 1-4) ======

    /// <summary>
    /// Phase 3 item 1: one HEAppE instance succeeds; a second instance's GetClusters fails.
    /// The result carries one LocationSyncResultEntry per instance — the failing instance's
    /// location is IsSuccess=false with the FailedToListHeappeClusters error, the healthy
    /// instance's location is IsSuccess=true; AnySucceeded==true, AllSucceeded==false.
    /// </summary>
    [Fact(DisplayName = "Phase 3.1: one instance succeeds, sibling instance's GetClusters fails → per-instance breakdown")]
    public async Task Phase3_OneInstanceSucceeds_SiblingGetClustersFails_ReportsPerInstanceBreakdown()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var healthyLocationId = Guid.NewGuid();
        var failingLocationId = Guid.NewGuid();

        var instanceHealthy = new HeappeInstanceSyncEntry(_instanceAUri, new Version(5, 0, 0), HeappeIdentifierA);
        var instanceFailing = new HeappeInstanceSyncEntry(_instanceBUri, new Version(5, 0, 0), HeappeIdentifierB);

        var projectAllocation = BuildProjectAllocation(projectId, resourceId,
        [
            new LocationAllocationInfoSyncEntry(healthyLocationId, "cluster-ok", "login-ok", ELocationType.HPC, "AGG", instanceHealthy),
            new LocationAllocationInfoSyncEntry(failingLocationId, "cluster-fail", "login-fail", ELocationType.HPC, "AGG", instanceFailing)
        ]);

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocation));
        _mockLocationPartitionsSynchronizationService
            .Setup(s => s.SynchronizationLocationPartitionsAsync(It.IsAny<LocationSyncEntry>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));
        _mockConnectionFactory
            .Setup(f => f.ForServiceAsync(It.IsAny<HeappeIdentifier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HeappeIdentifier identifier, CancellationToken _) => ServiceConnection(identifier, "key"));
        _mockHeappeService
            .Setup(s => s.GetProjectByResourceName(It.IsAny<BaseHeappeConnectionInfo>(), "test-resource", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectModel, Error>.Success(BuildHeappeProject(1L)));
        _mockHeappeService
            .Setup(s => s.GetProjectCommandTemplatesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<CommandTemplateModel>, Error>.Success([]));

        var nodeTypeOk = BuildNodeType(1L, "cluster-ok", "AGG");
        var clusterOk = BuildCluster("cluster-ok", [nodeTypeOk]);
        _mockHeappeService
            .Setup(s => s.GetClusters(It.Is<BaseHeappeConnectionInfo>(c => c.Identifier == HeappeIdentifierA), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterDetailedModel>, Error>.Success([clusterOk]));
        _mockHeappeService
            .Setup(s => s.GetClusters(It.Is<BaseHeappeConnectionInfo>(c => c.Identifier == HeappeIdentifierB), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterDetailedModel>, Error>.Failure(new Error("B001", Lukdrasil.Error.ErrorSeverity.Error, "clusters unreachable")));

        _mockCreateService
            .Setup(s => s.CreateCommandTemplateAsync(It.IsAny<CommandTemplateCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Success());

        using var dbContext = CreateInMemoryDbContext();
        SeedCommandTemplateLocationDefinition(dbContext, healthyLocationId, ELocationType.HPC);

        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            new FakeHybridCache(),
            _mockKillSwitchStateService.Object,
            _nexusSettings);

        var result = await service.SyncCommandTemplatesForResource(ProjectShortName, ResourceName, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var syncResult = result.Value;
        Assert.True(syncResult.AnySucceeded);
        Assert.False(syncResult.AllSucceeded);
        Assert.Equal(2, syncResult.Locations.Count);

        var healthy = Assert.Single(syncResult.Locations, l => l.LocationName == "cluster-ok");
        Assert.True(healthy.IsSuccess);
        Assert.Equal(_instanceAUri, healthy.HeappeInstanceUri);

        var failing = Assert.Single(syncResult.Locations, l => l.LocationName == "cluster-fail");
        Assert.False(failing.IsSuccess);
        Assert.Equal(_instanceBUri, failing.HeappeInstanceUri);
        Assert.Contains(failing.Errors, e => e.Description.Contains("Failed to list clusters", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// An instance without a direct URL is reported under its NexusApi proxy address
    /// <c>{TokenExchange:Nexus:BaseUrl}/api/heappe/{identifier}/</c>; an instance with one keeps it.
    /// </summary>
    [Fact(DisplayName = "An instance without a URL is reported under the proxy of TokenExchange:Nexus:BaseUrl")]
    public async Task SyncCommandTemplatesForResource_ReportsAnInstanceWithoutUrl_UnderTheProxyOfTheConfiguredBaseUrl()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var healthyLocationId = Guid.NewGuid();
        var failingLocationId = Guid.NewGuid();

        var instanceHealthy = new HeappeInstanceSyncEntry(_instanceAUri, new Version(5, 0, 0), HeappeIdentifierA);
        var instanceWithoutUrl = new HeappeInstanceSyncEntry(null, new Version(5, 0, 0), HeappeIdentifierB);

        var projectAllocation = BuildProjectAllocation(projectId, resourceId,
        [
            new LocationAllocationInfoSyncEntry(healthyLocationId, "cluster-ok", "login-ok", ELocationType.HPC, "AGG", instanceHealthy),
            new LocationAllocationInfoSyncEntry(failingLocationId, "cluster-fail", "login-fail", ELocationType.HPC, "AGG", instanceWithoutUrl)
        ]);

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocation));
        _mockLocationPartitionsSynchronizationService
            .Setup(s => s.SynchronizationLocationPartitionsAsync(It.IsAny<LocationSyncEntry>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));
        _mockConnectionFactory
            .Setup(f => f.ForServiceAsync(It.IsAny<HeappeIdentifier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HeappeIdentifier identifier, CancellationToken _) => ServiceConnection(identifier, "key"));
        _mockHeappeService
            .Setup(s => s.GetProjectByResourceName(It.IsAny<BaseHeappeConnectionInfo>(), "test-resource", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectModel, Error>.Success(BuildHeappeProject(1L)));
        _mockHeappeService
            .Setup(s => s.GetProjectCommandTemplatesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<CommandTemplateModel>, Error>.Success([]));
        _mockHeappeService
            .Setup(s => s.GetClusters(It.Is<BaseHeappeConnectionInfo>(c => c.Identifier == HeappeIdentifierA), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterDetailedModel>, Error>.Success([BuildCluster("cluster-ok", [BuildNodeType(1L, "cluster-ok", "AGG")])]));
        _mockHeappeService
            .Setup(s => s.GetClusters(It.Is<BaseHeappeConnectionInfo>(c => c.Identifier == HeappeIdentifierB), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterDetailedModel>, Error>.Failure(new Error("B001", Lukdrasil.Error.ErrorSeverity.Error, "clusters unreachable")));
        _mockCreateService
            .Setup(s => s.CreateCommandTemplateAsync(It.IsAny<CommandTemplateCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Success());

        using var dbContext = CreateInMemoryDbContext();
        SeedCommandTemplateLocationDefinition(dbContext, healthyLocationId, ELocationType.HPC);

        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            new FakeHybridCache(),
            _mockKillSwitchStateService.Object,
            Options.Create(new TokenExchangeOptions { Nexus = { BaseUrl = new Uri("https://nexus.example:8443") } }));

        var result = await service.SyncCommandTemplatesForResource(ProjectShortName, ResourceName, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var healthy = Assert.Single(result.Value.Locations, l => l.LocationName == "cluster-ok");
        Assert.Equal(_instanceAUri, healthy.HeappeInstanceUri);
        var withoutUrl = Assert.Single(result.Value.Locations, l => l.LocationName == "cluster-fail");
        Assert.Equal(new Uri("https://nexus.example:8443/api/heappe/heappe-b/"), withoutUrl.HeappeInstanceUri);
    }

    /// <summary>
    /// Phase 3 item 2: single instance, one location's cluster aggregation is missing while a
    /// sibling location under the same instance succeeds → only the missing-aggregation
    /// location is IsSuccess=false.
    /// </summary>
    [Fact(DisplayName = "Phase 3.2: single instance, one location's aggregation missing, sibling succeeds → only that one fails")]
    public async Task Phase3_OneLocationAggregationMissing_SiblingSucceeds_OnlyMissingOneFails()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var missingAggLocationId = Guid.NewGuid();
        var okLocationId = Guid.NewGuid();

        var instanceA = new HeappeInstanceSyncEntry(_instanceAUri, new Version(5, 0, 0), HeappeIdentifierA);

        var projectAllocation = BuildProjectAllocation(projectId, resourceId,
        [
            new LocationAllocationInfoSyncEntry(missingAggLocationId, "cluster-shared", "login-1", ELocationType.HPC, "AGG-MISSING", instanceA),
            new LocationAllocationInfoSyncEntry(okLocationId, "cluster-shared", "login-2", ELocationType.HPC, "AGG-OK", instanceA)
        ]);

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocation));
        _mockLocationPartitionsSynchronizationService
            .Setup(s => s.SynchronizationLocationPartitionsAsync(It.IsAny<LocationSyncEntry>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));
        _mockConnectionFactory
            .Setup(f => f.ForServiceAsync(HeappeIdentifierA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceConnection(HeappeIdentifierA, "key-a"));
        _mockHeappeService
            .Setup(s => s.GetProjectByResourceName(It.IsAny<BaseHeappeConnectionInfo>(), "test-resource", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectModel, Error>.Success(BuildHeappeProject(1L)));
        _mockHeappeService
            .Setup(s => s.GetProjectCommandTemplatesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<CommandTemplateModel>, Error>.Success([]));

        var nodeTypeOk = BuildNodeType(1L, "cluster-shared", "AGG-OK");
        var clusterShared = BuildCluster("cluster-shared", [nodeTypeOk]);
        _mockHeappeService
            .Setup(s => s.GetClusters(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterDetailedModel>, Error>.Success([clusterShared]));

        _mockCreateService
            .Setup(s => s.CreateCommandTemplateAsync(It.IsAny<CommandTemplateCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Success());

        using var dbContext = CreateInMemoryDbContext();
        SeedCommandTemplateLocationDefinition(dbContext, okLocationId, ELocationType.HPC);

        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            new FakeHybridCache(),
            _mockKillSwitchStateService.Object,
            _nexusSettings);

        var result = await service.SyncCommandTemplatesForResource(ProjectShortName, ResourceName, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var syncResult = result.Value;
        Assert.True(syncResult.AnySucceeded);
        Assert.False(syncResult.AllSucceeded);

        var okEntry = Assert.Single(syncResult.Locations, l => l.LocationName == "cluster-shared" && l.AggregationName == "AGG-OK");
        Assert.True(okEntry.IsSuccess);

        var missingEntry = Assert.Single(syncResult.Locations, l => l.LocationName == "cluster-shared" && l.AggregationName == "AGG-MISSING");
        Assert.False(missingEntry.IsSuccess);
        Assert.Contains(missingEntry.Errors, e => e.Description.Contains("AGG-MISSING", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Phase 3 item 3: a location's partition sync fails → that location's entry is
    /// IsSuccess=false with the partition error, and no CreateCommandTemplateAsync call was
    /// attempted for it (skip rule + partition-error propagation regression).
    /// </summary>
    [Fact(DisplayName = "Phase 3.3: partition sync failure skips template sync and reports the partition error for that location")]
    public async Task Phase3_PartitionSyncFailure_SkipsTemplateSync_AndReportsPartitionError()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var failingLocationId = Guid.NewGuid();
        var okLocationId = Guid.NewGuid();

        var instanceA = new HeappeInstanceSyncEntry(_instanceAUri, new Version(5, 0, 0), HeappeIdentifierA);

        var projectAllocation = BuildProjectAllocation(projectId, resourceId,
        [
            new LocationAllocationInfoSyncEntry(failingLocationId, "cluster-a", "login-fail", ELocationType.HPC, "AGG", instanceA),
            new LocationAllocationInfoSyncEntry(okLocationId, "cluster-a", "login-ok", ELocationType.HPC, "AGG", instanceA)
        ]);

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocation));

        _mockLocationPartitionsSynchronizationService
            .Setup(s => s.SynchronizationLocationPartitionsAsync(
                It.Is<LocationSyncEntry>(l => l.Id == failingLocationId), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Failure(new Error("P001", Lukdrasil.Error.ErrorSeverity.Error, "partition sync failed")));
        _mockLocationPartitionsSynchronizationService
            .Setup(s => s.SynchronizationLocationPartitionsAsync(
                It.Is<LocationSyncEntry>(l => l.Id == okLocationId), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));

        _mockConnectionFactory
            .Setup(f => f.ForServiceAsync(HeappeIdentifierA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceConnection(HeappeIdentifierA, "key-a"));
        _mockHeappeService
            .Setup(s => s.GetProjectByResourceName(It.IsAny<BaseHeappeConnectionInfo>(), "test-resource", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectModel, Error>.Success(BuildHeappeProject(1L)));
        _mockHeappeService
            .Setup(s => s.GetProjectCommandTemplatesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<CommandTemplateModel>, Error>.Success([]));

        // cluster-a is known with an AGG aggregation, so the OK location can fully succeed.
        var nodeType = BuildNodeType(1L, "cluster-a", "AGG");
        var cluster = BuildCluster("cluster-a", [nodeType]);
        _mockHeappeService
            .Setup(s => s.GetClusters(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterDetailedModel>, Error>.Success([cluster]));

        _mockCreateService
            .Setup(s => s.CreateCommandTemplateAsync(It.IsAny<CommandTemplateCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Success());

        using var dbContext = CreateInMemoryDbContext();
        SeedCommandTemplateLocationDefinition(dbContext, okLocationId, ELocationType.HPC);
        SeedCommandTemplateLocationDefinition(dbContext, failingLocationId, ELocationType.HPC);

        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            new FakeHybridCache(),
            _mockKillSwitchStateService.Object,
            _nexusSettings);

        var result = await service.SyncCommandTemplatesForResource(ProjectShortName, ResourceName, CancellationToken.None);

        // OK location succeeded, so overall is Success with a partial breakdown.
        Assert.True(result.IsSuccess);
        var syncResult = result.Value;

        // Both locations share LocationName "cluster-a" and AggregationName "AGG", so they are
        // indistinguishable by name — disambiguate by IsSuccess (the test's intent: one failed, one ok).
        var failingEntry = Assert.Single(syncResult.Locations, l => !l.IsSuccess);
        Assert.Contains(failingEntry.Errors, e => e.Description.Contains("partition sync failed", StringComparison.OrdinalIgnoreCase));

        var okEntry = Assert.Single(syncResult.Locations, l => l.IsSuccess);
        Assert.True(okEntry.IsSuccess);

        // No template sync was attempted for the partition-failed location...
        _mockCreateService.Verify(
            s => s.CreateCommandTemplateAsync(
                It.Is<CommandTemplateCreationRequest>(r => r.LocationId == failingLocationId),
                It.IsAny<CancellationToken>()),
            Times.Never);
        // ...but it was attempted for the healthy sibling.
        _mockCreateService.Verify(
            s => s.CreateCommandTemplateAsync(
                It.Is<CommandTemplateCreationRequest>(r => r.LocationId == okLocationId),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        // Regression: partition sync must receive the RESOURCE name, never the project short name.
        // A prior bug passed projectShortName here, so a partition credential failure was mislabelled
        // with the project ("resource: '<project>'") instead of the actual resource.
        _mockLocationPartitionsSynchronizationService.Verify(
            s => s.SynchronizationLocationPartitionsAsync(It.IsAny<LocationSyncEntry>(), ResourceName, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        _mockLocationPartitionsSynchronizationService.Verify(
            s => s.SynchronizationLocationPartitionsAsync(It.IsAny<LocationSyncEntry>(), ProjectShortName, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Phase 3 item 4: every location fails → AnySucceeded==false and the returned
    /// ValidationResult&lt;CommandTemplateSyncResult&gt;.IsSuccess==false (top-level Failure).
    /// </summary>
    [Fact(DisplayName = "Phase 3.4: every location fails → AnySucceeded==false and top-level Failure")]
    public async Task Phase3_EveryLocationFails_ReturnsTopLevelFailure()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var locationAId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();

        var instanceA = new HeappeInstanceSyncEntry(_instanceAUri, new Version(5, 0, 0), HeappeIdentifierA);

        var projectAllocation = BuildProjectAllocation(projectId, resourceId,
        [
            new LocationAllocationInfoSyncEntry(locationAId, "cluster-missing-a", "login-a", ELocationType.HPC, "AGG", instanceA),
            new LocationAllocationInfoSyncEntry(locationBId, "cluster-missing-b", "login-b", ELocationType.HPC, "AGG", instanceA)
        ]);

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocation));
        _mockLocationPartitionsSynchronizationService
            .Setup(s => s.SynchronizationLocationPartitionsAsync(It.IsAny<LocationSyncEntry>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));
        _mockConnectionFactory
            .Setup(f => f.ForServiceAsync(HeappeIdentifierA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceConnection(HeappeIdentifierA, "key-a"));
        _mockHeappeService
            .Setup(s => s.GetProjectByResourceName(It.IsAny<BaseHeappeConnectionInfo>(), "test-resource", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectModel, Error>.Success(BuildHeappeProject(1L)));
        _mockHeappeService
            .Setup(s => s.GetProjectCommandTemplatesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<CommandTemplateModel>, Error>.Success([]));
        // No clusters known → both locations fail at the missing-cluster step.
        _mockHeappeService
            .Setup(s => s.GetClusters(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterDetailedModel>, Error>.Success([]));

        using var dbContext = CreateInMemoryDbContext();
        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            new FakeHybridCache(),
            _mockKillSwitchStateService.Object,
            _nexusSettings);

        var result = await service.SyncCommandTemplatesForResource(ProjectShortName, ResourceName, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Error.Any());
    }

    // ====== B08: sync skips a frozen location (ADR-0077 §6) ======

    /// <summary>
    /// Mandatory test #7 (B08): a resource with one frozen and one healthy location under the
    /// same HEAppE instance syncs the healthy location, entirely SKIPS the frozen one (no
    /// partition sync, no template sync attempted against it), and emits a Warning log
    /// identifying the skipped location by name. Location-grain (B02 SHIPPED API,
    /// ADR-0077 §6): the whitelist is not consulted for this skip.
    /// </summary>
    [Fact(DisplayName = "B08: sync skips a frozen location (Warning log) and still syncs the healthy sibling")]
    public async Task SyncCommandTemplatesForResource_FrozenLocation_IsSkipped_HealthySiblingStillSyncs()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var frozenLocationId = Guid.NewGuid();
        var healthyLocationId = Guid.NewGuid();

        var instanceA = new HeappeInstanceSyncEntry(_instanceAUri, new Version(5, 0, 0), HeappeIdentifierA);

        var projectAllocation = BuildProjectAllocation(projectId, resourceId,
        [
            new LocationAllocationInfoSyncEntry(frozenLocationId, "cluster-frozen", "login-frozen", ELocationType.HPC, "AGG", instanceA),
            new LocationAllocationInfoSyncEntry(healthyLocationId, "cluster-healthy", "login-healthy", ELocationType.HPC, "AGG", instanceA)
        ]);

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocation));

        // Batch call over BOTH location ids in one shot (no N+1) — only the frozen one comes back.
        _mockKillSwitchStateService
            .Setup(s => s.GetEnforcedKilledLocationIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(frozenLocationId) && ids.Contains(healthyLocationId) && ids.Count == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlySet<Guid>)new HashSet<Guid> { frozenLocationId });

        _mockLocationPartitionsSynchronizationService
            .Setup(s => s.SynchronizationLocationPartitionsAsync(
                It.Is<LocationSyncEntry>(l => l.Id == healthyLocationId), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));

        _mockConnectionFactory
            .Setup(f => f.ForServiceAsync(HeappeIdentifierA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceConnection(HeappeIdentifierA, "key-a"));

        _mockHeappeService
            .Setup(s => s.GetProjectByResourceName(It.IsAny<BaseHeappeConnectionInfo>(), "test-resource", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectModel, Error>.Success(BuildHeappeProject(1L)));
        _mockHeappeService
            .Setup(s => s.GetProjectCommandTemplatesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<CommandTemplateModel>, Error>.Success([]));

        // Only the healthy location's cluster needs to be known — the frozen one is filtered
        // out before any per-location HEAppE lookup happens.
        var nodeType = BuildNodeType(1L, "cluster-healthy", "AGG");
        var cluster = BuildCluster("cluster-healthy", [nodeType]);
        _mockHeappeService
            .Setup(s => s.GetClusters(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterDetailedModel>, Error>.Success([cluster]));

        _mockCreateService
            .Setup(s => s.CreateCommandTemplateAsync(It.IsAny<CommandTemplateCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Success());

        using var dbContext = CreateInMemoryDbContext();
        SeedCommandTemplateLocationDefinition(dbContext, healthyLocationId, ELocationType.HPC);

        var getWarningMessage = SetupLogCapture(LogLevel.Warning);

        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            new FakeHybridCache(),
            _mockKillSwitchStateService.Object,
            _nexusSettings);

        var result = await service.SyncCommandTemplatesForResource(ProjectShortName, ResourceName, CancellationToken.None);

        // The frozen location is excluded from the run entirely (not reported as a failure) —
        // only the healthy location was attempted, and it succeeded.
        Assert.True(result.IsSuccess);
        var syncResult = result.Value;
        Assert.True(syncResult.AllSucceeded);
        var healthyEntry = Assert.Single(syncResult.Locations);
        Assert.Equal("cluster-healthy", healthyEntry.LocationName);
        Assert.True(healthyEntry.IsSuccess);

        _mockCreateService.Verify(
            s => s.CreateCommandTemplateAsync(
                It.Is<CommandTemplateCreationRequest>(r => r.LocationId == healthyLocationId),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        _mockCreateService.Verify(
            s => s.CreateCommandTemplateAsync(
                It.Is<CommandTemplateCreationRequest>(r => r.LocationId == frozenLocationId),
                It.IsAny<CancellationToken>()),
            Times.Never);

        // The frozen location never reaches partition sync — it is skipped before that step.
        _mockLocationPartitionsSynchronizationService.Verify(
            s => s.SynchronizationLocationPartitionsAsync(
                It.Is<LocationSyncEntry>(l => l.Id == frozenLocationId), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        Assert.Contains("cluster-frozen", getWarningMessage(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Regression guard (MAJOR-1, review of B08): when a resource's ONLY location is frozen,
    /// the skip empties <c>locationGroups</c> entirely. Returning <c>Failure([])</c> here would
    /// render as <c>IsSuccess=false</c> with a ZERO-count error list (Lukdrasil.Result 2.0.0) —
    /// a correct-but-unexplained failure that <c>WorkItemProcessor</c> would log as
    /// <c>"Background sync FAILED for {Key}: []"</c> every periodic run for the whole incident.
    /// The result must instead carry a real, descriptive, reason-free error (ADR-0077: the
    /// kill-switch reason is admin-only and must never surface on this path).
    /// </summary>
    [Fact(DisplayName = "B08: an all-frozen resource fails with a descriptive, non-empty, reason-free error (not Failure([]))")]
    public async Task SyncCommandTemplatesForResource_OnlyLocationFrozen_ReturnsDescriptiveNonEmptyError()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var frozenLocationId = Guid.NewGuid();

        var instanceA = new HeappeInstanceSyncEntry(_instanceAUri, new Version(5, 0, 0), HeappeIdentifierA);

        var projectAllocation = BuildProjectAllocation(projectId, resourceId,
        [
            new LocationAllocationInfoSyncEntry(frozenLocationId, "cluster-frozen", "login-frozen", ELocationType.HPC, "AGG", instanceA)
        ]);

        _mockUserorgService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(projectAllocation));

        _mockKillSwitchStateService
            .Setup(s => s.GetEnforcedKilledLocationIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(frozenLocationId) && ids.Count == 1),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlySet<Guid>)new HashSet<Guid> { frozenLocationId });

        using var dbContext = CreateInMemoryDbContext();
        var service = new ProjectCommandTemplateSyncService(
            _mockLogger.Object,
            _mockUserorgService.Object,
            _mockHeappeService.Object,
            _mockConnectionFactory.Object,
            _mockLocationPartitionsSynchronizationService.Object,
            _mockCreateService.Object,
            dbContext,
            new FakeHybridCache(),
            _mockKillSwitchStateService.Object,
            _nexusSettings);

        var result = await service.SyncCommandTemplatesForResource(ProjectShortName, ResourceName, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Error);
        Assert.Contains(result.Error, e => e.Description.Contains("frozen", StringComparison.OrdinalIgnoreCase)
            && e.Description.Contains("kill-switch", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Error, e => e.Description.Contains("reason", StringComparison.OrdinalIgnoreCase));

        // No HEAppE/partition work was attempted — the only location was frozen and excluded
        // before any of it, same as the partial-skip path.
        _mockLocationPartitionsSynchronizationService.Verify(
            s => s.SynchronizationLocationPartitionsAsync(It.IsAny<LocationSyncEntry>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static ProjectResourceInfoSyncEntry BuildProjectAllocation(
        Guid projectId, Guid resourceId, IReadOnlyList<LocationAllocationInfoSyncEntry> locations) =>
        new(
            Id: projectId,
            Name: "test-project-resource",
            ShortName: "tpr",
            Description: null,
            StartDate: DateTime.UtcNow.AddDays(-1),
            EndDate: DateTime.UtcNow.AddYears(1),
            Resource: new ResourceInfoSyncEntry(
                Id: resourceId,
                Name: "test-resource",
                Description: null,
                StartDate: DateTime.UtcNow.AddDays(-1),
                EndDate: DateTime.UtcNow.AddYears(1),
                Locations: locations));

    private static ProjectModel BuildHeappeProject(long id) =>
        new(id, "HeappeProject", "", "", DateTime.UtcNow.AddYears(-1), DateTime.UtcNow.AddYears(1), 0, false, "/scripts", false, []);

    private static NodeTypeModel BuildNodeType(long id, string clusterAllocationName, string aggregationName) =>
        new(
            Id: id, Name: "compute", Description: "", NumberOfNodes: 1, CoresPerNode: 8, MaxWalltime: 3600,
            FileTransferMethodId: 0, Queue: "queue1", QualityOfService: "", ClusterAllocationName: clusterAllocationName,
            ClusterNodeTypeAggregation: new ClusterNodeTypeAggregationModel(1L, aggregationName, "", "", DateTime.MinValue, null),
            Accounting: [], Projects: []);

    private static ClusterDetailedModel BuildCluster(string name, IList<NodeTypeModel> nodeTypes) =>
        new(
            Id: 1L, Name: name, Description: null, MasterNodeName: "master", SchedulerType: 0, TimeZone: "UTC",
            Port: null, ConnectionProtocol: 0, UpdateJobStateByServiceAccount: false, DomainName: null,
            ProxyConnection: null, FileTransferMethodIds: [], NodeTypes: nodeTypes);

    /// <summary>
    /// Seeds a CommandTemplateLocationDefinition (with its parent CommandTemplateDefinition)
    /// for the given location, matched by LocationType (no LocationPartitionId), so
    /// GetCommandTemplateDefinitionsAsync finds at least one template definition to sync
    /// for that location, allowing the flow to reach createService.CreateCommandTemplateAsync.
    /// </summary>
    private static void SeedCommandTemplateLocationDefinition(HeappeIntegrationDbContext dbContext, Guid locationId, ELocationType locationType)
    {
        var definition = new CommandTemplateDefinition
        {
            Id = Guid.NewGuid(),
            Name = "tmpl-def",
            Type = ECommandTemplateType.Unknown
        };
        dbContext.Set<CommandTemplateDefinition>().Add(definition);

        var locationDefinition = new CommandTemplateLocationDefinition
        {
            Id = Guid.NewGuid(),
            PreparationCommand = null,
            ExecutionCommand = "run.sh",
            LocationType = locationType,
            CommandTemplateDefinition = definition,
            CommandTemplateDefinitionId = definition.Id,
            LocationPartitionId = null
        };
        dbContext.Set<CommandTemplateLocationDefinition>().Add(locationDefinition);
        dbContext.SaveChanges();
    }

    /// <summary>
    /// Creates an in-memory DbContext for testing.
    /// Allows tests to run locally without external database.
    /// </summary>
    private static HeappeIntegrationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;

        return new HeappeIntegrationDbContext(options);
    }

    /// <summary>
    /// Opens a SQLite "Data Source=:memory:" connection. The connection must stay open for the
    /// lifetime of the in-memory SQLite database and be disposed by the caller.
    /// </summary>
    private static Microsoft.Data.Sqlite.SqliteConnection CreateOpenSqliteConnection()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Creates a DbContext backed by a real (SQLite) relational provider. Needed for
    /// ProcessAllocationExpiration scenarios that call ExecuteUpdateAsync — a relational-only
    /// EF Core API not supported by the InMemory provider used by every other test in this file.
    /// </summary>
    private static HeappeIntegrationDbContext CreateSqliteDbContext(Microsoft.Data.Sqlite.SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseSqlite(connection)
            .Options;

        var dbContext = new HeappeIntegrationDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }
}

