using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

namespace UserOrganizationManagement.Domain.Entities;

#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public class UserPermission : AuditableEntity<Guid>
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    public Project? Project { get; set; }
    public User? User { get; set; }
    public int? SystemRoleId { get; set; }
}