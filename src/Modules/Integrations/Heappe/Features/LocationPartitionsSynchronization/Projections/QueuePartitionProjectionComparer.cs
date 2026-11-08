namespace Heappe.Features.LocationPartitionsSynchronization.Projections;

/// <summary>
/// Equality comparer for <see cref="QueuePartitionProjection"/> instances.
/// Compares <see cref="QueuePartitionProjection.QueueName"/> and
/// <see cref="QueuePartitionProjection.AggregationName"/> using a supplied
/// string comparer (defaults to <see cref="StringComparer.Ordinal"/>).
/// </summary>
internal sealed class QueuePartitionProjectionComparer : IEqualityComparer<QueuePartitionProjection>
{
    private readonly IEqualityComparer<string> _stringComparer;

    /// <summary>
    /// Creates a new instance of <see cref="QueuePartitionProjectionComparer"/>.
    /// </summary>
    /// <param name="stringComparer">
    /// Optional string comparer to use for comparing queue and aggregation names.
    /// If <c>null</c>, <see cref="StringComparer.Ordinal"/> is used.
    /// </param>
    public QueuePartitionProjectionComparer(IEqualityComparer<string>? stringComparer = null)
    {
        _stringComparer = stringComparer ?? StringComparer.Ordinal;
    }

    /// <summary>
    /// Determines whether two <see cref="QueuePartitionProjection"/> objects are equal.
    /// Two projections are considered equal when both their queue names and aggregation names
    /// are equal according to the configured string comparer. Null aggregation names are treated
    /// as empty strings for comparison purposes.
    /// </summary>
    /// <param name="x">First projection to compare.</param>
    /// <param name="y">Second projection to compare.</param>
    /// <returns><c>true</c> if the projections are equal; otherwise <c>false</c>.</returns>
    public bool Equals(QueuePartitionProjection? x, QueuePartitionProjection? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null || y is null)
        {
            return false;
        }

        return _stringComparer.Equals(x.QueueName, y.QueueName)
            && _stringComparer.Equals(x.AggregationName ?? string.Empty, y.AggregationName ?? string.Empty);
    }

    /// <summary>
    /// Returns a hash code for the specified <see cref="QueuePartitionProjection"/>.
    /// Combines hash codes of the queue name and aggregation name using the configured string comparer.
    /// If the provided object is <c>null</c>, returns 0.
    /// </summary>
    /// <param name="obj">The projection for which to get a hash code.</param>
    /// <returns>An integer hash code.</returns>
    public int GetHashCode(QueuePartitionProjection obj)
    {
        if (obj is null)
        {
            return 0;
        }

        var h1 = _stringComparer.GetHashCode(obj.QueueName);
        var h2 = _stringComparer.GetHashCode(obj.AggregationName ?? string.Empty);
        return HashCode.Combine(h1, h2);
    }
}