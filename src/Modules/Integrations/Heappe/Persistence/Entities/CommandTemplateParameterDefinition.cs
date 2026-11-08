using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

namespace Heappe.Persistence.Entities;

internal class CommandTemplateParameterDefinition : AuditableEntity<Guid>
{
    // ponytail: Id generated here (not by the DB default NewId()) so it is known before SaveChangesAsync.
    internal CommandTemplateParameterDefinition() => Id = Guid.CreateVersion7();

    internal string Name { get; set; }
    internal string? Description { get; set; }
    internal CommandTemplateDefinition CommandTemplateDefinition { get; set; }
}