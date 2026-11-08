using System.Collections.Immutable;

using UserOrg.Shared.Hashing;

namespace Waldur.Contracts.Payloads;

/// <summary>
/// A project's marketplace resource (embedded in <see cref="ProjectPayload"/> — resources have no
/// independent lifecycle on our side, ADR-0054). Upsert key is <c>(Name, ServiceName)</c> where
/// <see cref="Name"/> is <c>EffectiveId ?? BackendId</c> after grouping by
/// <c>(EffectiveId, BackendId, Offering.CustomerId)</c>; the <see cref="ResourceExternalId"/>
/// (Waldur resource UUID) is the assigned-location upsert key.
/// </summary>
/// <param name="Name">Resource name: <c>EffectiveId ?? BackendId</c> (with the deucalion trim applied in mapping).</param>
/// <param name="ResourceExternalId">Waldur resource UUID (canonical lowercase "D" form).</param>
/// <param name="Description">First resource description in the group (truncated in mapping).</param>
/// <param name="StartDate">Earliest <c>created</c> across the group (null-fallback applied in mapping).</param>
/// <param name="EndDate">Latest <c>end_date</c> across the group (<c>?? MaxValue</c> in mapping).</param>
/// <param name="IsDeleted">True when every resource in the group is terminated.</param>
/// <param name="Endpoint">Normalized SSH-login endpoint of the resource's offering.</param>
/// <param name="AggregationName">Offering name.</param>
/// <param name="AllocationUnitType">Mapped allocation-unit type name, if any.</param>
/// <param name="AllocationUnitCount">Converted allocation amount (node/core-hours → Userorg units), if any.</param>
/// <param name="HeappeUri">Config-derived HEAppE base URI for the resource's provider (HPC only).</param>
/// <param name="HeappeIdentifier">Config-derived HEAppE identifier for the resource's provider (HPC only).</param>
/// <param name="Values">Leftover allocation values + the derived <c>HEAppELocalBasePath</c> entry.</param>
/// <param name="CreatedAt">Audit timestamp — travels for create only, never hashed (A1).</param>
/// <param name="ModifiedAt">Audit timestamp — travels for write only, never hashed (A1).</param>
[GenerateIdentityHash]
public sealed partial record ResourcePayload(
    string Name,
    string ResourceExternalId,
    string? Description,
    DateTime StartDate,
    DateTime EndDate,
    bool IsDeleted,
    string Endpoint,
    string AggregationName,
    string? AllocationUnitType,
    long? AllocationUnitCount,
    string? HeappeUri,
    string? HeappeIdentifier,
    IImmutableList<ResourceAllocationValuePayload> Values)
{
    [HashIgnore]
    public DateTime? CreatedAt { get; init; }

    [HashIgnore]
    public DateTime? ModifiedAt { get; init; }
}