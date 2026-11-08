using System.Net;

using Microsoft.Extensions.Http.Resilience;

using Polly;

using Waldur.Sync.Configuration;

namespace Waldur.Sync.Http;

/// <summary>
/// Builds the Polly HTTP retry strategy for a Waldur instance. Ported from the legacy
/// <c>ServiceCollectionExtensions.AddResilienceHandler</c>: exponential backoff with jitter,
/// retrying transient failures (HTTP 5xx, 429, <see cref="HttpRequestException"/>).
/// </summary>
internal static class WaldurRetryStrategy
{
    public static HttpRetryStrategyOptions Build(WaldurInstanceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var retryOptions = new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = Math.Max(0, options.RetryCount),
            Delay = TimeSpan.FromMilliseconds(Math.Max(1, options.RetryInitialBackoffMs)),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(r => (int)r.StatusCode >= 500 ||
                                   r.StatusCode == HttpStatusCode.TooManyRequests)
        };

        if (options.RetryMaxDelayMs is int maxMs)
        {
            retryOptions.MaxDelay = TimeSpan.FromMilliseconds(maxMs);
        }

        return retryOptions;
    }
}