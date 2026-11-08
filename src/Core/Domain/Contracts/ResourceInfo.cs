namespace UserOrganizationManagement.Domain.Contracts;


public record ResourceInfo
(
    Guid Id,
    string Name,
    string? Description,
    DateTime? StartDate,
    DateTime? EndDate,
    List<LocationAllocationInfo> LocationAllocations
);