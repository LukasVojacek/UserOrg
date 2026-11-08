using System.Text.Json;
using System.Text.Json.Serialization;

namespace UserOrg.Shared;

public sealed class PermissionKeyJsonConverter : JsonConverter<PermissionKey>
{
    public override PermissionKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (s is null || !PermissionKey.TryParse(s, out var key))
            throw new JsonException($"Invalid PermissionKey value: '{s}'");

        return key;
    }

    public override void Write(Utf8JsonWriter writer, PermissionKey value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}