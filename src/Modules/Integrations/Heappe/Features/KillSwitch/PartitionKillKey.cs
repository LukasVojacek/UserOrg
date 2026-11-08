namespace Heappe.Features.KillSwitch;

/// <summary>
/// The in-module identity of a command-template partition for kill-switch evaluation: the
/// Location it sits on (via <c>LocationPartition.LocationId</c>) and the
/// <c>ProjectCommandTemplate</c> it belongs to. These are the only two GUIDs the kill rule
/// needs, both already carried by a partition — so the decision stays in-module with no
/// Core/name lookup (ADR-0077 §2, relationship-map §D).
/// </summary>
/// <param name="LocationId">The Location the partition sits on.</param>
/// <param name="ProjectCommandTemplateId">The command template the partition belongs to.</param>
internal readonly record struct PartitionKillKey(Guid LocationId, Guid ProjectCommandTemplateId);