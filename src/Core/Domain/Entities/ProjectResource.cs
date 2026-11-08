using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

using UserOrganizationManagement.Domain.Enums;

namespace UserOrganizationManagement.Domain.Entities;

public class ProjectResource : AuditableExtendedServiceIntegrationEntity<Guid>
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? PrimaryInvestigatorEmail { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public Guid? ProviderId { get; set; }
    public IList<AssignedProjectResourceToProject> AssignedProjects { get; init; } = [];
    public IList<UserProjectAccess> AssignedUsers { get; init; } = [];
    public IList<AssignedLocation> AssignedLocations { get; init; } = [];
}