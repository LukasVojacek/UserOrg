using Core.DataAccess.Queries.Contracts;
using Core.Domain.Provider;
using Core.Domain.Shared;
using Core.Features.Resource.Workflow.Handlers;
using Core.Features.Resource.Workflow.Steps;

using Heappe.Contracts;

using Moq;

using UserOrg.Shared;

using UserOrg.Shared.Persistance.Abstractions.Enums;

using WorkflowEngine.Contracts.Exceptions;

using static Core.Features.Resource.CreateResourceOnLocation.CreateResourceOnLocationEndpoint;

using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.Features.Resource.Workflow.Handlers;

/// <summary>
/// Characterization tests for <see cref="CheckHeappeHandler"/>. The happy path is a safety net for the
/// surrounding workflow wiring changes; the sad paths pin the exact exception + error codes that moved here
/// from the endpoint when B12 rewrote it for the async 202 contract — a transient <see cref="RetryableException"/>
/// for the HEAppE liveness gate, and a <see cref="PermanentException"/> for the non-transient missing
/// allocation-unit-type.
/// <para>
/// The handler is built through <see cref="CheckHeappeHandlerHarness"/> rather than a literal <c>new</c>: the
/// persistence dependencies are on their way out (T-005-01 / plan decision D1) and these tests are about the
/// two gates, not about the constructor's parameter list. The repository and unit-of-work doubles are still
/// handed over so the harness can satisfy whichever constructor the handler currently declares.
/// </para>
/// </summary>
public sealed class CheckHeappeHandlerTests
{
    // The step comes from the shared harness, which runs the real endpoint: T-055-04 changes this record's
    // members, and a literal constructor call here would stop compiling the moment that lands.
    private static Task<CheckHeappeStep> BuildStepAsync()
        => CheckHeappeHandlerHarness.BuildStepAsync(EAllocationUnitType.NodeHours);

    private static ResourceAggregate ExistingResource() => ResourceAggregate.Create(
       ResourceName.Create(CreateResourceFlowStepFactory.ResourceNameValue),
       Core.Domain.Provider.Provider.Create(ProviderName.From(CreateResourceFlowStepFactory.ProviderNameValue), countryId: 1),
       UserId.From(Guid.NewGuid()),
       CreateResourceFlowStepFactory.Start, CreateResourceFlowStepFactory.End,
       description: "old",
       primaryInvestigatorEmail: Email.Create("old@example.com"), externalId: ExternalId.Create("ext-1"));

    private static Mock<IRepository<ResourceAggregate, ProjectResourceKey>> Repo(ResourceAggregate? resource)
    {
        var repo = new Mock<IRepository<ResourceAggregate, ProjectResourceKey>>();
        repo.Setup(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(resource));
        return repo;
    }

    private static Mock<IUnitOfWork> SavingUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    [Fact]
    public async Task Should_ReturnNullData_When_AllInstancesAliveAndAllocationUnitTypeExists()
    {
        // Arrange
        var step = await BuildStepAsync();

        var livenessProbe = new Mock<IHeappeLivenessProbe>();
        livenessProbe.Setup(l => l.IsInstanceAliveAsync(It.IsAny<HeappeIdentifier>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var assignments = new Mock<IResourceAssignmentQuery>();
        assignments.Setup(a => a.AllocationUnitTypeExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

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

        var handler = CheckHeappeHandlerHarness.CreateHandler(
            livenessProbe.Object, assignments.Object, Repo(ExistingResource()).Object, SavingUnitOfWork().Object, locationQuery.Object);

        // Act
        var result = await handler.HandleAsync(step, CancellationToken.None);

        // Assert
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Should_ThrowHeappeUnreachable_When_LivenessProbeReportsInstanceNotAlive()
    {
        // Arrange
        var step = await BuildStepAsync();

        var livenessProbe = new Mock<IHeappeLivenessProbe>();
        livenessProbe.Setup(l => l.IsInstanceAliveAsync(It.IsAny<HeappeIdentifier>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // The allocation-unit-type gate sits after the liveness gate; the handler must never reach it here.
        var assignments = new Mock<IResourceAssignmentQuery>();
        assignments.Setup(a => a.AllocationUnitTypeExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var handler = CheckHeappeHandlerHarness.CreateHandler(
            livenessProbe.Object, assignments.Object, Repo(ExistingResource()).Object, SavingUnitOfWork().Object);

        // Act + Assert
        var exception = await Assert.ThrowsAsync<RetryableException>(
            () => handler.HandleAsync(step, CancellationToken.None));

        Assert.Equal("HEAPPE_UNREACHABLE", exception.ErrorInfo.ErrorCode);
        assignments.Verify(a => a.AllocationUnitTypeExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ThrowAllocationUnitTypeNotFound_When_AllocationUnitTypeDoesNotExist()
    {
        // Arrange
        var step = await BuildStepAsync();

        var livenessProbe = new Mock<IHeappeLivenessProbe>();
        livenessProbe.Setup(l => l.IsInstanceAliveAsync(It.IsAny<HeappeIdentifier>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var assignments = new Mock<IResourceAssignmentQuery>();
        assignments.Setup(a => a.AllocationUnitTypeExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

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

        var handler = CheckHeappeHandlerHarness.CreateHandler(
            livenessProbe.Object, assignments.Object, Repo(ExistingResource()).Object, SavingUnitOfWork().Object, locationQuery.Object);

        // Act + Assert — a missing allocation-unit-type is not transient (retrying won't make it appear),
        // so it is a PermanentException, unlike the HEAPPE_UNREACHABLE liveness gate above.
        var exception = await Assert.ThrowsAsync<PermanentException>(
            () => handler.HandleAsync(step, CancellationToken.None));

        Assert.Equal("ALLOCATION_UNIT_TYPE_NOT_FOUND", exception.ErrorInfo.ErrorCode);
    }
}
