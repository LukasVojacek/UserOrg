using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Core.Domain.Provider;

/// <summary>
/// Provider name value object (natural identifier, unique constraint).
/// </summary>
[JsonConverter(typeof(ValueObjectJsonConverter<ProviderName, string>))]
public readonly record struct ProviderName(string Value) : IValueObject<ProviderName, string>, IComparable<ProviderName>, IComparable
{
    static ProviderName IValueObject<ProviderName, string>.From(string value) => Create(value);

    public static ProviderName Create(string value) =>
        TryCreate(value, out var result, out var error)
            ? result
            : throw new ArgumentException(error, nameof(value));

    /// <summary>Validates and constructs a <see cref="ProviderName"/> without throwing.</summary>
    public static bool TryCreate(string? value, out ProviderName result, [NotNullWhen(false)] out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = default;
            error = "ProviderName cannot be empty";
            return false;
        }

        if (value.Length > 100)
        {
            result = default;
            error = "ProviderName must not exceed 100 characters";
            return false;
        }

        // Preserve the original casing for display; only trim surrounding whitespace. Identity is
        // case-insensitive via Equals/GetHashCode, so the natural key still has one definition of
        // equality across routing, repository lookup, uniqueness, in-memory matching, and
        // authorization — without rewriting what callers see on reads (ADR-0012).
        result = new ProviderName(value.Trim());
        error = null;
        return true;
    }

    public static ProviderName From(string value) => Create(value);

    public bool Equals(ProviderName other) =>
        StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    public override int GetHashCode() =>
        Value is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    // Case-insensitive ordering, consistent with the case-insensitive identity above. Lets read queries order
    // by the value object directly (`orderby p.Name`) — EF translates that to ORDER BY the mapped column, while
    // the in-memory test provider sorts via this comparison. Ordering by `p.Name.Value` is NOT SQL-translatable.
    public int CompareTo(ProviderName other) => StringComparer.OrdinalIgnoreCase.Compare(Value, other.Value);

    int IComparable.CompareTo(object? obj) => obj is ProviderName other
        ? CompareTo(other)
        : throw new ArgumentException($"Object must be of type {nameof(ProviderName)}.", nameof(obj));

    public override string ToString() => Value;
}