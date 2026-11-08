using System.Net;

using Microsoft.Extensions.DependencyInjection;

using Polly;

using Waldur.Sync.Configuration;
using Waldur.Sync.Http;

using Xunit;

namespace Waldur.Sync.Tests;

public class WaldurRetryStrategyTests
{
    private sealed class SequenceStatusHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode[] _statuses;
        public int CallCount { get; private set; }

        public SequenceStatusHandler(params HttpStatusCode[] statuses) => _statuses = statuses;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var status = _statuses[Math.Min(CallCount, _statuses.Length - 1)];
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    [Fact]
    public async Task Should_RetryTransientErrors_ThenSucceed()
    {
        using var handler = new SequenceStatusHandler(
            HttpStatusCode.InternalServerError,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.OK);

        var options = new WaldurInstanceOptions { RetryCount = 3, RetryInitialBackoffMs = 1 };

        var services = new ServiceCollection();
        services.AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddResilienceHandler("retry", builder => builder.AddRetry(WaldurRetryStrategy.Build(options)));

        using var sp = services.BuildServiceProvider();
        using var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("test");

        using var response = await client.GetAsync(new Uri("https://example.local/"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, handler.CallCount);
    }
}
