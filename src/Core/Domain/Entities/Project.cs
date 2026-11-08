using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.Entities;

public class Project : AuditableExtendedServiceIntegrationEntity<ProjectId>
{
    public string? Name { get; set; }
    public ProjectShortName ShortName { get; set; }
    public string? Description { get; set; }
    public string? Email { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public IList<ProjectTag> ProjectTags { get; private set; } = [];
    public ProjectDomainType? ProjectDomainType { get; set; }
    public User? CreatedByUser { get; set; }
    public IList<AssignedProjectResourceToProject> AssignedProjectResources { get; private set; } = [];
    public IList<AssignedUserToProject> AssignedUsers { get; private set; } = [];
    // TODO In future this replace table AssignedUserToProject
    //public IList<UserProjectAccess> AssignedUsers { get; private set; } = [];
    public IList<UserPermission> UserPermissions { get; private set; } = [];
}