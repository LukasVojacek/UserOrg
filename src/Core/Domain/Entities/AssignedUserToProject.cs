using UserOrg.Shared.Persistance.Abstractions.Interfaces;

using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.Entities;

public class AssignedUserToProject : IAuditableEntity
{
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public bool IsDeleted { get; set; }
    public Project? Project { get; set; }
    public ProjectId ProjectId { get; set; }
    public User? User { get; set; }
    public User? AssignedByUser { get; set; }
}