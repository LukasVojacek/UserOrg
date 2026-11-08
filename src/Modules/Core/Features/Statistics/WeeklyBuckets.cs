namespace Core.Features.Statistics;

/// <summary>
/// Pure, <c>DateTime.Now</c>-free reimplementation of the legacy weekly bucketing
/// (<c>WeekPart.GetWeeksFromStartDate</c> / <c>GetWeekPartForDate</c>). Weeks are Monday-anchored and
/// half-open <c>[Monday 00:00, Sunday 00:00)</c>; the series is bounded by <paramref name="today"/>
/// and the requested end date. <paramref name="today"/> is supplied by the caller (from a
/// <see cref="TimeProvider"/>) instead of read from the ambient clock, so the logic is testable.
/// </summary>
internal static class WeeklyBuckets
{
    internal readonly record struct Week(DateOnly From, DateOnly To);

    internal static IReadOnlyList<Week> GetWeeks(DateOnly fromDate, DateOnly toDate, DateOnly today)
    {
        var weeks = new List<Week>();
        var start = fromDate.ToDateTime(TimeOnly.MinValue);

        // Legacy: AddDays(DayOfWeek.Monday - DayOfWeek) — anchors to the Monday of the start week
        // (steps back for Tue–Sat, forward for Sun).
        var monday = start.AddDays(DayOfWeek.Monday - start.DayOfWeek);
        var todayMidnight = today.ToDateTime(TimeOnly.MinValue);
        var end = toDate.ToDateTime(TimeOnly.MaxValue);

        while (monday <= todayMidnight)
        {
            var weekEnd = monday.AddDays(6);
            weeks.Add(new Week(DateOnly.FromDateTime(monday), DateOnly.FromDateTime(weekEnd)));
            monday = monday.AddDays(7);
            if (monday >= end)
            {
                break;
            }
        }

        return weeks;
    }

    /// <summary>
    /// Counts timestamps that fall inside the half-open week window <c>[From 00:00, To 00:00)</c>.
    /// Callers pass timestamps already constrained to the requested <c>[fromDate, toDate]</c> range.
    /// </summary>
    internal static int CountIn(IReadOnlyList<DateTime> timestamps, Week week)
    {
        var from = week.From.ToDateTime(TimeOnly.MinValue);
        var to = week.To.ToDateTime(TimeOnly.MinValue);
        return timestamps.Count(t => t >= from && t < to);
    }
}