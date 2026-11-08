using System.Threading.RateLimiting;

namespace Waldur.Sync.Http;

/// <summary>
/// Delegating handler that applies client-side token-bucket rate limiting to all outgoing HTTP
/// requests. Ported from the legacy <c>WaldurPlatform.Base.RateLimitHandler</c>: requests are
/// throttled (queued), never rejected, and the caller's cancellation is honoured after the lease
/// is acquired.
/// </summary>
public sealed class RateLimitHandler : DelegatingHandler
{
    private readonly TokenBucketRateLimiter _limiter;

    /// <summary>Creates a handler with the given rate-limit parameters.</summary>
    /// <param name="rateLimitPerSecond">Maximum tokens replenished per second.</param>
    /// <param name="rateLimitBurst">Optional burst capacity; defaults to <paramref name="rateLimitPerSecond"/>.</param>
    public RateLimitHandler(int rateLimitPerSecond, int? rateLimitBurst = null)
    {
        var permitsPerSecond = Math.Max(1, rateLimitPerSecond);
        var burst = Math.Max(permitsPerSecond, rateLimitBurst ?? permitsPerSecond);
        _limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = burst,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = int.MaxValue,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            TokensPerPeriod = permitsPerSecond,
            AutoReplenishment = true
        });
    }

    /// <summary>Test-friendly constructor allowing injection of a pre-configured limiter.</summary>
    public RateLimitHandler(TokenBucketRateLimiter limiter)
    {
        ArgumentNullException.ThrowIfNull(limiter);
        _limiter = limiter;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Acquire with CancellationToken.None so the limiter throttles throughput without being
        // interrupted (rejected) by the caller's token while waiting in the queue.
        using var lease = await _limiter.AcquireAsync(1, CancellationToken.None).ConfigureAwait(false);

        // Honour cancellation only after the lease is acquired.
        cancellationToken.ThrowIfCancellationRequested();
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _limiter.Dispose();
        }

        base.Dispose(disposing);
    }
}