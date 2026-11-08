using Waldur.Sync.Api.WaldurApiModels;

using Xunit;

namespace Waldur.Sync.Tests.Worker;

public class WaldurSyncWorkerSummaryTests
{
    [Fact]
    public async Task Should_ReportCreatedAndFetched_When_AllEntitiesNew()
    {
        var scenario = new WorkerTestScenario().WithFullHappyPath();
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);
        var summary = await worker.RunIterationAsync(CancellationToken.None);

        Assert.Equal(3, summary.Fetched);   // provider + project + member
        Assert.Equal(3, summary.Created);
        Assert.Equal(0, summary.Updated);
        Assert.Equal(0, summary.Unchanged);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(0, summary.Absent);
        Assert.False(summary.FetchFailed);
    }

    [Fact]
    public async Task Should_ReportAbsentTransition_When_KnownProviderLeavesThePull()
    {
        var scenario = new WorkerTestScenario().WithFullHappyPath();
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);
        await worker.RunIterationAsync(CancellationToken.None); // provider + project + member known

        // The provider (and its offering) vanish from the pull; the project loses its resource and
        // becomes ineligible too, so both provider and project transition into absence.
        scenario.Client.Customers = [];
        scenario.Client.OfferingsByCustomer = new Dictionary<Guid, IReadOnlyList<ApiPublicOffering>>();
        scenario.Client.ResourcesByProject = new Dictionary<Guid, IReadOnlyList<ApiMarketplaceResource>>();

        var summary = await worker.RunIterationAsync(CancellationToken.None);

        Assert.True(summary.Absent >= 1);
    }

    [Fact]
    public async Task Should_ReportFetchFailed_When_ApiThrows()
    {
        var scenario = new WorkerTestScenario().WithFullHappyPath();
        scenario.Client.CustomersFetchThrows = true;
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);
        var summary = await worker.RunIterationAsync(CancellationToken.None);

        Assert.True(summary.FetchFailed);
        Assert.Empty(scenario.Publisher.Published);
    }
}
