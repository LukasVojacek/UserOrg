using Core.Features.Resource.Workflow.Steps;

using Heappe.Contracts;

using UserOrg.Shared;

using Microsoft.Extensions.Logging;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;

namespace Core.Features.Resource.Workflow.Handlers;

internal sealed class CheckHeappeHandler : IStepHandler<CheckHeappeStep>
{
    private readonly IHeappeLivenessProbe _livenessProbe;
    private readonly IResourceAssignmentQuery _assignments;
    private readonly ILogger<CheckHeappeHandler> _logger;
    private readonly ILocationQuery _locations;

    public CheckHeappeHandler(IHeappeLivenessProbe livenessProbe, IResourceAssignmentQuery assignments, ILogger<CheckHeappeHandler> logger, ILocationQuery locations)
    {
        _livenessProbe = livenessProbe;
        _assignments = assignments;
        _logger = logger;
        _locations = locations;
    }

    public async Task<StepResult> HandleAsync(CheckHeappeStep request, CancellationToken ct)
    {
        // HEAppE liveness gate (ADR-0050 §4): every resolved location's HEAppE instance must answer its health
        // probe before anything is created. Unlike a missing location name, ALL unreachable instances are
        // collected and reported together — retrying the whole batch N times against a live external dependency
        // is expensive, so the caller should see every problem in one response. A location whose stored
        // Endpoint isn't even a well-formed URI (never validated as one at Location.Create/UpdateDetails) is
        // treated the same as unreachable rather than throwing.
        //var livenessResults = await Task.WhenAll(request.ResolvedLocations.Select(async location =>
        //    (Location: location, Alive: Uri.TryCreate(location.Endpoint, UriKind.Absolute, out var endpointUri)
        //        && await _livenessProbe.IsInstanceAlive(endpointUri, ct))));

        var livenessResults = await Task.WhenAll(request.ResolvedLocations.Where(w => w.LocationType == Domain.Shared.ELocationType.HPC).Select(async r =>
        {
            var location = await _locations.GetLocationByProviderAndNameAsync(
                request.ProviderName,
                LocationName.Create(r.LocationName),
                null,
                ct);

            if (location is null)
            {
                return (Location: r, Alive: false);
            }

            var alive = HeappeIdentifier.TryParse(r.heappeIdentifier, out var identifier)
                && await _livenessProbe.IsInstanceAliveAsync(identifier, ct);

            return (Location: r, Alive: alive);
        }));

        var unreachable = livenessResults
            .Where(r => !r.Alive)
            .Select(r => $"'{r.Location.LocationName}' ({r.Location.Endpoint})")
            .ToArray();
        if (unreachable.Length > 0)
        {
            _logger.LogWarning("One or more HEAppE instances did not answer their health probe: {UnreachableInstances}", string.Join(", ", unreachable));
            throw new RetryableException("HEAPPE_UNREACHABLE", $"One or more HEAppE instances did not answer their health probe: {string.Join(", ", unreachable)}");
        }
        // T-055-04: the unit types come from the ASSIGNMENTS, not from the resolved location refs. A ref is
        // (LocationName, Endpoint) only, and the same location assigned twice under different aggregations
        // (ADR-0050 §3) yields two refs that are equal by value — looking the assignment up per ref would
        // resolve both to the first one and silently stop checking the second unit type.
        //
        // Distinct: the same AllocationUnitTypeId commonly repeats across assignments in one request, so check
        // each id at most once. The checks below are serialised, unlike the liveness probes above —
        // IResourceAssignmentQuery is composed of scoped IDataQuery<T> instances that all share one scoped
        // DatabaseContext, which cannot run more than one operation concurrently.
        var distinctAllocationUnitTypeIds = request.Data.AssignedLocations.Select(a => (int)a.AllocationUnitType).Distinct().ToArray();
        foreach (var id in distinctAllocationUnitTypeIds)
        {
            var exists = await _assignments.AllocationUnitTypeExistsAsync(id, ct);
            if (!exists)
            {
                _logger.LogWarning("Allocation unit type '{MissingAllocationUnitTypeId}' does not exist.", id);
                throw new PermanentException("ALLOCATION_UNIT_TYPE_NOT_FOUND", $"Allocation unit type '{id}' does not exist.");
            }
        }

        return new StepResult(null);
    }
}