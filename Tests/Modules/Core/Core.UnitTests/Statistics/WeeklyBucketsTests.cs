using Core.Features.Statistics;

namespace Core.UnitTests.Statistics;

public sealed class WeeklyBucketsTests
{
    // 2026-06-01, -08, -15 are all Mondays; 2026-06-15 is used as "today".
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 6, 30);
    private static readonly DateOnly Today = new(2026, 6, 15);

    [Fact]
    public void Should_AnchorWeeksToMonday_AndStopAtToday()
    {
        var weeks = WeeklyBuckets.GetWeeks(From, To, Today);

        Assert.Equal(3, weeks.Count);
        Assert.Equal(new DateOnly(2026, 6, 1), weeks[0].From);
        Assert.Equal(new DateOnly(2026, 6, 7), weeks[0].To);
        Assert.Equal(new DateOnly(2026, 6, 8), weeks[1].From);
        Assert.Equal(new DateOnly(2026, 6, 15), weeks[2].From);
        // No week starts after today.
        Assert.DoesNotContain(weeks, w => w.From > Today);
    }

    [Fact]
    public void Should_AnchorBackToMondayOfWeek_When_StartIsMidWeek()
    {
        // 2026-06-03 is a Wednesday; the first week must still start on Monday 2026-06-01.
        var weeks = WeeklyBuckets.GetWeeks(new DateOnly(2026, 6, 3), To, Today);

        Assert.Equal(new DateOnly(2026, 6, 1), weeks[0].From);
    }

    [Fact]
    public void Should_CountTimestampsInHalfOpenWeek()
    {
        var week = new WeeklyBuckets.Week(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 7));

        var timestamps = new[]
        {
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),    // Monday 00:00 — counted
            new DateTime(2026, 6, 6, 23, 59, 59, DateTimeKind.Utc), // Saturday — counted
            new DateTime(2026, 6, 7, 0, 0, 0, DateTimeKind.Utc),    // Sunday 00:00 — excluded (half-open upper bound)
            new DateTime(2026, 5, 31, 23, 59, 59, DateTimeKind.Utc) // before the window — excluded
        };

        Assert.Equal(2, WeeklyBuckets.CountIn(timestamps, week));
    }

    [Fact]
    public void Should_ReturnNoWeeks_When_StartIsAfterToday()
    {
        var weeks = WeeklyBuckets.GetWeeks(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), Today);

        Assert.Empty(weeks);
    }
}
