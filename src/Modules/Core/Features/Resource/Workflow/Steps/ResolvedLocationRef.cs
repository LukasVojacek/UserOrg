using Core.Domain.Provider;
using Core.Domain.Shared;

namespace Core.Features.Resource.Workflow.Steps;

/// <summary>
/// A location the create-resource endpoint already resolved against the provider aggregate, reduced to the two
/// fields the flow's HEAppE liveness gate actually consumes: the natural key it names in its report, and the
/// endpoint it probes (ADR-0029 — the natural key crosses the boundary, not the <see cref="Location"/> entity).
/// <para>
/// One ref per ASSIGNMENT, not per distinct location: the same location may legitimately be assigned twice under
/// different aggregations (ADR-0050 §3), and the gate reports per resolved location. Two such refs are then equal
/// by value, so nothing may be looked up "per ref" in <c>Data.AssignedLocations</c> — see
/// <c>CheckHeappeHandler</c>, which derives the allocation unit types from the assignments themselves.
/// </para>
/// </summary>
internal sealed record ResolvedLocationRef(string LocationName, ELocationType LocationType, string Endpoint, string? heappeUrl, string? heappeIdentifier = null);