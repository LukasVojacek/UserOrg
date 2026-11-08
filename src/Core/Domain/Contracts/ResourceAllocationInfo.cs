namespace UserOrganizationManagement.Domain.Contracts;

public sealed record ResourceAllocationInfo
(
    Guid Id,
    string Name,
    IReadOnlyList<LocationAllocationInfo> Locations
);