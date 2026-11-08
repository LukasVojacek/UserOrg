using Core.DataAccess.Queries.Contracts;
using Core.Features.Statistics.GetStatistics;

using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

namespace Core.UnitTests.Statistics;

public sealed class GetStatisticsEndpointTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static async Task<GetStatisticsEndpoint.Response> InvokeAsync(CreationTimestamps timestamps)
    {
        var query = new Mock<IStatisticsQuery>();
        query.Setup(q => q.GetCreationTimestampsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(timestamps);

        // 2026-06-15 is a Monday; with no date filters the window is 2026-06-01 .. 2026-06-15.
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));

        var result = await GetStatisticsEndpoint.HandleAsync(null, null, query.Object, clock, CancellationToken.None);

        return Assert.IsType<Ok<GetStatisticsEndpoint.Response>>(result).Value!;
    }

    [Fact]
    public async Task Should_ReturnTotalsAcrossAllRows_And_BucketWeeklyWithinRange()
    {
        var timestamps = new CreationTimestamps(
            Users:
            [
                new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc),  // week 0
                new DateTime(2026, 6, 9, 10, 0, 0, DateTimeKind.Utc),  // week 1
                new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc)   // before range — counts in total only
            ],
            Projects: [new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc)], // week 2 (Monday, included by half-open lower bound)
            Resources: []);

        var response = await InvokeAsync(timestamps);

        // Totals are the full row counts, independent of the requested window.
        Assert.Equal(1, response.NumberOfProjects);
        Assert.Equal(3, response.NumberOfUsers);
        Assert.Equal(0, response.NumberOfResources);

        Assert.Equal(3, response.WeeklyData.Count);
        Assert.Equal(1, response.WeeklyData[0].UsersCreated);
        Assert.Equal(1, response.WeeklyData[1].UsersCreated);
        Assert.Equal(0, response.WeeklyData[2].UsersCreated);
        Assert.Equal(1, response.WeeklyData[2].ProjectsCreated);
        Assert.All(response.WeeklyData, w => Assert.Equal(0, w.ResourcesCreated));
    }
}
