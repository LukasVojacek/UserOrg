using UserOrg.Shared.Persistance.Abstractions.Enums;

namespace UserOrganizationManagement.Domain.Contracts;

public sealed record LocationAllocationInfo
(
    Guid LocationId,
    string LocationName,
    string LoginEndpoint,
    ELocationType LocationType,
    string? AggregationName,
    HeappeInstance HeappeInstance
);