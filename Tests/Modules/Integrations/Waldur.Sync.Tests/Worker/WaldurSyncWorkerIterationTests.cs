using Waldur.Contracts.Events;

using Xunit;

namespace Waldur.Sync.Tests.Worker;

public class WaldurSyncWorkerIterationTests
{
    [Fact]
    public async Task Should_PublishInPhaseOrder_When_AllEntitiesNew()
    {
        var scenario = new WorkerTestScenario().WithFullHappyPath();
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);
        await worker.RunIterationAsync(CancellationToken.None);

        var kinds = scenario.Publisher.Published.Select(e => e.GetType()).ToList();
        Assert.Equal(
            [typeof(ProviderCreated), typeof(ProjectCreated), typeof(UserAssigned)],
            kinds);
    }

    [Fact]
    public async Task Should_CarryInstanceAndServiceName_When_Publishing()
    {
        var scenario = new WorkerTestScenario().WithFullHappyPath();
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);
        await worker.RunIterationAsync(CancellationToken.None);

        Assert.All(scenario.Publisher.Published, e =>
        {
            Assert.Equal("EFP", e.Instance);
            Assert.Equal("EFP-Waldur", e.ServiceName);
        });
    }
}
