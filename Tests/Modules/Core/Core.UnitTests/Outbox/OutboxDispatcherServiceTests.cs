using System.Collections.Concurrent;

using Core.DataAccess.Outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Core.UnitTests.Outbox;

/// <summary>
/// Characterization tests for <see cref="OutboxDispatcherService"/> (block O4 test-coverage audit). These pin
/// TODAY's poll/dispatch behaviour — success stamping, failure/backoff bookkeeping, dead-letter exclusion from
/// the next batch, backoff gating, and batch ordering/cap — so a later change (e.g. telemetry wiring around
/// <c>ProcessRowAsync</c>) cannot silently break the dispatch loop. No telemetry (meters/spans) is asserted here;
/// none exists yet.
/// <para>
/// <c>PollOnceAsync</c> is <c>internal</c> (not <c>private</c>) purely as a test seam so a single poll can be
/// driven deterministically instead of running the full <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>
/// loop; this does not change any dispatch behaviour.
/// </para>
/// </summary>
public sealed class OutboxDispatcherServiceTests
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

    private sealed class FakeDispatchTarget(Func<OutboxMessage, bool> shouldThrow) : IOutboxDispatchTarget
    {
        private readonly ConcurrentBag<Guid> _calls = [];

        internal static FakeDispatchTarget AlwaysSucceeds() => new(_ => false);
        internal static FakeDispatchTarget AlwaysThrows() => new(_ => true);

        internal IReadOnlyCollection<Guid> Calls => _calls;

        public Task DispatchAsync(OutboxMessage message, CancellationToken ct)
        {
            _calls.Add(message.Id);
            if (shouldThrow(message))
                throw new InvalidOperationException("dispatch boom");

            return Task.CompletedTask;
        }
    }

    private static OutboxMessage PendingMessage(
        DateTimeOffset occurredAt, int attempts = 0, DateTimeOffset? lastAttemptAt = null) => new()
        {
            Id = Guid.NewGuid(),
            Type = "Core.Domain.Project.ProjectResourceAssignmentChangedEvent",
            Payload = "{}",
            OccurredAt = occurredAt,
            Attempts = attempts,
            LastAttemptAt = lastAttemptAt,
        };

    private static async Task SeedAsync(string storeName, params OutboxMessage[] messages)
    {
        await using var ctx = NewContext(storeName);
        ctx.OutboxMessages.AddRange(messages);
        await ctx.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task<OutboxMessage> ReadAsync(string storeName, Guid id)
    {
        await using var ctx = NewContext(storeName);
        return await ctx.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    [Fact]
    public async Task Should_StampDispatchedAt_When_TargetSucceeds()
    {
        var storeName = Guid.NewGuid().ToString();
        var message = PendingMessage(DateTimeOffset.UtcNow.AddMinutes(-5));
        await SeedAsync(storeName, message);

        var target = FakeDispatchTarget.AlwaysSucceeds();
        var service = new OutboxDispatcherService(
            new FakeContextFactory(storeName), target, NullLogger<OutboxDispatcherService>.Instance);

        await service.PollOnceAsync(CancellationToken.None);

        var persisted = await ReadAsync(storeName, message.Id);
        Assert.NotNull(persisted.DispatchedAt);
        Assert.Equal(0, persisted.Attempts);
        Assert.Null(persisted.Error);
        Assert.Contains(message.Id, target.Calls);
    }

    [Fact]
    public async Task Should_RecordFailureAndKeepPending_When_TargetThrows()
    {
        var storeName = Guid.NewGuid().ToString();
        var message = PendingMessage(DateTimeOffset.UtcNow.AddMinutes(-5));
        await SeedAsync(storeName, message);

        var target = FakeDispatchTarget.AlwaysThrows();
        var service = new OutboxDispatcherService(
            new FakeContextFactory(storeName), target, NullLogger<OutboxDispatcherService>.Instance);

        var before = DateTimeOffset.UtcNow;
        await service.PollOnceAsync(CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        var persisted = await ReadAsync(storeName, message.Id);
        Assert.Null(persisted.DispatchedAt);
        Assert.Equal(1, persisted.Attempts);
        Assert.NotNull(persisted.LastAttemptAt);
        Assert.InRange(persisted.LastAttemptAt!.Value, before, after);
        Assert.Equal("dispatch boom", persisted.Error);
    }

    [Fact]
    public async Task Should_ExcludeRowFromNextBatch_When_AttemptsReachesMaxAttempts()
    {
        var storeName = Guid.NewGuid().ToString();
        // One attempt away from the dead-letter threshold; never attempted so it is due regardless of backoff.
        var message = PendingMessage(DateTimeOffset.UtcNow.AddMinutes(-5), attempts: OutboxRetryPolicy.MaxAttempts - 1);
        await SeedAsync(storeName, message);

        var target = FakeDispatchTarget.AlwaysThrows();
        var service = new OutboxDispatcherService(
            new FakeContextFactory(storeName), target, NullLogger<OutboxDispatcherService>.Instance);

        await service.PollOnceAsync(CancellationToken.None);

        var afterFirstPoll = await ReadAsync(storeName, message.Id);
        Assert.Equal(OutboxRetryPolicy.MaxAttempts, afterFirstPoll.Attempts);
        Assert.Single(target.Calls);

        // Second poll: the row now has Attempts == MaxAttempts, so the batch query (Attempts < MaxAttempts)
        // must no longer select it, regardless of backoff timing.
        await service.PollOnceAsync(CancellationToken.None);

        Assert.Single(target.Calls);
        var afterSecondPoll = await ReadAsync(storeName, message.Id);
        Assert.Equal(OutboxRetryPolicy.MaxAttempts, afterSecondPoll.Attempts);
        Assert.Null(afterSecondPoll.DispatchedAt);
    }

    [Fact]
    public async Task Should_SkipRowInsideBackoffWindow_And_DispatchRowPastBackoff()
    {
        var storeName = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;

        // Attempts = 1 -> backoff = 60s (OutboxRetryPolicy.Backoff(1)).
        var notYetDue = PendingMessage(now.AddMinutes(-10), attempts: 1, lastAttemptAt: now);
        var pastBackoff = PendingMessage(now.AddMinutes(-10), attempts: 1, lastAttemptAt: now.AddMinutes(-10));
        await SeedAsync(storeName, notYetDue, pastBackoff);

        Assert.False(OutboxRetryPolicy.IsDue(notYetDue.Attempts, notYetDue.LastAttemptAt, now));
        Assert.True(OutboxRetryPolicy.IsDue(pastBackoff.Attempts, pastBackoff.LastAttemptAt, now));

        var target = FakeDispatchTarget.AlwaysSucceeds();
        var service = new OutboxDispatcherService(
            new FakeContextFactory(storeName), target, NullLogger<OutboxDispatcherService>.Instance);

        await service.PollOnceAsync(CancellationToken.None);

        Assert.Contains(pastBackoff.Id, target.Calls);
        Assert.DoesNotContain(notYetDue.Id, target.Calls);

        var notYetDuePersisted = await ReadAsync(storeName, notYetDue.Id);
        var pastBackoffPersisted = await ReadAsync(storeName, pastBackoff.Id);
        Assert.Null(notYetDuePersisted.DispatchedAt);
        Assert.NotNull(pastBackoffPersisted.DispatchedAt);
    }

    [Fact]
    public async Task Should_DispatchOnlyEarliestBatchSizeRows_When_MoreThanBatchSizeArePending()
    {
        const int batchSize = 50;
        var storeName = Guid.NewGuid().ToString();
        var baseTime = DateTimeOffset.UtcNow.AddDays(-1);

        // 5 extra rows beyond BatchSize, each with a strictly increasing OccurredAt so ordering is unambiguous.
        var messages = Enumerable.Range(0, batchSize + 5)
            .Select(i => PendingMessage(baseTime.AddSeconds(i)))
            .ToList();
        await SeedAsync(storeName, [.. messages]);

        var target = FakeDispatchTarget.AlwaysSucceeds();
        var service = new OutboxDispatcherService(
            new FakeContextFactory(storeName), target, NullLogger<OutboxDispatcherService>.Instance);

        await service.PollOnceAsync(CancellationToken.None);

        Assert.Equal(batchSize, target.Calls.Count);

        var expectedDispatched = messages.Take(batchSize).Select(m => m.Id).ToHashSet();
        var expectedSkipped = messages.Skip(batchSize).Select(m => m.Id).ToHashSet();

        Assert.True(expectedDispatched.SetEquals(target.Calls));
        foreach (var id in expectedSkipped)
        {
            var persisted = await ReadAsync(storeName, id);
            Assert.Null(persisted.DispatchedAt);
        }
    }
}
