using Xunit;

namespace Waldur.Sync.Tests.Worker;

public class WaldurSyncWorkerSchedulingTests
{
    [Fact]
    public async Task Should_StopCleanly_When_Cancelled()
    {
        var scenario = new WorkerTestScenario().WithFullHappyPath();
        var worker = scenario.Build();
        using var cts = new CancellationTokenSource();

        // Cancel as soon as the first inter-iteration delay is requested.
        scenario.Time.OnDelay = cts.Cancel;

        await worker.RunAsync(cts.Token); // must not throw

        Assert.True(scenario.Client.CustomersCallCount >= 1);
        Assert.Equal(1, scenario.Rebuilder.CallCount);
    }

    [Fact]
    public async Task Should_DelayByConfiguredInterval_MeasuredFromCompletion()
    {
        var scenario = new WorkerTestScenario(syncIntervalMinutes: 30).WithFullHappyPath();
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

        await worker.RunAsync(cts.Token);

        Assert.Equal(2, scenario.Time.Delays.Count);
        Assert.All(scenario.Time.Delays, d => Assert.Equal(TimeSpan.FromMinutes(30), d));
    }

    [Fact]
    public async Task Should_RequestDelayAfterIterationPublishes_When_Looping()
    {
        var scenario = new WorkerTestScenario().WithFullHappyPath();
        var worker = scenario.Build();
        using var cts = new CancellationTokenSource();

        var sequence = new List<string>();
        scenario.Publisher.OnPublished = _ => sequence.Add("publish");
        scenario.Time.OnDelay = () =>
        {
            sequence.Add("delay");
            cts.Cancel();
        };

        await worker.RunAsync(cts.Token);

        // The first delay is requested only after the iteration's publishes completed (A2).
        var firstDelay = sequence.IndexOf("delay");
        Assert.True(firstDelay > 0);
        Assert.All(sequence.Take(firstDelay), step => Assert.Equal("publish", step));
    }
}
