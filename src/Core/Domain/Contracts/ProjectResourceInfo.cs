using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.Contracts;

public record ProjectResourceInfo
(
    ProjectId Id,
    string Name,
    ProjectShortName ShortName,
    string? Description,
    DateTime StartDate,
    DateTime EndDate,
    IList<ResourceInfo> Resources
);