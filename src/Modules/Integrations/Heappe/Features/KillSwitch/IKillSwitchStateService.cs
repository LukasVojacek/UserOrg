namespace Heappe.Features.KillSwitch;

/// <summary>
/// The single place the kill-switch degrade rule (ADR-0077 §2, ADR-0086 §3) is expressed: a
/// command-template partition is effectively <em>killed</em> when its Location has an enforced
/// kill-switch AND either that kill-switch is under lockdown (<c>IgnoreWhitelist</c>) or the
/// <c>(LocationId, ProjectCommandTemplateId)</c> pair is not whitelisted. Effective
/// <c>Enabled = State == Active &amp;&amp; !killed</c>. All three read paths (B04–B06) and the
/// write/sync guards (B07/B08) consume this service instead of duplicating the rule, so they
/// inherit lockdown for free (ADR-0086 §3). A second copy of the rule anywhere is a defect.
/// </summary>
/// <remarks>
/// Reads only from the <c>heappe_integration</c> schema (the two B01 tables); it never writes and
/// never calls Core. Both queries are batch-shaped (constant round-trips regardless of set size)
/// because every read path iterates partitions.
/// </remarks>
internal interface IKillSwitchStateService
{
    /// <summary>
    /// Returns the subset of <paramref name="partitionKeys"/> that are effectively killed: their
    /// Location has an enforced kill-switch and either that kill-switch ignores the whitelist
    /// (lockdown) or the pair is not whitelisted. Lockdown never deletes whitelist rows — lifting
    /// it restores the standing whitelist with no re-setup.
    /// </summary>
    /// <param name="partitionKeys">The partition identities to evaluate.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The killed subset. Membership is tested with <c>Contains</c>; a key absent from the
    /// result is not killed.</returns>
    Task<IReadOnlySet<PartitionKillKey>> GetKilledPartitionsAsync(IReadOnlyCollection<PartitionKillKey> partitionKeys, CancellationToken ct = default);

    /// <summary>
    /// Returns the subset of <paramref name="locationIds"/> that currently carry an enforced
    /// kill-switch (a frozen Location), ignoring the whitelist. Serves the location-grain guards
    /// (write paths B07, sync B08) that act before any specific template is known.
    /// </summary>
    /// <param name="locationIds">The Location ids to evaluate.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The frozen Location ids among the input.</returns>
    Task<IReadOnlySet<Guid>> GetEnforcedKilledLocationIdsAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken ct = default);
}