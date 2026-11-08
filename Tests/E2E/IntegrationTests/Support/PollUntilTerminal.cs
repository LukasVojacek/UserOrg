using System.Globalization;

namespace IntegrationTests.Support;

/// <summary>
/// Bounded poll-until-terminal helper for the async write scenarios (ADR-0067): the background RequestWorker
/// settles a request off the HTTP thread, so a test asserts by repeatedly probing DB / Gateway / WireMock state
/// until a predicate holds or a hard timeout elapses — never a fixed sleep. Reused by B04 and B05.
/// </summary>
internal static class PollUntilTerminal
{
    /// <summary>
    /// Probes <paramref name="probe"/> immediately and then every <paramref name="interval"/> until
    /// <paramref name="isSatisfied"/> accepts the observed value, which is returned. Throws
    /// <see cref="TimeoutException"/> — carrying <paramref name="description"/> and the last observed value —
    /// if <paramref name="timeout"/> elapses first.
    /// </summary>
    public static async Task<T> ForAsync<T>(
        Func<CancellationToken, Task<T>> probe,
        Func<T, bool> isSatisfied,
        TimeSpan timeout,
        TimeSpan interval,
        string description,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(isSatisfied);

        var deadline = DateTimeOffset.UtcNow + timeout;
        T last;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            last = await probe(cancellationToken);
            if (isSatisfied(last))
            {
                return last;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{description}' was not satisfied within {timeout.TotalSeconds:0}s. Last observed: {last?.ToString() ?? "<null>"}."));
            }

            await Task.Delay(interval, cancellationToken);
        }
    }
}
