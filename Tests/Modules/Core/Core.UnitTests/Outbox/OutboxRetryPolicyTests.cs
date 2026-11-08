using Core.DataAccess.Outbox;

namespace Core.UnitTests.Outbox;

/// <summary>
/// Unit tests for the pure retry/backoff policy (block O4, ADR-0040). This is the testable core of the
/// dispatcher's retry behavior; the SQL poll loop itself is verified against a real DB by the developer/CI.
/// </summary>
public sealed class OutboxRetryPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 06, 29, 12, 00, 00, TimeSpan.Zero);

    [Fact]
    public void Should_BeDue_When_NeverAttempted()
    {
        Assert.True(OutboxRetryPolicy.IsDue(attempts: 0, lastAttemptAt: null, now: Now));
    }

    [Fact]
    public void Should_NotBeDue_When_WithinBackoffWindow()
    {
        // After 1 attempt the backoff is 60s (BaseDelay 30s * 2^1). 10s elapsed is inside the window.
        var lastAttempt = Now - TimeSpan.FromSeconds(10);

        Assert.False(OutboxRetryPolicy.IsDue(attempts: 1, lastAttemptAt: lastAttempt, now: Now));
    }

    [Fact]
    public void Should_BeDue_When_BackoffWindowElapsed()
    {
        // After 1 attempt the backoff is 60s; 2 minutes elapsed is well past it.
        var lastAttempt = Now - TimeSpan.FromMinutes(2);

        Assert.True(OutboxRetryPolicy.IsDue(attempts: 1, lastAttemptAt: lastAttempt, now: Now));
    }

    [Fact]
    public void Should_GrowBackoff_When_AttemptsIncrease()
    {
        var first = OutboxRetryPolicy.Backoff(1);
        var second = OutboxRetryPolicy.Backoff(2);
        var third = OutboxRetryPolicy.Backoff(3);

        Assert.True(second > first);
        Assert.True(third > second);
    }

    [Fact]
    public void Should_CapBackoffAtMaxDelay_When_AttemptsAreLarge()
    {
        var maxDelay = TimeSpan.FromHours(1);

        // A huge attempt count must never exceed the ceiling and must not overflow.
        Assert.Equal(maxDelay, OutboxRetryPolicy.Backoff(100));
        Assert.Equal(maxDelay, OutboxRetryPolicy.Backoff(int.MaxValue));
    }

    [Fact]
    public void Should_ExposeMaxAttempts_When_Referenced()
    {
        Assert.True(OutboxRetryPolicy.MaxAttempts > 0);
    }
}
