using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

namespace UserOrganizationManagement.Domain.Entities;

public class ProjectTag : AuditableEntity<Guid>
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public IList<Project>? Projects { get; init; }
    public User? CreatedByUser { get; set; }
}