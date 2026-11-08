namespace Waldur.Sync.Http;

/// <summary>
/// Runs an async selector over a set of sources with a <b>bounded</b> degree of parallelism and
/// returns the results in source order. Replaces the legacy producer's unbounded
/// <c>Task.WhenAll</c> fan-out over per-project / per-offering detail fetches (ADR-0056/A4).
/// </summary>
internal static class BoundedFanOut
{
    public static async Task<IReadOnlyList<TResult>> MapAsync<TSource, TResult>(
        IReadOnlyList<TSource> sources,
        int maxDegreeOfParallelism,
        Func<TSource, CancellationToken, Task<TResult>> selector,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(selector);

        var dop = Math.Max(1, maxDegreeOfParallelism);
        var results = new TResult[sources.Count];

        await Parallel.ForAsync(
            0,
            sources.Count,
            new ParallelOptions { MaxDegreeOfParallelism = dop, CancellationToken = ct },
            async (i, token) => results[i] = await selector(sources[i], token).ConfigureAwait(false))
            .ConfigureAwait(false);

        return results;
    }
}