using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

using Core.DataAccess.Outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Core.UnitTests.Outbox;

/// <summary>
/// Verifies the B01 <see cref="OutboxTelemetry"/> instruments move as the dispatcher processes rows: a
/// dispatched counter + duration histogram on success, a failed counter on a retryable failure, and a
/// dead-lettered counter when <see cref="OutboxRetryPolicy.MaxAttempts"/> is reached. Instrument values are
/// captured via a <see cref="MeterListener"/> scoped to <see cref="OutboxTelemetry.MeterName"/>.
/// </summary>
public sealed class OutboxTelemetryTests
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

    private sealed class FakeDispatchTarget(bool shouldThrow) : IOutboxDispatchTarget
    {
        public Task DispatchAsync(OutboxMessage message, CancellationToken ct)
        {
            if (shouldThrow)
                throw new InvalidOperationException("dispatch boom");

            return Task.CompletedTask;
        }
    }

    private static OutboxMessage PendingMessage(string type, int attempts = 0) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Payload = "{}",
        OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        Attempts = attempts,
    };

    private sealed class CapturedInstruments
    {
        public ConcurrentDictionary<string, long> Counters { get; } = new();
        public ConcurrentDictionary<string, int> HistogramSamples { get; } = new();
    }

    // The instruments are static/process-wide, so parallel dispatcher tests fire the same counters. Each test
    // uses a unique message Type and the listener only counts measurements carrying that type tag — isolating
    // this test's contribution regardless of concurrent test runs.
    private static MeterListener StartListener(CapturedInstruments captured, string typeTagValue)
    {
        bool MatchesType(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            foreach (var tag in tags)
            {
                if (tag.Key == OutboxTelemetry.TagMessageType && (tag.Value as string) == typeTagValue)
                    return true;
            }

            return false;
        }

        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OutboxTelemetry.MeterName)
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (MatchesType(tags))
                captured.Counters.AddOrUpdate(instrument.Name, measurement, (_, v) => v + measurement);
        });
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
        {
            if (MatchesType(tags))
                captured.HistogramSamples.AddOrUpdate(instrument.Name, 1, (_, v) => v + 1);
        });
        listener.Start();
        return listener;
    }

    private static async Task<CapturedInstruments> RunPollAsync(bool throwOnDispatch, int attempts = 0)
    {
        var storeName = Guid.NewGuid().ToString();
        var type = $"Test.Event.{Guid.NewGuid().ToString("N")}";
        var message = PendingMessage(type, attempts);
        await using (var ctx = NewContext(storeName))
        {
            ctx.OutboxMessages.Add(message);
            await ctx.SaveChangesAsync(CancellationToken.None);
        }

        var captured = new CapturedInstruments();
        using var listener = StartListener(captured, type);

        using var service = new OutboxDispatcherService(
            new FakeContextFactory(storeName),
            new FakeDispatchTarget(throwOnDispatch),
            NullLogger<OutboxDispatcherService>.Instance);

        await service.PollOnceAsync(CancellationToken.None);

        listener.RecordObservableInstruments();
        return captured;
    }

    [Fact]
    public async Task Should_IncrementDispatchedAndRecordDuration_When_TargetSucceeds()
    {
        var captured = await RunPollAsync(throwOnDispatch: false);

        Assert.Equal(1, captured.Counters.GetValueOrDefault("outbox.messages.dispatched"));
        Assert.Equal(0, captured.Counters.GetValueOrDefault("outbox.messages.failed"));
        Assert.Equal(0, captured.Counters.GetValueOrDefault("outbox.messages.dead_lettered"));
        Assert.True(captured.HistogramSamples.GetValueOrDefault("outbox.dispatch.duration") >= 1);
    }

    [Fact]
    public async Task Should_IncrementFailedCounter_When_TargetThrowsBelowMaxAttempts()
    {
        var captured = await RunPollAsync(throwOnDispatch: true);

        Assert.Equal(1, captured.Counters.GetValueOrDefault("outbox.messages.failed"));
        Assert.Equal(0, captured.Counters.GetValueOrDefault("outbox.messages.dispatched"));
        Assert.Equal(0, captured.Counters.GetValueOrDefault("outbox.messages.dead_lettered"));
    }

    [Fact]
    public async Task Should_IncrementDeadLetteredCounter_When_AttemptsReachMaxAttempts()
    {
        var captured = await RunPollAsync(throwOnDispatch: true, attempts: OutboxRetryPolicy.MaxAttempts - 1);

        Assert.Equal(1, captured.Counters.GetValueOrDefault("outbox.messages.dead_lettered"));
        Assert.Equal(1, captured.Counters.GetValueOrDefault("outbox.messages.failed"));
        Assert.Equal(0, captured.Counters.GetValueOrDefault("outbox.messages.dispatched"));
    }
}
