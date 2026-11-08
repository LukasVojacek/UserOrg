using System.Text.Json.Serialization;

namespace Core.Domain.Provider;

/// <summary>
/// Strongly typed ID for Provider aggregate root.
/// Prevents type confusion between different ID types at compile-time.
/// </summary>
[JsonConverter(typeof(ValueObjectJsonConverter<ProviderId, Guid>))]
public readonly record struct ProviderId(Guid Value) : IGuidId<ProviderId>
{
    static ProviderId IValueObject<ProviderId, Guid>.From(Guid value) => new(value);
    public static ProviderId Create(Guid value) => new(value);

    public static ProviderId New() => new(Guid.CreateVersion7());

    public static ProviderId From(Guid value) => new(value);

    public static ProviderId Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("ProviderId value cannot be empty", nameof(value));
        }

        if (!Guid.TryParse(value, out var guid))
        {
            throw new ArgumentException($"Invalid ProviderId format: {value}", nameof(value));
        }

        return new ProviderId(guid);
    }

    public override string ToString() => Value.ToString();
}