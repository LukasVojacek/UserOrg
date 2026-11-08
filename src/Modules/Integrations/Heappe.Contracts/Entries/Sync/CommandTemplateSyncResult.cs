namespace Heappe.Contracts.Entries.Sync;

/// <summary>
/// Aggregate outcome of a command template synchronization run across all of a
/// project resource's location assignments.
/// </summary>
/// <param name="Locations">The per-location synchronization outcomes.</param>
public sealed record CommandTemplateSyncResult(
    IReadOnlyList<LocationSyncResultEntry> Locations)
{
    /// <summary>
    /// Whether every location in this run succeeded.
    /// </summary>
    public bool AllSucceeded => Locations.Count > 0 && Locations.All(l => l.IsSuccess);

    /// <summary>
    /// Whether at least one location in this run succeeded.
    /// </summary>
    public bool AnySucceeded => Locations.Any(l => l.IsSuccess);
}