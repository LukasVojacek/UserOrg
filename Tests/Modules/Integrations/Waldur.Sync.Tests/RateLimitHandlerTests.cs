using System.Diagnostics;
using System.Net;
using System.Threading.RateLimiting;

using Waldur.Sync.Http;

using Xunit;

namespace Waldur.Sync.Tests;

public class RateLimitHandlerTests
{
    private sealed class CountingHandler : DelegatingHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task Should_ThrottleOutgoingRequests_When_TokensExhausted()
    {
        var limiterOptions = new TokenBucketRateLimiterOptions
        {
            TokenLimit = 1,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = int.MaxValue,
            ReplenishmentPeriod = TimeSpan.FromMilliseconds(200),
            TokensPerPeriod = 1,
            AutoReplenishment = true
        };
        using var limiter = new TokenBucketRateLimiter(limiterOptions);
        var counting = new CountingHandler();
        using var rateHandler = new RateLimitHandler(limiter) { InnerHandler = counting };
        using var client = new HttpClient(rateHandler);

        var sw = Stopwatch.StartNew();
        var t1 = client.GetAsync(new Uri("https://example.local/"), CancellationToken.None);
        var t2 = client.GetAsync(new Uri("https://example.local/"), CancellationToken.None);
        await Task.WhenAll(t1, t2);
        sw.Stop();

        Assert.Equal(2, counting.Calls);
        Assert.True(sw.ElapsedMilliseconds >= 180, $"Elapsed {sw.ElapsedMilliseconds}ms shorter than expected");
    }

    [Fact]
    public async Task Should_ThrottleWithoutException_When_ManyRequestsQueued()
    {
        var limiterOptions = new TokenBucketRateLimiterOptions
        {
            TokenLimit = 1,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = int.MaxValue,
            ReplenishmentPeriod = TimeSpan.FromMilliseconds(100),
            TokensPerPeriod = 1,
            AutoReplenishment = true
        };
        using var limiter = new TokenBucketRateLimiter(limiterOptions);
        var counting = new CountingHandler();
        using var rateHandler = new RateLimitHandler(limiter) { InnerHandler = counting };
        using var client = new HttpClient(rateHandler);

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => client.GetAsync(new Uri("https://example.local/"), CancellationToken.None))
            .ToArray();
        var responses = await Task.WhenAll(tasks);

        Assert.Equal(5, counting.Calls);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task Should_ThrowOperationCancelled_When_TokenAlreadyCancelledBeforeSend()
    {
        var limiterOptions = new TokenBucketRateLimiterOptions
        {
            TokenLimit = 10,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = int.MaxValue,
            ReplenishmentPeriod = TimeSpan.FromSeconds(60),
            TokensPerPeriod = 1,
            AutoReplenishment = true
        };
        using var limiter = new TokenBucketRateLimiter(limiterOptions);
        var counting = new CountingHandler();
        using var rateHandler = new RateLimitHandler(limiter) { InnerHandler = counting };
        using var invoker = new HttpMessageInvoker(rateHandler);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://example.local/"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invoker.SendAsync(request, cts.Token));
        Assert.Equal(0, counting.Calls);
    }
}
