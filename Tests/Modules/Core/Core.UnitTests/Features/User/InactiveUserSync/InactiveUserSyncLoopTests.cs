using System.Collections.Concurrent;
using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using InactiveUserSyncOptions = Core.Features.User.InactiveUserSync.InactiveUserSyncOptions;
using InactiveUserSyncService = Core.Features.User.InactiveUserSync.InactiveUserSync;

namespace Core.UnitTests.Features.User.InactiveUserSync;

public sealed class InactiveUserSyncLoopTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SyncInterval = TimeSpan.FromDays(7);
    private static readonly TimeSpan Tick = TimeSpan.FromTicks(1);

    private sealed class ConcurrentLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception)));
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private int _disabledUserFetches;

        public int DisabledUserFetches => Volatile.Read(ref _disabledUserFetches);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/protocol/openid-connect/token"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"test-token","expires_in":3600,"token_type":"Bearer"}"""),
                });
            }

            Interlocked.Increment(ref _disabledUserFetches);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });
        }
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name = "") => client;
    }

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly HttpClient _client;

        public Harness()
        {
            _client = new HttpClient(Http);
            _provider = new ServiceCollection()
                .AddSingleton<TimeProvider>(Clock)
                .BuildServiceProvider();

            Service = ActivatorUtilities.CreateInstance<InactiveUserSyncService>(
                _provider,
                _provider.GetRequiredService<IServiceScopeFactory>(),
                new StaticHttpClientFactory(_client),
                Options.Create(new InactiveUserSyncOptions
                {
                    BaseUrl = "https://aai.test.local/auth/realms/LEXIS",
                    ClientId = "test-client",
                    ClientSecret = "test-secret",
                    SyncInterval = SyncInterval,
                    Enabled = true,
                }),
                Logger);
        }

        public ManualTimeProvider Clock { get; } = new(Now);

        public CountingHandler Http { get; } = new();

        public ConcurrentLogger<InactiveUserSyncService> Logger { get; } = new();

        public InactiveUserSyncService Service { get; }

        public void Dispose()
        {
            Service.Dispose();
            _client.Dispose();
            _provider.Dispose();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10, CancellationToken.None);

        Assert.True(condition(), because);
    }

    private static async Task StartAndPassTheStartupDelayAsync(Harness harness, CancellationToken ct)
    {
        await harness.Service.StartAsync(ct);
        await WaitUntilAsync(
            () => harness.Clock.PendingTimerCount == 1,
            "the 60 s startup delay is scheduled on the injected TimeProvider");

        harness.Clock.Advance(StartupDelay);
        await WaitUntilAsync(() => harness.Http.DisabledUserFetches == 1, "the first sync runs once the startup delay has passed");
    }

    private static async Task StopWithinFiveSecondsAsync(BackgroundService service)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task Should_RunTheFirstSync_OnlyOnceTheStartupDelayHasPassedOnTheInjectedClock()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = new Harness();

        await harness.Service.StartAsync(ct);
        await WaitUntilAsync(
            () => harness.Clock.PendingTimerCount == 1,
            "the 60 s startup delay is scheduled on the injected TimeProvider");

        harness.Clock.Advance(StartupDelay - Tick);
        Assert.Equal(1, harness.Clock.PendingTimerCount);
        Assert.Equal(0, harness.Http.DisabledUserFetches);

        harness.Clock.Advance(Tick);
        await WaitUntilAsync(() => harness.Http.DisabledUserFetches == 1, "the first sync runs once the startup delay has passed");
        Assert.Contains(harness.Logger.Entries, e => e.Message == "No inactive users to process");

        await StopWithinFiveSecondsAsync(harness.Service);
    }

    [Fact]
    public async Task Should_SyncAgain_OnlyOnceTheSyncIntervalHasPassedOnTheInjectedClock()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = new Harness();
        await StartAndPassTheStartupDelayAsync(harness, ct);
        await WaitUntilAsync(() => harness.Clock.PendingTimerCount == 1, "the sync interval is scheduled on the injected TimeProvider");

        harness.Clock.Advance(SyncInterval - Tick);
        Assert.Equal(1, harness.Http.DisabledUserFetches);

        harness.Clock.Advance(Tick);
        await WaitUntilAsync(() => harness.Http.DisabledUserFetches == 2, "the second sync runs once the interval has passed");

        await StopWithinFiveSecondsAsync(harness.Service);
    }

    [Fact]
    public async Task Should_EndTheLoop_When_StoppedWhileWaitingForTheNextSync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = new Harness();
        await StartAndPassTheStartupDelayAsync(harness, ct);

        await StopWithinFiveSecondsAsync(harness.Service);

        Assert.True(harness.Service.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Equal(1, harness.Http.DisabledUserFetches);
        Assert.Equal(0, harness.Clock.PendingTimerCount);
    }

    [Fact]
    public async Task Should_ReturnWithoutSyncing_When_StoppedDuringTheStartupDelay()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = new Harness();

        await harness.Service.StartAsync(ct);
        await WaitUntilAsync(
            () => harness.Logger.Entries.Any(e => e.Message == $"Inactive user sync started. First run in {StartupDelay}, then every {SyncInterval}"),
            "the service logs its schedule before the startup delay");
        await StopWithinFiveSecondsAsync(harness.Service);

        Assert.True(harness.Service.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Equal(0, harness.Http.DisabledUserFetches);
    }
}
