using System.Text.Json.Serialization;

namespace Core.Domain.Provider;

[JsonConverter(typeof(ValueObjectJsonConverter<ServiceName, string>))]
public readonly record struct ServiceName(string Value) : IValueObject<ServiceName, string>
{
    static ServiceName IValueObject<ServiceName, string>.From(string value) => new(value);

    public static ServiceName Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("ServiceName cannot be empty", nameof(value));
        }
        if (value.Length > 100)
        {
            throw new ArgumentException("ServiceName must not exceed 100 characters", nameof(value));
        }
        return new ServiceName(value.Trim());
    }

    /// <summary>Default service name value object - "LEXIS".</summary>
    public static ServiceName Default => Create("LEXIS");

    public override string ToString() => Value;
}