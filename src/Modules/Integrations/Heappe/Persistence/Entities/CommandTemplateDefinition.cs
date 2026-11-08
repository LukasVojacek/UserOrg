using Heappe.Persistence.Enums;

using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

namespace Heappe.Persistence.Entities;

/// <summary>
/// Defines a command template that can be used for creating command templates in HEAppE.
/// </summary>
/// <remarks>
/// The <c>ExecutionCommand</c> and <c>PreparationCommand</c> properties must contain
/// the COMPLETE path to the script/command, including any base directory or path prefix.
/// The service does NOT construct paths - it uses these values as-is when creating templates in HEAppE.
/// 
/// Example: If the base scripts directory is "/scripts" and the script name is "jobscript.sh",
/// then ExecutionCommand should be "/scripts/jobscript.sh", not just "jobscript.sh".
/// </remarks>
internal class CommandTemplateDefinition : AuditableEntity<Guid>
{
    // ponytail: Id generated here (not by the DB default NewId()) so it is known before SaveChangesAsync.
    internal CommandTemplateDefinition() => Id = Guid.CreateVersion7();

    internal string Name { get; set; }
    internal string? Description { get; set; }
    internal ECommandTemplateType Type { get; set; } = ECommandTemplateType.Unknown;
    internal IList<CommandTemplateParameterDefinition> ComandTemplateParameterDefinitions { get; init; } = [];
    internal IList<ProjectCommandTemplate> ProjectCommandTemplates { get; init; } = [];
    internal IList<CommandTemplateLocationDefinition> CommandTemplateLocationDefinitions { get; init; } = [];
}