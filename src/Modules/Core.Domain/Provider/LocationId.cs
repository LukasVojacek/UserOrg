using System.Text.Json.Serialization;

namespace Core.Domain.Provider;

/// <summary>
/// Strongly typed ID for Location.
/// </summary>
[JsonConverter(typeof(ValueObjectJsonConverter<LocationId, Guid>))]
public readonly record struct LocationId(Guid Value) : IGuidId<LocationId>
{
    static LocationId IValueObject<LocationId, Guid>.From(Guid value) => new(value);
    public static LocationId Create(Guid value) => new(value);

    public static LocationId New() => new(Guid.CreateVersion7());

    public static LocationId From(Guid value) => new(value);

    public static LocationId Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("LocationId value cannot be empty", nameof(value));
        }

        if (!Guid.TryParse(value, out var guid))
        {
            throw new ArgumentException($"Invalid LocationId format: {value}", nameof(value));
        }

        return new LocationId(guid);
    }

    public override string ToString() => Value.ToString();
}