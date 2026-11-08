using System.Text.Json.Serialization;

namespace Core.Domain.Request;

/// <summary>
/// Strongly typed ID for Request aggregate root.
/// Prevents type confusion between different ID types at compile-time.
/// </summary>
[JsonConverter(typeof(ValueObjectJsonConverter<RequestId, Guid>))]
public readonly record struct RequestId(Guid Value) : IGuidId<RequestId>
{
    static RequestId IValueObject<RequestId, Guid>.From(Guid value) => new(value);
    public static RequestId Create(Guid value) => new(value);

    public static RequestId New() => new(Guid.CreateVersion7());

    public static RequestId From(Guid value) => new(value);

    public static RequestId Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("RequestId value cannot be empty", nameof(value));
        }

        if (!Guid.TryParse(value, out var guid))
        {
            throw new ArgumentException($"Invalid RequestId format: {value}", nameof(value));
        }

        return new RequestId(guid);
    }

    public override string ToString() => Value.ToString();
}