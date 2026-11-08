namespace Heappe.Persistence.Entities;

/// <summary>
/// A command template exempted from a Location's kill-switch (ADR-0077 §1, relationship-map §D).
/// Stored by internal id (<see cref="ProjectCommandTemplate"/> id) rather than by name: the
/// module's read paths only ever hold GUIDs on a partition, so matching stays in-module with no
/// Core call in the hot enforcement path. The API contract resolves names to this id once, at
/// PUT time. Covers all of the template's aggregation partitions on the Location.
/// </summary>
internal sealed class LocationCommandTemplateWhitelist
{
    internal Guid LocationId { get; set; }
    internal Guid ProjectCommandTemplateId { get; set; }

    /// <summary>
    /// Who added this exception. Approving an exception is as security-relevant as lifting the
    /// switch for one template and must leave a trace (ADR-0013 §1).
    /// </summary>
    internal Guid AddedByUserId { get; set; }

    /// <summary>
    /// When this exception was added (UTC). Set by the whitelist write path on every insert;
    /// replace semantics mean a re-added row is stamped afresh (ADR-0013 §1).
    /// </summary>
    internal DateTime AddedAtUtc { get; set; }
}