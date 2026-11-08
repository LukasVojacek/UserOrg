using Core.DataAccess.Queries.Contracts;
using Core.Features.Resource.Workflow.Handlers;
using Core.Features.Resource.Workflow.Steps;

using Heappe.Contracts;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using static Core.Features.Resource.CreateResourceOnLocation.CreateResourceOnLocationEndpoint;

using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.Features.Resource.Workflow.Handlers;

/// <summary>
/// Shared construction for <see cref="CheckHeappeHandler"/> tests. The handler is built through
/// <see cref="ActivatorUtilities"/> rather than a literal <c>new</c> on purpose: T-005-01 removes the
/// <see cref="IRepository{TAggregate,TKey}"/> and <see cref="IUnitOfWork"/> constructor dependencies, and a
/// test that hard-codes the current parameter list would stop compiling the moment the fix lands. The
/// persistence doubles stay registered so they are available before the fix and simply go unused after it —
/// that "never resolved, never called" is itself the contract the gate must keep (ADR-0050 §4).
/// </summary>
internal static class CheckHeappeHandlerHarness
{
    internal static CheckHeappeHandler CreateHandler(
        IHeappeLivenessProbe livenessProbe,
        IResourceAssignmentQuery assignments,
        IRepository<ResourceAggregate, ProjectResourceKey> repository,
        IUnitOfWork unitOfWork,
        ILocationQuery? locationQuery = null,
        ILogger<CheckHeappeHandler>? logger = null)
    {
        var provider = new DoubleProvider(new Dictionary<Type, object>
        {
            [typeof(IHeappeLivenessProbe)] = livenessProbe,
            [typeof(IResourceAssignmentQuery)] = assignments,
            [typeof(IRepository<ResourceAggregate, ProjectResourceKey>)] = repository,
            [typeof(IUnitOfWork)] = unitOfWork,
            [typeof(ILocationQuery)] = locationQuery ?? new Mock<ILocationQuery>().Object,
            [typeof(ILogger<CheckHeappeHandler>)] = logger ?? NullLogger<CheckHeappeHandler>.Instance,
        });

        return ActivatorUtilities.CreateInstance<CheckHeappeHandler>(provider);
    }

    /// <summary>The smallest thing <see cref="ActivatorUtilities"/> needs: a lookup of test doubles by type.</summary>
    private sealed class DoubleProvider(Dictionary<Type, object> doubles) : IServiceProvider
    {
        public object? GetService(Type serviceType) => doubles.GetValueOrDefault(serviceType);
    }

    /// <summary>
    /// A <see cref="CheckHeappeStep"/> with one resolved location per supplied allocation unit type. Each
    /// location gets its own name and endpoint so the liveness gate sees distinct instances.
    /// <para>
    /// Built by running the REAL endpoint (<see cref="CreateResourceFlowStepFactory"/>) rather than by calling
    /// the record's constructor: T-055-04 replaces the step's <c>Provider</c> and tuple-valued
    /// <c>ResolvedLocations</c> members, and a literal constructor call here would stop compiling the moment
    /// that lands — taking every test in this folder with it. Going through the endpoint also means the gate is
    /// exercised against the payload production really produces.
    /// </para>
    /// </summary>
    internal static Task<CheckHeappeStep> BuildStepAsync(params EAllocationUnitType[] allocationUnitTypes)
    {
        var unitTypes = allocationUnitTypes.Length > 0
            ? allocationUnitTypes
            : [EAllocationUnitType.NodeHours];

        var locations = unitTypes
            .Select((_, index) => ($"location-{index + 1}", $"https://heappe-{index + 1}.example.test"))
            .ToArray();

        var assignments = unitTypes
            .Select((unitType, index) => CreateResourceFlowStepFactory.Assignment(
                locationName: locations[index].Item1,
                allocationUnitType: unitType,
                specifications: [new("HEAPPE_IDENTIFIER", $"heappe-{index + 1}")]))
            .ToArray();

        return CreateResourceFlowStepFactory.InitialStepAsync(
            CreateResourceFlowStepFactory.ProviderWith(locations),
            CreateResourceFlowStepFactory.RequestFor(assignments));
    }

    /// <summary>A liveness probe that reports every instance alive.</summary>
    internal static IHeappeLivenessProbe AliveProbe()
    {
        var probe = new Mock<IHeappeLivenessProbe>();
        probe.Setup(p => p.IsInstanceAliveAsync(It.IsAny<HeappeIdentifier>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return probe.Object;
    }

    /// <summary>An assignment query for which every allocation unit type exists.</summary>
    internal static IResourceAssignmentQuery ExistingAllocationUnitTypes()
    {
        var assignments = new Mock<IResourceAssignmentQuery>();
        assignments.Setup(a => a.AllocationUnitTypeExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return assignments.Object;
    }
}
