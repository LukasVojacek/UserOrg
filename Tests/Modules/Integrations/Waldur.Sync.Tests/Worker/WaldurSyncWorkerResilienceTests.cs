using Waldur.Contracts.Events;

using Xunit;

namespace Waldur.Sync.Tests.Worker;

public class WaldurSyncWorkerResilienceTests
{
    [Fact]
    public async Task Should_CountMembersAsBlockedByParent_When_TheirProjectFails()
    {
        var scenario = new WorkerTestScenario().WithFullHappyPath();

        // The project handler throws (e.g. a referenced provider/location is missing); its member
        // event then throws too (missing parent), exactly as the in-process consumer would (A1).
        scenario.Publisher.ThrowFor = e => e is ProjectCreated or UserAssigned;
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);
        var summary = await worker.RunIterationAsync(CancellationToken.None);

        Assert.Equal(2, summary.Failed);            // project (root) + member (cascade)
        Assert.Equal(1, summary.BlockedByParent);   // only the member is blocked-by-parent
        Assert.Contains(scenario.Publisher.Published, e => e is ProviderCreated);
        Assert.DoesNotContain(scenario.Publisher.Published, e => e is ProjectCreated);
        Assert.DoesNotContain(scenario.Publisher.Published, e => e is UserAssigned);
    }

    [Fact]
    public async Task Should_RetryStartupNextTick_And_NotThrowOutOfRunAsync_When_ColdRebuildFailsOnce()
    {
        var scenario = new WorkerTestScenario().WithFullHappyPath();
        scenario.Rebuilder.ThrowTimes = 1; // first cold rebuild fails, second succeeds
        var worker = scenario.Build();
        using var cts = new CancellationTokenSource();

        var delays = 0;
        scenario.Time.OnDelay = () =>
        {
            if (++delays >= 2)
            {
                cts.Cancel();
            }
        };

        // Must NOT throw — a faulted worker cannot be allowed to stop the host (and other instances).
        await worker.RunAsync(cts.Token);

        Assert.Equal(2, scenario.Rebuilder.CallCount);      // failed tick 1 → retried tick 2
        Assert.True(scenario.Client.CustomersCallCount >= 1); // iterated only after startup succeeded
    }

    [Fact]
    public async Task Should_NotIterate_When_ColdRebuildFails()
    {
        var scenario = new WorkerTestScenario().WithFullHappyPath();
        scenario.Rebuilder.ThrowTimes = 5; // rebuild keeps failing
        var worker = scenario.Build();
        using var cts = new CancellationTokenSource();

        var delays = 0;
        scenario.Time.OnDelay = () =>
        {
            if (++delays >= 3)
            {
                cts.Cancel();
            }
        };

        await worker.RunAsync(cts.Token);

        // Never iterated on a bad/empty map → nothing fetched, nothing published.
        Assert.Equal(0, scenario.Client.CustomersCallCount);
        Assert.Empty(scenario.Publisher.Published);
    }
}
