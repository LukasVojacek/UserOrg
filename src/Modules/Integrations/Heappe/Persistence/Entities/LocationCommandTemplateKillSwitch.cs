namespace Heappe.Persistence.Entities;

/// <summary>
/// One row per Location: whether HEAppE command-template launches on that Location are
/// currently frozen (ADR-0077). Last-write-wins — enforcing/lifting overwrites this row,
/// it is never appended to.
/// </summary>
internal sealed class LocationCommandTemplateKillSwitch
{
    internal Guid LocationId { get; set; }
    internal bool IsEnforced { get; set; }

    /// <summary>
    /// Lockdown: while enforced, the Location's whitelist is ignored rather than deleted
    /// (ADR-0013 §3). Defaults to <see langword="false" />; a lift always resets it.
    /// </summary>
    internal bool IgnoreWhitelist { get; set; }

    internal string Reason { get; set; } = string.Empty;
    internal DateTime EnforcedAtUtc { get; set; }
    internal Guid EnforcedByUserId { get; set; }
    internal DateTime? LiftedAtUtc { get; set; }
    internal Guid? LiftedByUserId { get; set; }
}