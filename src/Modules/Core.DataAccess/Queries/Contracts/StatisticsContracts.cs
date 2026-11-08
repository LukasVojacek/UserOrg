namespace Core.DataAccess.Queries.Contracts;

/// <summary>
/// Raw <c>CreatedAt</c> timestamps (normalised to UTC) for every non-deleted User, Project and
/// ProjectResource. The statistics endpoint derives totals (list counts) and weekly buckets from
/// these in memory, keeping the week-bucketing a pure, testable function.
/// </summary>
public sealed record CreationTimestamps(
    IReadOnlyList<DateTime> Users,
    IReadOnlyList<DateTime> Projects,
    IReadOnlyList<DateTime> Resources);