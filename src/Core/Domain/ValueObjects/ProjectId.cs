using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UserOrganizationManagement.Domain.ValueObjects;

[TypeConverter(typeof(ProjectIdTypeConverter))]
[JsonConverter(typeof(ProjectIdJsonConverter))]
public partial record struct ProjectId(Guid Value) : IParsable<ProjectId>
{
    public static implicit operator Guid(ProjectId id) => id.Value;
    public static explicit operator ProjectId(Guid? value) => value is null ? Empty : new ProjectId(value.Value);

    public static ProjectId Create(Guid Value) => new(Value);
    public static ProjectId Create(string Value) => new(Guid.Parse(Value));
    public static ProjectId Empty => new(Guid.Empty);
    public static ProjectId DefaultUsrOrgProject { get; } = Create("00000000-0000-0000-0000-000000000001");

    public static ProjectId Parse(string s, IFormatProvider? provider) => Create(s);

    public static bool TryParse(string input, out ProjectId projectId)
    {
        var guid = Guid.TryParse(input, out Guid inputGuid);
        if (guid)
        {
            projectId = Create(inputGuid);
            return true;
        }

        projectId = Empty;
        return false;
    }
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out ProjectId result)
    {
        if (Guid.TryParse(s, out Guid resultGuid))
        {
            result = ProjectId.Create(resultGuid);
            return true;
        }

        result = Empty;
        return false;
    }

    private sealed class ProjectIdTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            var stringValue = value as string;
            return TryParse(stringValue, out ProjectId result) ? result : base.ConvertFrom(context, culture, value);
        }
    }
    private sealed class ProjectIdJsonConverter : JsonConverter<ProjectId>
    {
        public override ProjectId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Guid guid = reader.GetGuid();
            return ProjectId.Create(guid);
        }

        public override void Write(Utf8JsonWriter writer, ProjectId value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    }

    public readonly Guid ToGuid() => throw new NotImplementedException();

    public static ProjectId ToProjectId(ProjectId left, ProjectId right) => throw new NotImplementedException();
}