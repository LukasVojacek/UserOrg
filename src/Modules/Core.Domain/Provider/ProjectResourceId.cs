using System.Text.Json.Serialization;

namespace Core.Domain.Provider;

/// <summary>
/// Strongly typed ID for ProjectResource.
/// Used for cross-aggregate references between Request and ProjectResource.
/// </summary>
[JsonConverter(typeof(ValueObjectJsonConverter<ProjectResourceId, Guid>))]
public readonly record struct ProjectResourceId(Guid Value) : IGuidId<ProjectResourceId>
{
    static ProjectResourceId IValueObject<ProjectResourceId, Guid>.From(Guid value) => new(value);
    public static ProjectResourceId Create(Guid value) => new(value);

    public static ProjectResourceId New() => new(Guid.CreateVersion7());

    public static ProjectResourceId From(Guid value) => new(value);

    public static ProjectResourceId Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("ProjectResourceId value cannot be empty", nameof(value));
        }

        if (!Guid.TryParse(value, out var guid))
        {
            throw new ArgumentException($"Invalid ProjectResourceId format: {value}", nameof(value));
        }

        return new ProjectResourceId(guid);
    }

    public override string ToString() => Value.ToString();
}