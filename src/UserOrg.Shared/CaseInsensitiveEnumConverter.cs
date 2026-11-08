using System.Text.Json;
using System.Text.Json.Serialization;

namespace UserOrg.Shared;

/// <summary>
/// Case-insensitive JSON converter for enums
/// </summary>
public class CaseInsensitiveEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var enumText = reader.GetString();
            if (enumText != null && Enum.TryParse<TEnum>(enumText, ignoreCase: true, out var value))
            {
                return value;
            }
            throw new JsonException($"Unable to convert \"{enumText}\" to enum type {typeof(TEnum).Name}");
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            var enumValue = reader.GetInt32();
            return (TEnum)Enum.ToObject(typeof(TEnum), enumValue);
        }

        throw new JsonException($"Unexpected token type {reader.TokenType} when parsing enum");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}