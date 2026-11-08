using Heappe.Persistence.Enums;

using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

namespace Heappe.Persistence.Entities;

internal class ProjectCommandTemplatePartition : AuditableEntity<Guid>
{
    // ponytail: Id generated here (not by the DB default NewId()) so it is known before SaveChangesAsync.
    internal ProjectCommandTemplatePartition() => Id = Guid.CreateVersion7();

    internal long? CommandTemplateId { get; set; }
    internal ECommandTemplateState State { get; set; } = ECommandTemplateState.Unknown;
    internal Guid LocationPartitionId { get; set; }
    internal LocationPartition LocationPartition { get; set; }
    internal ProjectCommandTemplate ProjectCommandTemplate { get; set; }

}