using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Core.Domain.User;

/// <summary>
/// User name value object (natural identifier for User aggregate).
/// </summary>
[JsonConverter(typeof(ValueObjectJsonConverter<UserName, string>))]
public readonly record struct UserName(string Value) : IValueObject<UserName, string>
{
    static UserName IValueObject<UserName, string>.From(string value) => new(value);
    public const int MaxLenght = 100;

    public static UserName Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Username cannot be empty", nameof(value));
        }

        if (value.Length is < 3 or > MaxLenght)
        {
            throw new ArgumentException("Username must be between 3 and 100 characters", nameof(value));
        }

        return new UserName(value.Trim());
    }

    public static UserName From(string value) => Create(value);

    public override string ToString() => Value;

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out UserName result)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            result = default;
            return false;
        }

        var trimmed = s.Trim();

        if (trimmed.Length is < 3 or > MaxLenght)
        {
            result = default;
            return false;
        }

        result = new UserName(trimmed);
        return true;
    }
}