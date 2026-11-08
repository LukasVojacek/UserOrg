using System.Buffers;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using UserOrg.Shared.Persistance;

namespace UserOrganizationManagement.Domain.ValueObjects;

[TypeConverter(typeof(ProjectShortNameTypeConverter))]
[JsonConverter(typeof(ProjectShortNameJsonConverter))]
public record struct ProjectShortName(string Value) : IParsable<ProjectShortName>
{
    public static implicit operator string(ProjectShortName shortName) => shortName.Value;
    public static explicit operator ProjectShortName(string? value) => value is null ? Empty : new ProjectShortName(value);

    public static ProjectShortName Create(string Value) => new(Value);
    public static ProjectShortName Empty => new(string.Empty);

    private static readonly SearchValues<char> AllowedCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    /// <summary>
    /// A short name is an identifier: it travels in URL paths, response headers and log lines, so it is limited
    /// to ASCII letters, digits, '-' and '_' and to <see cref="DbConstants.ProjectShortNameMaxLength"/> characters.
    /// </summary>
    /// <param name="value">The candidate short name.</param>
    /// <returns><see langword="true"/> when the value can be used as a short name as-is.</returns>
    public static bool IsValid([NotNullWhen(true)] string? value)
        => !string.IsNullOrEmpty(value)
            && value.Length <= DbConstants.ProjectShortNameMaxLength
            && !value.AsSpan().ContainsAnyExcept(AllowedCharacters);

    /// <summary>
    /// Creates a short name from an external identifier, folding it to the shape <see cref="IsValid"/> accepts:
    /// diacritics are decomposed to their base letter, everything else outside the allowed set is dropped and the
    /// result is capped at <see cref="DbConstants.ProjectShortNameMaxLength"/> characters. An already valid value
    /// is used unchanged.
    /// </summary>
    /// <param name="value">The external identifier, typically a slug from the source system.</param>
    /// <returns>The normalized short name.</returns>
    public static ProjectShortName CreateNormalized(string? value)
    {
        if (IsValid(value))
        {
            return Create(value);
        }

        var decomposed = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var buffer = new char[Math.Min(decomposed.Length, DbConstants.ProjectShortNameMaxLength)];
        var written = 0;

        foreach (var character in decomposed)
        {
            if (written == buffer.Length)
            {
                break;
            }

            if (AllowedCharacters.Contains(character))
            {
                buffer[written++] = character;
            }
        }

        return Create(new string(buffer, 0, written));
    }

    public static bool TryParse(string input, out ProjectShortName ProjectShortName)
    {
        ProjectShortName = Create(input);

        return !string.IsNullOrEmpty(input);
    }

    private sealed class ProjectShortNameTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            var stringValue = value as string;
            return TryParse(stringValue, out ProjectShortName result) ? result : base.ConvertFrom(context, culture, value);
        }
    }
    private sealed class ProjectShortNameJsonConverter : JsonConverter<ProjectShortName>
    {
        public override ProjectShortName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Create(reader.GetString());

        public override void Write(Utf8JsonWriter writer, ProjectShortName value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    }

    public static ProjectShortName ToProjectShortName(ProjectShortName left, ProjectShortName right) => throw new NotImplementedException();
    public static ProjectShortName Parse(string s, IFormatProvider? provider) => Create(s);
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out ProjectShortName result)
    {
        result = ProjectShortName.Create(s);
        return true;
    }
}