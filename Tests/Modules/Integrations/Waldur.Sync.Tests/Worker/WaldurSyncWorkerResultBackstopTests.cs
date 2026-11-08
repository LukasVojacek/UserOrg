using Waldur.Contracts.Events;

using Xunit;

namespace Waldur.Sync.Tests.Worker;

/// <summary>
/// B07 tests for <see cref="Waldur.Sync.Worker.WaldurSyncWorker"/>'s apply loop under ADR-0065: a
/// handler <c>Result.Failure</c> (expected dependency-missing) writes no SHA and re-diffs next
/// iteration, and an UNEXPECTED handler exception is contained by the retained backstop (no SHA,
/// iteration continues).
/// </summary>
public class WaldurSyncWorkerResultBackstopTests
{
    [Fact]
    public async Task Should_NotWriteSha_When_HandlerReturnsFailure()
    {
        var scenario = new WorkerTestScenario().WithFullHappyPath();
        scenario.Publisher.FailFor = e => e is ProviderCreated;
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);
        var summary = await worker.RunIterationAsync(CancellationToken.None);

        // The rejected provider is not acked (not recorded), but sibling items are still processed.
        Assert.DoesNotContain(scenario.Publisher.Published, e => e is ProviderCreated);
        Assert.Contains(scenario.Publisher.Published, e => e is ProjectCreated);
        Assert.Contains(scenario.Publisher.Published, e => e is UserAssigned);
        Assert.Equal(1, summary.Failed);

        // Its SHA was never written, so next iteration re-diffs it as Created and re-publishes it;
        // project/member were acked and stay silent.
        scenario.Publisher.FailFor = null;
        await worker.RunIterationAsync(CancellationToken.None);

        Assert.Single(scenario.Publisher.Published, e => e is ProviderCreated);
        Assert.Single(scenario.Publisher.Published, e => e is ProjectCreated);
        Assert.Single(scenario.Publisher.Published, e => e is UserAssigned);
    }

    [Fact]
    public async Task Should_Backstop_When_HandlerThrowsUnexpected()
    {
        var scenario = new WorkerTestScenario().WithFullHappyPath();
        // An UNEXPECTED fault (e.g. a simulated SqlException / programming error) — the publisher throws
        // a non-cancellation exception.
        scenario.Publisher.ThrowFor = e => e is ProviderCreated;
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);

        // The backstop must contain the throw: RunIterationAsync completes rather than propagating.
        var summary = await worker.RunIterationAsync(CancellationToken.None);

        Assert.Equal(1, summary.Failed);
        Assert.DoesNotContain(scenario.Publisher.Published, e => e is ProviderCreated); // no ack
        Assert.Contains(scenario.Publisher.Published, e => e is ProjectCreated);        // iteration continues
        Assert.Contains(scenario.Publisher.Published, e => e is UserAssigned);
    }
}
