using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.Contracts;

public sealed record ProjectAllocationInfo
(
    ProjectId Id,
    ProjectShortName ShortName,
    IReadOnlyList<ResourceAllocationInfo> ResourceAllocations
);