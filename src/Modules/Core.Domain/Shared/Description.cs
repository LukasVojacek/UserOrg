using System.Text.Json.Serialization;

namespace Core.Domain.Shared;

[JsonConverter(typeof(ValueObjectJsonConverter<Description, string>))]
public readonly record struct Description(string Value) : IValueObject<Description, string>
{
    static Description IValueObject<Description, string>.From(string value) => new(value);

    public static Description Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length > 500)
        {
            throw new ArgumentException("Description must not exceed 500 characters");
        }
        return new Description(value.Trim());
    }

    public override string ToString() => Value ?? string.Empty;
}