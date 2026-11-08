using System.Collections.Concurrent;

using Core.DataAccess.Queries.Contracts;
using Core.DataAccess.ResourceRequests;
using Core.Features.ResourceRequest.Expiry;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using WorkflowEngine.Contracts.Ports;

namespace Core.UnitTests.ResourceRequests.Expiry;

public sealed class ResourceRequestExpirySweeperLoopTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan PollDelay = TimeSpan.FromHours(1);
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

    private sealed class Harness : IDisposable
    {
        private int _sweeps;
        private readonly ServiceProvider _provider;

        public Harness(Func<int, CancellationToken, Task<IReadOnlyList<ResourceRequestDetail>>> onSweep)
        {
            var query = new Mock<IProjectResourceRequestQuery>();
            query
                .Setup(q => q.FindDueForExpiryAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .Returns((DateTimeOffset _, CancellationToken ct) => onSweep(Interlocked.Increment(ref _sweeps), ct));

            var services = new ServiceCollection();
            services.AddScoped(_ => query.Object);
            services.AddScoped(_ => new Mock<IWorkflowClient>().Object);
            services.AddScoped(_ => new Mock<IResourceRequestProjectionStore>().Object);
            _provider = services.BuildServiceProvider();

            Sweeper = new ResourceRequestExpirySweeper(
                _provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new ResourceRequestExpiryOptions { DurationInDays = 30 }),
                Clock,
                Logger);
        }

        public ManualTimeProvider Clock { get; } = new(Now);

        public ConcurrentLogger<ResourceRequestExpirySweeper> Logger { get; } = new();

        public ResourceRequestExpirySweeper Sweeper { get; }

        public int Sweeps => Volatile.Read(ref _sweeps);

        public void Dispose()
        {
            Sweeper.Dispose();
            _provider.Dispose();
        }
    }

    private static Task<IReadOnlyList<ResourceRequestDetail>> NothingDue(int sweep, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ResourceRequestDetail>>([]);

    private static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10, CancellationToken.None);

        Assert.True(condition(), because);
    }

    private static async Task StopWithinFiveSecondsAsync(BackgroundService service)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task Should_SweepAtStart_ThenAgainOnlyOnceAnHourHasPassedOnTheInjectedClock()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = new Harness(NothingDue);

        await harness.Sweeper.StartAsync(ct);
        await WaitUntilAsync(() => harness.Sweeps == 1 && harness.Clock.PendingTimerCount == 1, "the first sweep runs at start and parks on the poll delay");

        harness.Clock.Advance(PollDelay - Tick);
        Assert.Equal(1, harness.Clock.PendingTimerCount);
        Assert.Equal(1, harness.Sweeps);

        harness.Clock.Advance(Tick);
        await WaitUntilAsync(() => harness.Sweeps == 2, "the second sweep runs once the hour has passed");

        await StopWithinFiveSecondsAsync(harness.Sweeper);
        Assert.Contains(harness.Logger.Entries, e => e.Message == "Resource-request expiry sweeper started.");
        Assert.Contains(harness.Logger.Entries, e => e.Message == "Resource-request expiry sweeper stopped.");
    }

    [Fact]
    public async Task Should_LogTheFailureAndSweepAgainAfterThePollDelay_When_ASweepThrows()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = new Harness((sweep, token) => sweep == 1
            ? throw new InvalidOperationException("db down")
            : NothingDue(sweep, token));

        await harness.Sweeper.StartAsync(ct);
        await WaitUntilAsync(() => harness.Clock.PendingTimerCount == 1, "a failed sweep still parks on the poll delay");

        Assert.Contains(harness.Logger.Entries, e =>
            e.Level == LogLevel.Error && e.Message == "Resource-request expiry sweep failed; retrying after the poll delay.");

        harness.Clock.Advance(PollDelay);
        await WaitUntilAsync(() => harness.Sweeps == 2, "the loop survives the failed sweep");

        await StopWithinFiveSecondsAsync(harness.Sweeper);
    }

    [Fact]
    public async Task Should_StopWithoutAnotherSweep_When_StoppedDuringThePollDelay()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = new Harness(NothingDue);

        await harness.Sweeper.StartAsync(ct);
        await WaitUntilAsync(() => harness.Sweeps == 1 && harness.Clock.PendingTimerCount == 1, "the first sweep parks on the poll delay");

        await StopWithinFiveSecondsAsync(harness.Sweeper);

        Assert.True(harness.Sweeper.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Equal(1, harness.Sweeps);
        Assert.Equal(0, harness.Clock.PendingTimerCount);
        Assert.Contains(harness.Logger.Entries, e => e.Message == "Resource-request expiry sweeper stopped.");
    }

    [Fact]
    public async Task Should_StopQuietly_When_ShutdownCancelsASweepInFlight()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = new Harness(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return [];
        });

        await harness.Sweeper.StartAsync(ct);
        await WaitUntilAsync(() => harness.Sweeps == 1, "the first sweep is in flight");

        await StopWithinFiveSecondsAsync(harness.Sweeper);

        Assert.True(harness.Sweeper.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Equal(0, harness.Clock.PendingTimerCount);
        Assert.DoesNotContain(harness.Logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(harness.Logger.Entries, e => e.Message == "Resource-request expiry sweeper stopped.");
    }
}
