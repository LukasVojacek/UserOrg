using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Core.Domain.Provider;

/// <summary>
/// Location name value object (natural identifier).
/// </summary>
[JsonConverter(typeof(ValueObjectJsonConverter<LocationName, string>))]
public readonly record struct LocationName(string Value) : IValueObject<LocationName, string>, IComparable<LocationName>, IComparable
{
    static LocationName IValueObject<LocationName, string>.From(string value) => Create(value);

    public static LocationName Create(string value) =>
        TryCreate(value, out var result, out var error)
            ? result
            : throw new ArgumentException(error, nameof(value));

    /// <summary>Validates and constructs a <see cref="LocationName"/> without throwing.</summary>
    public static bool TryCreate(string? value, out LocationName result, [NotNullWhen(false)] out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = default;
            error = "LocationName cannot be empty";
            return false;
        }

        if (value.Length > 100)
        {
            result = default;
            error = "LocationName must not exceed 100 characters";
            return false;
        }

        // Preserve the original casing for display; only trim surrounding whitespace. Identity is
        // case-insensitive via Equals/GetHashCode, so "GPU" and "gpu" still collapse for the purely
        // in-memory matching/uniqueness checks (no DB collation safety net) without rewriting what
        // callers see on reads (ADR-0012).
        result = new LocationName(value.Trim());
        error = null;
        return true;
    }

    public static LocationName From(string value) => Create(value);

    public bool Equals(LocationName other) =>
        StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    public override int GetHashCode() =>
        Value is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    // Case-insensitive ordering, consistent with the case-insensitive identity above. Lets read queries order
    // by the value object directly (`orderby loc.Name`) — EF translates that to ORDER BY the mapped column, while
    // the in-memory test provider sorts via this comparison. Ordering by `loc.Name.Value` is NOT SQL-translatable.
    public int CompareTo(LocationName other) => StringComparer.OrdinalIgnoreCase.Compare(Value, other.Value);

    int IComparable.CompareTo(object? obj) => obj is LocationName other
        ? CompareTo(other)
        : throw new ArgumentException($"Object must be of type {nameof(LocationName)}.", nameof(obj));

    public override string ToString() => Value;
}