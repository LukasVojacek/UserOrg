using Waldur.Sync.Http;

using Xunit;

namespace Waldur.Sync.Tests;

public class BoundedFanOutTests
{
    [Fact]
    public async Task Should_PreserveSourceOrder_When_Mapping()
    {
        var sources = Enumerable.Range(0, 20).ToList();

        var results = await BoundedFanOut.MapAsync(
            sources,
            maxDegreeOfParallelism: 4,
            async (i, ct) =>
            {
                await Task.Yield();
                return i * 2;
            },
            CancellationToken.None);

        Assert.Equal(sources.Select(i => i * 2), results);
    }

    [Fact]
    public async Task Should_BoundConcurrency_When_DegreeLimited()
    {
        var current = 0;
        var maxObserved = 0;
        var gate = new object();

        await BoundedFanOut.MapAsync(
            Enumerable.Range(0, 50).ToList(),
            maxDegreeOfParallelism: 3,
            async (_, ct) =>
            {
                lock (gate)
                {
                    current++;
                    maxObserved = Math.Max(maxObserved, current);
                }

                await Task.Delay(5, ct);

                lock (gate)
                {
                    current--;
                }

                return 0;
            },
            CancellationToken.None);

        Assert.True(maxObserved <= 3, $"Observed concurrency {maxObserved} exceeded the bound of 3.");
    }
}
