using System.Text.Json.Serialization;

namespace Core.Domain.User;

/// <summary>
/// Strongly typed ID for User aggregate root.
/// Prevents type confusion between different ID types at compile-time.
/// </summary>
[JsonConverter(typeof(ValueObjectJsonConverter<UserId, Guid>))]
public readonly record struct UserId(Guid Value) : IGuidId<UserId>
{
    static UserId IValueObject<UserId, Guid>.From(Guid value) => new(value);
    public static UserId Create(Guid value) => new(value);

    public static UserId New() => new(Guid.CreateVersion7());

    public static UserId From(Guid value) => new(value);

    public static UserId Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("UserId value cannot be empty", nameof(value));
        }

        if (!Guid.TryParse(value, out var guid))
        {
            throw new ArgumentException($"Invalid UserId format: {value}", nameof(value));
        }

        return new UserId(guid);
    }

    public override string ToString() => Value.ToString();
}