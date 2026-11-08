using Waldur.Contracts.Events;

using Xunit;

namespace Waldur.Sync.Tests.Worker;

public class WaldurSyncWorkerAckTests
{
    [Fact]
    public async Task Should_IsolateFailure_When_OneItemPublishThrows()
    {
        var scenario = new WorkerTestScenario().WithFullHappyPath();
        scenario.Publisher.ThrowFor = e => e is ProviderCreated;
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);
        var summary = await worker.RunIterationAsync(CancellationToken.None);

        // The failed provider is not recorded, but the following items are still processed.
        Assert.Contains(scenario.Publisher.Published, e => e is ProjectCreated);
        Assert.Contains(scenario.Publisher.Published, e => e is UserAssigned);
        Assert.DoesNotContain(scenario.Publisher.Published, e => e is ProviderCreated);
        Assert.Equal(1, summary.Failed);
    }

    [Fact]
    public async Task Should_NotWriteSha_When_PublishFails_SoItemReplaysNextIteration()
    {
        var scenario = new WorkerTestScenario().WithFullHappyPath();
        scenario.Publisher.ThrowFor = e => e is ProviderCreated;
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);
        await worker.RunIterationAsync(CancellationToken.None);

        // Second iteration: provider publish now succeeds. Because its SHA was never written, it is
        // re-diffed as Created and re-published; project/member were acked so they stay unchanged.
        scenario.Publisher.ThrowFor = null;
        await worker.RunIterationAsync(CancellationToken.None);

        Assert.Single(scenario.Publisher.Published, e => e is ProviderCreated);
        Assert.Single(scenario.Publisher.Published, e => e is ProjectCreated);
        Assert.Single(scenario.Publisher.Published, e => e is UserAssigned);
    }

    [Fact]
    public async Task Should_WriteSha_When_Acked_SoUnchangedItemsAreSilentNextIteration()
    {
        var scenario = new WorkerTestScenario().WithFullHappyPath();
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);
        await worker.RunIterationAsync(CancellationToken.None);
        var publishedAfterFirst = scenario.Publisher.Published.Count;

        var second = await worker.RunIterationAsync(CancellationToken.None);

        Assert.Equal(publishedAfterFirst, scenario.Publisher.Published.Count); // nothing new
        Assert.Equal(0, second.Created);
        Assert.Equal(3, second.Unchanged);
    }
}
