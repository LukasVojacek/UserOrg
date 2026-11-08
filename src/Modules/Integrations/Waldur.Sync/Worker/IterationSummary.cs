namespace Waldur.Sync.Worker;

/// <summary>
/// The always-log result of one sync iteration (ADR-0058): the counts that make up the single
/// summary line. Returned by <see cref="WaldurSyncWorker.RunIterationAsync"/> so the loop can log it
/// and tests can assert on it without scraping log output.
/// </summary>
/// <param name="Fetched">Entities considered this iteration (providers + projects + snapshot members).</param>
/// <param name="Created">Acked <c>*Created</c> / <c>UserAssigned</c> events.</param>
/// <param name="Updated">Acked <c>*Updated</c> / <c>UserRoleChanged</c> / <c>StorageConfigurationChanged</c> events.</param>
/// <param name="Removed">Acked <c>UserRemoved</c> events.</param>
/// <param name="Unchanged">Fetched entities whose hash matched (no event emitted).</param>
/// <param name="Failed">Publish failures (item not acked, no SHA written, retried next iteration).</param>
/// <param name="BlockedByParent">Subset of <see cref="Failed"/> whose parent entity also failed this iteration (ADR-0058/A2).</param>
/// <param name="Absent">Provider/project entries that transitioned into absence this iteration (ADR-0058/A1).</param>
public sealed record IterationSummary(
    int Fetched,
    int Created,
    int Updated,
    int Removed,
    int Unchanged,
    int Failed,
    int BlockedByParent,
    int Absent)
{
    /// <summary>Iteration wall-clock duration (set by the worker from its time provider).</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>True when the whole iteration failed to fetch its input (phase-level failure).</summary>
    public bool FetchFailed { get; init; }
}