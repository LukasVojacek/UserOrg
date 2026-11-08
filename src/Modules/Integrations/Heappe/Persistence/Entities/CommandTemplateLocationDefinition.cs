using UserOrg.Shared.Persistance.Abstractions.BaseEntities;
using UserOrg.Shared.Persistance.Abstractions.Enums;

namespace Heappe.Persistence.Entities;

internal sealed class CommandTemplateLocationDefinition : AuditableEntity<Guid>
{
    // ponytail: Id generated here (not by the DB default NewId()) so it is known before SaveChangesAsync.
    internal CommandTemplateLocationDefinition() => Id = Guid.CreateVersion7();

    /// <summary>
    /// The complete path to the preparation command/script.
    /// Must include the full path (e.g., "/scripts/prepare.sh"), not just the filename.
    /// </summary>
    internal string? PreparationCommand { get; set; }

    /// <summary>
    /// The complete path to the execution command/script.
    /// Must include the full path (e.g., "/scripts/jobscript.sh"), not just the filename.
    /// </summary>
    internal string? ExecutionCommand { get; set; }
    internal ELocationType LocationType { get; set; } = ELocationType.Unspecified;

    internal CommandTemplateDefinition CommandTemplateDefinition { get; set; }
    internal Guid CommandTemplateDefinitionId { get; set; }

    internal Guid? LocationPartitionId { get; set; }
}