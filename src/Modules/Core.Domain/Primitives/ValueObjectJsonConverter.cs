using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Domain.Primitives;

public sealed class ValueObjectJsonConverter<TSelf, TValue> : JsonConverter<TSelf>
    where TSelf : struct, IValueObject<TSelf, TValue>
{
    public override TSelf Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => TSelf.From(JsonSerializer.Deserialize<TValue>(ref reader, options)!);

    public override void Write(Utf8JsonWriter writer, TSelf value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value.Value, options);
}