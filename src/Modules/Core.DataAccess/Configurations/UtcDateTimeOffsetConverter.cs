using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Core.DataAccess.Configurations;

/// <summary>
/// Converts DateTimeOffset (domain) ↔ DateTime UTC (SQL Server column).
/// EF Core 8+ applies nullable lifting automatically for DateTimeOffset? properties.
/// </summary>
internal sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTime>
{
    internal static readonly UtcDateTimeOffsetConverter Instance = new();

    public UtcDateTimeOffsetConverter()
        : base(d => d.UtcDateTime, d => new DateTimeOffset(d, TimeSpan.Zero))
    {
    }
}