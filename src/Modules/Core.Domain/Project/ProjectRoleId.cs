using System.Globalization;
using System.Text.Json.Serialization;

namespace Core.Domain.Project;

[JsonConverter(typeof(ValueObjectJsonConverter<ProjectRoleId, int>))]
public readonly record struct ProjectRoleId : IValueObject<ProjectRoleId, int>
{
    public int Value { get; }
    public ProjectRoleId(int value) => Value = value;

    static ProjectRoleId IValueObject<ProjectRoleId, int>.From(int value) => new(value);
    public static ProjectRoleId Create(int value) => new(value);

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}