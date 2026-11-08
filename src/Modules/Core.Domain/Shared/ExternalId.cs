using System.Text.Json.Serialization;

namespace Core.Domain.Shared;

[JsonConverter(typeof(ValueObjectJsonConverter<ExternalId, string>))]
public readonly record struct ExternalId(string Value) : IValueObject<ExternalId, string>
{
    static ExternalId IValueObject<ExternalId, string>.From(string value) => new(value);

    public static ExternalId Create(string Value)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            throw new ArgumentException("ExternalId cannot be empty", nameof(Value));
        }
        if (Value.Length > 200)
        {
            throw new ArgumentException("ExternalId must not exceed 200 characters", nameof(Value));
        }

        return new ExternalId(Value.Trim());
    }

    public static ExternalId None => new(string.Empty);

    public override string ToString() => Value;
}