using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Core.Domain.User;

[JsonConverter(typeof(ValueObjectJsonConverter<IdpId, string>))]
public readonly record struct IdpId(string Value) : IValueObject<IdpId, string>
{
    static IdpId IValueObject<IdpId, string>.From(string value) => new(value);
    public const int MaxLenght = 150;

    public static IdpId Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identity Provider User ID cannot be empty", nameof(value));
        }
        if (value.Length is < 3 or > MaxLenght)
        {
            throw new ArgumentException("Identity Provider User ID must be between 3 and 150 characters", nameof(value));
        }
        return new IdpId(value.Trim());
    }

    public static IdpId From(string value) => Create(value);

    public override string ToString() => Value;

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out IdpId result)
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

        result = new IdpId(trimmed);
        return true;
    }
}