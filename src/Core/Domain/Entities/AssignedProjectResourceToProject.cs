using UserOrg.Shared.Persistance.Abstractions.Interfaces;

using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.Entities;

public class AssignedProjectResourceToProject : IAuditableEntity
{
    public Project? Project { get; set; }
    public ProjectId ProjectId { get; set; }
    public ProjectResource? ProjectResource { get; set; }
    public Guid ProjectResourceId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public bool IsDeleted { get; set; }
}