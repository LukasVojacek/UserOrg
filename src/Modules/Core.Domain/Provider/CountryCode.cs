using System.Text.Json.Serialization;

namespace Core.Domain.Provider;

/// <summary>
/// ISO 3166-1 alpha-2 country code value object.
/// </summary>
[JsonConverter(typeof(ValueObjectJsonConverter<CountryCode, string>))]
public readonly record struct CountryCode(string Value) : IValueObject<CountryCode, string>
{
    static CountryCode IValueObject<CountryCode, string>.From(string value) => new(value);

    public static CountryCode Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("CountryCode cannot be empty", nameof(value));
        }

        if (value.Length != 2 || !value.All(char.IsLetter))
        {
            throw new ArgumentException("CountryCode must be a 2-letter ISO code", nameof(value));
        }

        return new CountryCode(value.Trim().ToUpperInvariant());
    }

    public static CountryCode None => new(string.Empty);

    /// <summary>Creates a CountryCode from a database value, returning None if the value is null.</summary>
    public static CountryCode From(string? dbCountryCode) => dbCountryCode is null
        ? None
        : Create(dbCountryCode);

    public override string ToString() => Value;
}