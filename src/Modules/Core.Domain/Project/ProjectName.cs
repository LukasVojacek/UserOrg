using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Core.Domain.Project;

/// <summary>
/// Project name value object (business identifier, not necessarily unique).
/// </summary>
[JsonConverter(typeof(ValueObjectJsonConverter<ProjectName, string>))]
public readonly record struct ProjectName(string Value) : IValueObject<ProjectName, string>
{
    static ProjectName IValueObject<ProjectName, string>.From(string value) => new(value);

    public static ProjectName Create(string value) =>
        TryCreate(value, out var result, out var error)
            ? result
            : throw new ArgumentException(error, nameof(value));

    /// <summary>Validates and constructs a <see cref="ProjectName"/> without throwing.</summary>
    public static bool TryCreate(string? value, out ProjectName result, [NotNullWhen(false)] out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = default;
            error = "ProjectName cannot be empty";
            return false;
        }

        if (value.Length > 100)
        {
            result = default;
            error = "ProjectName must not exceed 100 characters";
            return false;
        }

        result = new ProjectName(value.Trim());
        error = null;
        return true;
    }

    public static ProjectName From(string value) => Create(value);

    public override string ToString() => Value;
}