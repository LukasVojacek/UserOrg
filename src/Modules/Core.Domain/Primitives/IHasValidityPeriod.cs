namespace Core.Domain.Primitives;

/// <summary>
/// Identifies entities that have a validity period defined by start and end dates.
/// </summary>
public interface IHasValidityPeriod
{
    DateTimeOffset StartDate { get; }
    DateTimeOffset EndDate { get; }

    public bool IsCurrentlyValid(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var now = timeProvider.GetUtcNow();
        return StartDate <= now && now <= EndDate;
    }
}