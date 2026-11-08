namespace Core.DataAccess.Outbox;

/// <summary>
/// Pure, testable retry/backoff policy for the outbox dispatcher (ADR-0028, ADR-0040). Decides whether a
/// row is due for another attempt and how long the backoff window grows per attempt. No I/O, no state —
/// this is the unit-testable core of the retry behavior.
/// </summary>
internal static class OutboxRetryPolicy
{
    /// <summary>Maximum delivery attempts before a row is treated as dead-lettered / stuck.</summary>
    internal const int MaxAttempts = 10;

    /// <summary>First backoff step (the delay after attempt #1).</summary>
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(30);

    /// <summary>Backoff ceiling — the window never grows past this regardless of attempt count.</summary>
    private static readonly TimeSpan MaxDelay = TimeSpan.FromHours(1);

    /// <summary>
    /// True when the row may be attempted now: either never attempted, or the backoff window since the
    /// last attempt has elapsed.
    /// </summary>
    /// <param name="attempts">Attempts made so far.</param>
    /// <param name="lastAttemptAt">Timestamp of the last attempt, or <c>null</c> if never attempted.</param>
    /// <param name="now">The current time.</param>
    internal static bool IsDue(int attempts, DateTimeOffset? lastAttemptAt, DateTimeOffset now)
    {
        if (lastAttemptAt is null)
            return true;

        return now - lastAttemptAt.Value >= Backoff(attempts);
    }

    /// <summary>
    /// Exponential backoff: <c>min(MaxDelay, BaseDelay * 2^attempts)</c>, capped at <see cref="MaxDelay"/>.
    /// </summary>
    /// <param name="attempts">Attempts made so far (0 ⇒ <see cref="BaseDelay"/>).</param>
    internal static TimeSpan Backoff(int attempts)
    {
        if (attempts <= 0)
            return BaseDelay;

        // Guard against overflow on the doubling: cap the exponent once it would exceed MaxDelay anyway.
        // 2^31 * 30s already dwarfs one hour, so clamp the shift well before long overflow.
        var shift = Math.Min(attempts, 31);
        var scaled = BaseDelay * Math.Pow(2, shift);

        return scaled >= MaxDelay ? MaxDelay : scaled;
    }
}