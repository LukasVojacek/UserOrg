using System.Collections.Frozen;

using Heappe.Persistence.Enums;

using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

namespace Heappe.Persistence.Entities;

internal class ProjectCommandTemplate : PrimaryIdentifiableEntity<Guid>
{
    // ponytail: Id generated here (not by the DB default NewId()) so it is known before SaveChangesAsync.
    internal ProjectCommandTemplate() => Id = Guid.CreateVersion7();

    internal string Name { get; private set; }
    internal ECommandTemplateType Type { get; set; } = ECommandTemplateType.Unknown;
    internal ECommandTemplateState State => GetCommandTemplateState(FrozenSet<Guid>.Empty);
    internal CommandTemplateDefinition? CommandTemplateDefinition { get; set; }
    internal Guid? CommandTemplateDefinitionId { get; set; }
    internal Guid ProjectId { get; private set; }
    internal Guid ProjectResourceId { get; private set; }
    internal IList<ProjectCommandTemplatePartition> ProjectCommandTemplatePartitions { get; init; } = [];


    internal static ProjectCommandTemplate Create(string name, ECommandTemplateType type, Guid projectId, Guid projectResourceId)
    {
        return new ProjectCommandTemplate()
        {
            Name = name,
            Type = type,
            CommandTemplateDefinition = default,
            ProjectId = projectId,
            ProjectResourceId = projectResourceId
        };
    }

    internal static ProjectCommandTemplate CreateWithDefinition(string name, ECommandTemplateType type, Guid projectId, Guid projectResourceId, Guid CommandTemplateDefinitionId)
    {
        return new ProjectCommandTemplate()
        {
            Name = name,
            Type = type,
            CommandTemplateDefinitionId = CommandTemplateDefinitionId,
            ProjectId = projectId,
            ProjectResourceId = projectResourceId
        };
    }

    /// <summary>
    /// Rolls up this template's partition states into one flattened state.
    /// </summary>
    /// <param name="killedPartitionIds">Partition ids sitting on a frozen (killed, non-whitelisted)
    /// location. An Active partition among these rolls up as Inactive — a killed partition cannot
    /// launch, so it degrades the state rather than merely flagging it (unlike expiration, which is
    /// OR'd on without changing Active/Inactive).</param>
    internal ECommandTemplateState GetCommandTemplateState(IReadOnlySet<Guid> killedPartitionIds)
    {
        var states = ProjectCommandTemplatePartitions
            .Select(a => a.State == ECommandTemplateState.Active && killedPartitionIds.Contains(a.Id)
                ? ECommandTemplateState.Inactive
                : a.State)
            .ToList();

        return states switch
        {
            [] => ECommandTemplateState.Unknown,
            _ when states.All(s => s == ECommandTemplateState.Active) => ECommandTemplateState.Active,
            _ when states.All(s => s == ECommandTemplateState.Inactive) => ECommandTemplateState.Inactive,
            _ when states.Any(s => s is ECommandTemplateState.Unknown or ECommandTemplateState.Pending) => ECommandTemplateState.Pending,
            _ => ECommandTemplateState.Unknown
        };
    }
}