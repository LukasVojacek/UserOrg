using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

namespace UserOrganizationManagement.Domain.Entities;

public class UserPreference : AuditableEntity<Guid>
{
    public User? User { get; set; }
    public SystemModule? SystemModule { get; set; }
    public string? Data { get; set; }
}