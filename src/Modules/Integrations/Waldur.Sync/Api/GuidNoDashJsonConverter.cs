using System.Text.Json;
using System.Text.Json.Serialization;

namespace Waldur.Sync.Api;

/// <summary>
/// JsonConverter that accepts GUIDs both with and without dashes ("N" and "D" formats).
/// Ported from the legacy <c>WaldurModels.GuidNoDashJsonConverter</c>.
/// </summary>
public sealed class GuidNoDashJsonConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (string.IsNullOrEmpty(s))
            {
                return default;
            }

            if (Guid.TryParse(s, out var g))
            {
                return g;
            }

            if (s.Length == 32 && Guid.TryParseExact(s, "N", out g))
            {
                return g;
            }

            throw new JsonException($"Invalid GUID: {s}");
        }

        throw new JsonException("Expected string for GUID");
    }

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString("D"));
    }
}