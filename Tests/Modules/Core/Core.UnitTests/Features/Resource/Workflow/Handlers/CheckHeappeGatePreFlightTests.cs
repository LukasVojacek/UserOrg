using Core.DataAccess.Queries.Contracts;
using Core.Features.Resource.Workflow;
using Core.Features.Resource.Workflow.Handlers;
using Core.Features.Resource.Workflow.Steps;

using Moq;

using UserOrg.Shared.Persistance.Abstractions.Enums;

using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.Features.Resource.Workflow.Handlers;

/// <summary>
/// The HEAppE gate is a pure pre-flight check (T-005-01, plan decisions D1 and D3): ADR-0050 §4 and ADR-0057
/// both state it runs "before anything is created", and the <c>ProjectResource</c> row is first written one
/// step later, by <see cref="InitializeProjectResourceHandler"/>. These tests pin that contract from the
/// outside — the gate must pass a brand-new resource through, must not read or write persistence at all, and
/// must not fan its allocation-unit-type lookups out concurrently over the one scoped
/// <c>DatabaseContext</c> that backs <see cref="IResourceAssignmentQuery"/>.
/// </summary>
public sealed class CheckHeappeGatePreFlightTests
{
    /// <summary>
    /// The brand-new-resource path: nothing has inserted the <c>ProjectResource</c> row yet, so the natural-key
    /// lookup the handler used to make can only ever come back empty. The gate must still complete, and the
    /// strategy must move the flow on to <see cref="InitializeProjectResourceStep"/> — the step that actually
    /// creates the row.
    /// </summary>
    [Fact]
    public async Task Should_ReachInitializeProjectResourceStep_When_ResourceDoesNotExistYet()
    {
        // Arrange — a repository that finds nothing, which is the real state of the world for a create.
        var repository = new Mock<IRepository<ResourceAggregate, ProjectResourceKey>>();
        repository
            .Setup(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<ResourceAggregate?>(null));

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var locationQuery = new Mock<ILocationQuery>();
        locationQuery.Setup(q => q.GetLocationByProviderAndNameAsync(
                It.IsAny<ProviderName>(),
                It.IsAny<LocationName>(),
                It.IsAny<IReadOnlyList<ProjectShortName>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationSummary(
                Name: "location-1",
                Description: null,
                Endpoint: "https://heappe-1.example.test",
                IsPrivate: false,
                IsShared: true,
                LocationType: Core.Domain.Shared.ELocationType.HPC,
                ProviderName: "acme",
                Mode: Core.Domain.Shared.EOperationMode.ReadOnly,
                DefaultServiceIntegrationUrl: "https://heappe-1.example.test"));

        var step = await CheckHeappeHandlerHarness.BuildStepAsync(EAllocationUnitType.NodeHours);
        var handler = CheckHeappeHandlerHarness.CreateHandler(
            CheckHeappeHandlerHarness.AliveProbe(),
            CheckHeappeHandlerHarness.ExistingAllocationUnitTypes(),
            repository.Object,
            unitOfWork.Object,
            locationQuery.Object);

        // Act — the gate must not reject a resource that does not exist yet.
        var result = await handler.HandleAsync(step, TestContext.Current.CancellationToken);

        // Assert — and the flow must be able to walk on to the step that creates it.
        Assert.Null(result.Data);

        var next = new CreateStrategy().Transition(step, result, null!);
        var initialize = Assert.IsType<InitializeProjectResourceStep>(next);
        Assert.Equal(step.RequestId, initialize.RequestId);
        Assert.Equal(step.Data.Name, initialize.Data.Name);
    }

    /// <summary>
    /// The gate owns no persistence. Both doubles are strict with no setups at all, so any call — a read, a
    /// state mutation or a save — fails the test on the spot.
    /// </summary>
    [Fact]
    public async Task Should_NotTouchTheResourceRepository_When_TheHeappeGateRuns()
    {
        // Arrange
        var repository = new Mock<IRepository<ResourceAggregate, ProjectResourceKey>>(MockBehavior.Strict);
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);

        var locationQuery = new Mock<ILocationQuery>();
        locationQuery.Setup(q => q.GetLocationByProviderAndNameAsync(
                It.IsAny<ProviderName>(),
                It.IsAny<LocationName>(),
                It.IsAny<IReadOnlyList<ProjectShortName>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationSummary(
                Name: "location-1",
                Description: null,
                Endpoint: "https://heappe-1.example.test",
                IsPrivate: false,
                IsShared: true,
                LocationType: Core.Domain.Shared.ELocationType.HPC,
                ProviderName: "acme",
                Mode: Core.Domain.Shared.EOperationMode.ReadOnly,
                DefaultServiceIntegrationUrl: "https://heappe-1.example.test"));

        var step = await CheckHeappeHandlerHarness.BuildStepAsync(EAllocationUnitType.NodeHours);
        var handler = CheckHeappeHandlerHarness.CreateHandler(
            CheckHeappeHandlerHarness.AliveProbe(),
            CheckHeappeHandlerHarness.ExistingAllocationUnitTypes(),
            repository.Object,
            unitOfWork.Object,
            locationQuery.Object);

        // Act
        var result = await handler.HandleAsync(step, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Data);
        repository.VerifyNoOtherCalls();
        unitOfWork.VerifyNoOtherCalls();
    }

    /// <summary>
    /// <see cref="IResourceAssignmentQuery"/> is a scoped adapter over the single scoped
    /// <c>DatabaseContext</c> (<c>Core.DataAccess/Queries/DataQuery.cs</c>,
    /// <c>Core.DataAccess/ServiceCollectionExtensions.cs</c>), so overlapping lookups are overlapping
    /// operations on one <c>DbContext</c>. The double below is the same guard EF's own
    /// <c>ConcurrencyDetector</c> is: a second call that starts while the first is still in flight throws
    /// exactly what EF would throw.
    /// </summary>
    [Fact]
    public async Task Should_QueryAllocationUnitTypesWithoutConcurrentContextUse_When_SeveralUnitTypesAreRequested()
    {
        // Arrange — two distinct allocation unit types, i.e. two distinct lookups.
        var assignments = new SingleContextAssignmentQuery();

        var repository = new Mock<IRepository<ResourceAggregate, ProjectResourceKey>>();
        repository
            .Setup(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<ResourceAggregate?>(null));

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var locationQuery = new Mock<ILocationQuery>();
        locationQuery.Setup(q => q.GetLocationByProviderAndNameAsync(
                It.IsAny<ProviderName>(),
                It.IsAny<LocationName>(),
                It.IsAny<IReadOnlyList<ProjectShortName>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationSummary(
                Name: "location-1",
                Description: null,
                Endpoint: "https://heappe-1.example.test",
                IsPrivate: false,
                IsShared: true,
                LocationType: Core.Domain.Shared.ELocationType.HPC,
                ProviderName: "acme",
                Mode: Core.Domain.Shared.EOperationMode.ReadOnly,
                DefaultServiceIntegrationUrl: "https://heappe-1.example.test"));

        var step = await CheckHeappeHandlerHarness.BuildStepAsync(EAllocationUnitType.NodeHours, EAllocationUnitType.CoreHours);
        var handler = CheckHeappeHandlerHarness.CreateHandler(
            CheckHeappeHandlerHarness.AliveProbe(),
            assignments,
            repository.Object,
            unitOfWork.Object,
            locationQuery.Object);

        // Act
        var exception = await Record.ExceptionAsync(() => handler.HandleAsync(step, TestContext.Current.CancellationToken));

        // Assert — report the overlap itself first; it is the diagnosis, the exception is only its symptom.
        Assert.False(
            assignments.ObservedOverlap,
            "AllocationUnitTypeExistsAsync was entered a second time while the first call was still in flight — that is concurrent use of the one scoped DatabaseContext behind IResourceAssignmentQuery.");
        Assert.Null(exception);

        // Both distinct unit types still have to be checked; serialising must not silently skip one.
        int[] expectedIds = [(int)EAllocationUnitType.NodeHours, (int)EAllocationUnitType.CoreHours];
        Assert.Equal(expectedIds.Order().ToArray(), assignments.QueriedIds.Order().ToArray());
    }

    /// <summary>
    /// An <see cref="IResourceAssignmentQuery"/> that behaves like the real one: every call goes through one
    /// <c>DatabaseContext</c>, so a second concurrent call throws EF's second-operation error instead of
    /// quietly serving both.
    /// </summary>
    private sealed class SingleContextAssignmentQuery : IResourceAssignmentQuery
    {
        private const string SecondOperationMessage =
            "A second operation was started on this context instance before a previous operation completed. This is usually caused by different threads concurrently using the same instance of DbContext.";

        private readonly Lock _gate = new();
        private readonly List<int> _queriedIds = [];
        private int _inFlight;

        internal bool ObservedOverlap { get; private set; }

        internal IReadOnlyList<int> QueriedIds
        {
            get
            {
                lock (_gate)
                {
                    return [.. _queriedIds];
                }
            }
        }

        public async Task<bool> AllocationUnitTypeExistsAsync(int allocationUnitTypeId, CancellationToken ct)
        {
            lock (_gate)
            {
                _queriedIds.Add(allocationUnitTypeId);
            }

            if (Interlocked.Increment(ref _inFlight) > 1)
            {
                ObservedOverlap = true;
                Interlocked.Decrement(ref _inFlight);
                throw new InvalidOperationException(SecondOperationMessage);
            }

            try
            {
                // A real round trip yields; without a yield point nothing could ever overlap and the guard
                // would be decoration rather than a test.
                await Task.Delay(TimeSpan.FromMilliseconds(25), ct);
                return true;
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public Task<bool> HasNonSharedLocationAsync(ProjectResourceKey key, CancellationToken ct)
            => throw new NotSupportedException("The HEAppE gate must not call HasNonSharedLocationAsync.");

        public Task<IReadOnlyList<ResourceProjectRef>> GetProjectsUsingResourceAsync(ProjectResourceKey key, CancellationToken ct)
            => throw new NotSupportedException("The HEAppE gate must not call GetProjectsUsingResourceAsync.");

        public Task<Guid?> GetResourceSurrogateAsync(ProjectResourceKey key, CancellationToken ct)
            => throw new NotSupportedException("The HEAppE gate must not call GetResourceSurrogateAsync.");
    }
}
