using System.Globalization;
using System.Text.Json.Serialization;

namespace Core.Domain.Project;

[JsonConverter(typeof(ValueObjectJsonConverter<ProjectDomainTypeId, int>))]
public readonly record struct ProjectDomainTypeId(int Value) : IValueObject<ProjectDomainTypeId, int>
{
    static ProjectDomainTypeId IValueObject<ProjectDomainTypeId, int>.From(int value) => new(value);
    public static ProjectDomainTypeId Create(int domainTypeId) => new(domainTypeId);
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}