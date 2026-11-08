using Waldur.Contracts.Events;
using Waldur.Sync.Api.WaldurApiModels;
using Waldur.Sync.Tests.Mapping;

using Xunit;

namespace Waldur.Sync.Tests.Worker;

public class WaldurSyncWorkerMembershipTests
{
    [Fact]
    public async Task Should_EmitNoMembershipEvents_When_UserFetchFails()
    {
        var scenario = new WorkerTestScenario().WithFullHappyPath();
        scenario.Client.ProjectsThatFailUserFetch.Add(MapperTestData.ProjectId);
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);
        await worker.RunIterationAsync(CancellationToken.None);

        // D7: a failed member fetch yields no membership events, but provider/project still publish.
        Assert.Contains(scenario.Publisher.Published, e => e is ProviderCreated);
        Assert.Contains(scenario.Publisher.Published, e => e is ProjectCreated);
        Assert.DoesNotContain(scenario.Publisher.Published, e => e is UserAssigned);
    }

    [Fact]
    public async Task Should_ReThrowCancellation_When_MembershipFetchCancelled()
    {
        using var cts = new CancellationTokenSource();
        var scenario = new WorkerTestScenario().WithFullHappyPath();
        scenario.Client.CancelOnUserFetch = cts; // cancels the token as the user fetch begins
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);

        // The Result seam re-throws cancellation (does not swallow it into MembershipSnapshot.Failed).
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => worker.RunIterationAsync(cts.Token));
    }

    [Fact]
    public async Task Should_EmitUserRemoved_When_KnownMemberLeavesCompleteSnapshot()
    {
        var scenario = new WorkerTestScenario().WithFullHappyPath();
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);
        await worker.RunIterationAsync(CancellationToken.None); // alice assigned + SHA recorded

        // Next iteration: alice is gone from the (still complete) snapshot → UserRemoved.
        scenario.Client.UsersByProject = new Dictionary<Guid, IReadOnlyList<ApiProjectUser>>
        {
            [MapperTestData.ProjectId] = []
        };

        var summary = await worker.RunIterationAsync(CancellationToken.None);

        Assert.Contains(scenario.Publisher.Published, e => e is UserRemoved);
        Assert.Equal(1, summary.Removed);
    }
}
