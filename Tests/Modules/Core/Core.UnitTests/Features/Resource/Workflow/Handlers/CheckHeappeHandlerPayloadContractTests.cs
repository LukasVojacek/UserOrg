using Core.DataAccess.Queries.Contracts;
using Core.Features.Resource.Workflow.Handlers;

using Heappe.Contracts;

using Moq;

using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using WorkflowEngine.Contracts.Exceptions;

using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.Features.Resource.Workflow.Handlers;

/// <summary>
/// Characterization of everything <see cref="CheckHeappeHandler"/> reads out of its payload, pinned from the
/// outside so the T-055-04 shape change cannot quietly alter it.
/// <para>
/// The gate consumes exactly three things per resolved location — the endpoint it probes, the name it names in
/// the HEAPPE_UNREACHABLE report, and the allocation unit type it verifies (CheckHeappeHandler.cs:35-52). Once
/// the payload carries <c>(LocationName, Endpoint)</c> refs instead of <c>(Location, LocationAssignment)</c>
/// tuples, the unit type is no longer sitting next to the location and has to be looked up in
/// <c>request.Data.AssignedLocations</c>. These tests are green today and must stay green: they are the
/// contract that lookup has to satisfy.
/// </para>
/// </summary>
public sealed class CheckHeappeHandlerPayloadContractTests
{
    [Fact]
    public async Task Should_ProbeEveryResolvedLocationsHeappeIdentifier_When_TheGateRuns()
    {
        var step = await CheckHeappeHandlerHarness.BuildStepAsync(EAllocationUnitType.NodeHours, EAllocationUnitType.CoreHours);
        var probe = new RecordingLivenessProbe(alive: true);

        var result = await Handler(probe, ExistingUnitTypes(), out _)
            .HandleAsync(step, TestContext.Current.CancellationToken);

        Assert.Null(result.Data);
        Assert.Equal(
            ["heappe-1", "heappe-2"],
            probe.Probed.Select(identifier => identifier.ToString()).Order().ToArray());
    }

    [Fact]
    public async Task Should_NameTheLocationAndItsEndpoint_When_AnInstanceDoesNotAnswer()
    {
        // Both fields the DTO has to carry are visible in this one message; a ref that dropped either would
        // degrade the operator-facing report to a guess.
        var step = await CheckHeappeHandlerHarness.BuildStepAsync(EAllocationUnitType.NodeHours);

        var exception = await Assert.ThrowsAsync<RetryableException>(
            () => Handler(new RecordingLivenessProbe(alive: false), ExistingUnitTypes(), out _)
                .HandleAsync(step, TestContext.Current.CancellationToken));

        Assert.Equal("HEAPPE_UNREACHABLE", exception.ErrorInfo.ErrorCode);
        Assert.Contains("'location-1' (https://heappe-1.example.test)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_CheckBothUnitTypes_When_TheSameLocationIsAssignedTwiceWithDifferentUnitTypes()
    {
        // The trap in the new shape. Two allocations on ONE location with two different unit types produce two
        // IDENTICAL location refs, so a per-ref lookup along the lines of
        // AssignedLocations.First(a => a.LocationName == ref.LocationName) resolves both refs to the first
        // assignment and silently stops verifying the second unit type — an unknown allocation unit type would
        // then reach InitializeProjectResource instead of failing this gate.
        var request = CreateResourceFlowStepFactory.RequestFor(
            CreateResourceFlowStepFactory.Assignment(aggregationName: "agg-a", allocationUnitType: EAllocationUnitType.NodeHours),
            CreateResourceFlowStepFactory.Assignment(aggregationName: "agg-b", allocationUnitType: EAllocationUnitType.CoreHours));

        var step = await CreateResourceFlowStepFactory.InitialStepAsync(CreateResourceFlowStepFactory.ProviderWith(), request);

        await Handler(new RecordingLivenessProbe(alive: true), ExistingUnitTypes(), out var queriedUnitTypeIds)
            .HandleAsync(step, TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { (int)EAllocationUnitType.NodeHours, (int)EAllocationUnitType.CoreHours }.Order().ToArray(),
            queriedUnitTypeIds.Order().ToArray());
    }

    private static CheckHeappeHandler Handler(
        IHeappeLivenessProbe probe,
        Mock<IResourceAssignmentQuery> assignments,
        out List<int> queriedUnitTypeIds)
    {
        var queried = new List<int>();
        assignments
            .Setup(a => a.AllocationUnitTypeExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, CancellationToken>((id, _) => queried.Add(id))
            .ReturnsAsync(true);

        queriedUnitTypeIds = queried;

        var locationQuery = new Mock<ILocationQuery>();
        // Handler needs locations to return something, so it doesn't fail the liveness gate check.
        // Return location based on the LocationName parameter so different locations have different endpoints.
        locationQuery.Setup(q => q.GetLocationByProviderAndNameAsync(
                It.IsAny<ProviderName>(),
                It.IsAny<LocationName>(),
                It.IsAny<IReadOnlyList<ProjectShortName>>(),
                It.IsAny<CancellationToken>()))
            .Returns<ProviderName, LocationName, IReadOnlyList<ProjectShortName>, CancellationToken>((provider, name, scope, ct) =>
            {
                // Return location based on location name to handle multiple distinct locations
                var locationName = name.ToString();
                var index = locationName.Contains("location-2") ? 2 : 1;
                var location = new LocationSummary(
                    Name: $"location-{index}",
                    Description: null,
                    Endpoint: $"https://heappe-{index}.example.test",
                    IsPrivate: false,
                    IsShared: true,
                    LocationType: Core.Domain.Shared.ELocationType.HPC,
                    ProviderName: provider.ToString(),
                    Mode: Core.Domain.Shared.EOperationMode.ReadOnly,
                    DefaultServiceIntegrationUrl: $"https://heappe-{index}.example.test");
                return Task.FromResult<LocationSummary?>(location);
            });

        return CheckHeappeHandlerHarness.CreateHandler(
            probe,
            assignments.Object,
            new Mock<IRepository<ResourceAggregate, ProjectResourceKey>>().Object,
            new Mock<IUnitOfWork>().Object,
            locationQuery.Object);
    }

    private static Mock<IResourceAssignmentQuery> ExistingUnitTypes() => new();

    /// <summary>A liveness probe that records every endpoint it was asked about.</summary>
    private sealed class RecordingLivenessProbe(bool alive) : IHeappeLivenessProbe
    {
        private readonly List<HeappeIdentifier> _probed = [];

        internal IReadOnlyList<HeappeIdentifier> Probed => [.. _probed];

        public Task<bool> IsInstanceAliveAsync(HeappeIdentifier identifier, CancellationToken cancellationToken)
        {
            _probed.Add(identifier);
            return Task.FromResult(alive);
        }
    }
}
