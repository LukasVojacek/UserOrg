using System.Collections.Concurrent;

using Core.DataAccess.Outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Core.UnitTests.Outbox;

/// <summary>
/// Covers the two dispatcher behaviours the safety-net (O4) and B01 telemetry suites leave untested: the
/// <see cref="Microsoft.Extensions.Hosting.BackgroundService"/> loop's outer try/catch keeping the poller alive
/// when a poll throws, and the operator-facing <c>LogCritical</c> emitted when a row reaches
/// <see cref="OutboxRetryPolicy.MaxAttempts"/>. The safety-net tests drive <c>PollOnceAsync</c> directly, so they
/// never exercise the loop; the telemetry tests assert the dead-lettered counter but not the critical log.
/// </summary>
public sealed class OutboxDispatcherResilienceTests
{
    private static OutboxDbContext NewContext(string storeName) =>
        new(new DbContextOptionsBuilder<OutboxDbContext>()
            .UseInMemoryDatabase(storeName)
            .Options);

    private sealed class FakeContextFactory(string storeName) : IDbContextFactory<OutboxDbContext>
    {
        public OutboxDbContext CreateDbContext() => NewContext(storeName);

        public Task<OutboxDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(NewContext(storeName));
    }

    /// <summary>Every context request fails — simulates a transient DB/factory outage during a poll.</summary>
    private sealed class ThrowingContextFactory : IDbContextFactory<OutboxDbContext>
    {
        public OutboxDbContext CreateDbContext() => throw new InvalidOperationException("db down");

        public Task<OutboxDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("db down");
    }

    private sealed class FakeDispatchTarget(bool shouldThrow) : IOutboxDispatchTarget
    {
        internal static FakeDispatchTarget AlwaysThrows() => new(true);
        internal static FakeDispatchTarget Unused() => new(false);

        public Task DispatchAsync(OutboxMessage message, CancellationToken ct)
        {
            if (shouldThrow)
                throw new InvalidOperationException("dispatch boom");

            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        internal readonly record struct Entry(LogLevel Level, string Message, Exception? Exception);

        private readonly ConcurrentQueue<Entry> _entries = new();
        private readonly TaskCompletionSource _errorLogged = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal IReadOnlyCollection<Entry> Entries => _entries;

        /// <summary>Completes the first time an <see cref="LogLevel.Error"/> entry is written.</summary>
        internal Task ErrorLogged => _errorLogged.Task;

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Enqueue(new Entry(logLevel, formatter(state, exception), exception));
            if (logLevel == LogLevel.Error)
                _errorLogged.TrySetResult();
        }
    }

    private static OutboxMessage PendingMessage(int attempts = 0) => new()
    {
        Id = Guid.NewGuid(),
        Type = "Core.Domain.Project.ProjectResourceAssignmentChangedEvent",
        Payload = "{}",
        OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        Attempts = attempts,
    };

    private static async Task SeedAsync(string storeName, OutboxMessage message)
    {
        await using var ctx = NewContext(storeName);
        ctx.OutboxMessages.Add(message);
        await ctx.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Should_LogErrorAndKeepLoopAlive_When_PollThrows()
    {
        var logger = new CapturingLogger<OutboxDispatcherService>();
        using var service = new OutboxDispatcherService(
            new ThrowingContextFactory(), FakeDispatchTarget.Unused(), logger);

        // The first poll runs immediately and its factory throws. Without the loop's try/catch, ExecuteAsync
        // would fault and no error would ever be logged, so the wait would time out. Observing the error log
        // (then a clean stop) is the proof the poll exception was swallowed and the loop kept running.
        await service.StartAsync(CancellationToken.None);
        await logger.ErrorLogged.WaitAsync(TimeSpan.FromSeconds(30));
        await service.StopAsync(CancellationToken.None);

        Assert.Contains(
            logger.Entries,
            e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Critical);
    }

    [Fact]
    public async Task Should_LogCritical_When_RowReachesMaxAttempts()
    {
        var storeName = Guid.NewGuid().ToString();
        var message = PendingMessage(attempts: OutboxRetryPolicy.MaxAttempts - 1);
        await SeedAsync(storeName, message);

        var logger = new CapturingLogger<OutboxDispatcherService>();
        using var service = new OutboxDispatcherService(
            new FakeContextFactory(storeName), FakeDispatchTarget.AlwaysThrows(), logger);

        await service.PollOnceAsync(CancellationToken.None);

        var critical = Assert.Single(logger.Entries, e => e.Level == LogLevel.Critical);
        Assert.Contains(message.Id.ToString(), critical.Message, StringComparison.Ordinal);
        Assert.NotNull(critical.Exception);
    }

    [Fact]
    public async Task Should_LogWarningNotCritical_When_FailureIsBelowMaxAttempts()
    {
        var storeName = Guid.NewGuid().ToString();
        var message = PendingMessage(attempts: OutboxRetryPolicy.MaxAttempts - 2);
        await SeedAsync(storeName, message);

        var logger = new CapturingLogger<OutboxDispatcherService>();
        using var service = new OutboxDispatcherService(
            new FakeContextFactory(storeName), FakeDispatchTarget.AlwaysThrows(), logger);

        await service.PollOnceAsync(CancellationToken.None);

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Critical);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }
}
