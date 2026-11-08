using System.Text.Json.Serialization;

namespace Core.Domain.Project;

/// <summary>
/// Strongly typed ID for Project aggregate root.
/// Prevents type confusion between different ID types at compile-time.
/// </summary>
[JsonConverter(typeof(ValueObjectJsonConverter<ProjectId, Guid>))]
public readonly record struct ProjectId(Guid Value) : IGuidId<ProjectId>
{
    static ProjectId IValueObject<ProjectId, Guid>.From(Guid value) => new(value);
    public static ProjectId Create(Guid value) => new(value);

    public static ProjectId New() => new(Guid.CreateVersion7());

    public static ProjectId From(Guid value) => new(value);

    public static ProjectId Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("ProjectId value cannot be empty", nameof(value));
        }

        if (!Guid.TryParse(value, out var guid))
        {
            throw new ArgumentException($"Invalid ProjectId format: {value}", nameof(value));
        }

        return new ProjectId(guid);
    }

    public override string ToString() => Value.ToString();
}