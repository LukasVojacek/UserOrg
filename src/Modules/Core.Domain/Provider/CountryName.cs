using System.Text.Json.Serialization;

namespace Core.Domain.Provider;

/// <summary>
/// Country name value object (natural identifier, unique constraint).
/// </summary>
[JsonConverter(typeof(ValueObjectJsonConverter<CountryName, string>))]
public readonly record struct CountryName(string Value) : IValueObject<CountryName, string>
{
    static CountryName IValueObject<CountryName, string>.From(string value) => new(value);

    public static CountryName Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("CountryName cannot be empty", nameof(value));
        }

        if (value.Length > 100)
        {
            throw new ArgumentException("CountryName must not exceed 100 characters", nameof(value));
        }

        return new CountryName(value.Trim());
    }

    public static CountryName From(string value) => Create(value);

    public override string ToString() => Value;
}