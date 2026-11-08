using System.Diagnostics.CodeAnalysis;

using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

namespace UserOrganizationManagement.Domain.Entities;

[SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "<Pending>")]
public class Configuration : AuditableEntity<Guid>
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}