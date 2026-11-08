using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Core.Domain.Provider;

[JsonConverter(typeof(ValueObjectJsonConverter<ResourceName, string>))]
public readonly record struct ResourceName : IValueObject<ResourceName, string>, IParsable<ResourceName>, IComparable<ResourceName>, IComparable
{
    public string Value { get; }

    private ResourceName(string value) => Value = value.Trim();

    // Ordinal ordering by the underlying name. Lets read queries order by the value object directly
    // (`orderby pr.ResourceName`), which EF translates to ORDER BY the mapped column while the EF in-memory
    // test provider sorts via this comparison. Ordering by `pr.ResourceName.Value` is NOT SQL-translatable.
    public int CompareTo(ResourceName other) => string.CompareOrdinal(Value, other.Value);

    int IComparable.CompareTo(object? obj) => obj is ResourceName other
        ? CompareTo(other)
        : throw new ArgumentException($"Object must be of type {nameof(ResourceName)}.", nameof(obj));

    static ResourceName IValueObject<ResourceName, string>.From(string value) => Create(value);

    public static ResourceName Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Resource name cannot be empty", nameof(value));

        if (value.Length > 100)
            throw new ArgumentException("Resource name cannot exceed 100 characters", nameof(value));

        return new ResourceName(value);
    }

    public static ResourceName From(string value) => Create(value);

    public static ResourceName Parse(string s, IFormatProvider? provider) => Create(s);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out ResourceName result)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length > 100)
        {
            result = default;
            return false;
        }

        result = new ResourceName(s);
        return true;
    }

    public override string ToString() => Value;
}