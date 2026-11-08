using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Core.Domain.Project;

[JsonConverter(typeof(ValueObjectJsonConverter<TagName, string>))]
public readonly record struct TagName(string Value) : IValueObject<TagName, string>
{
    static TagName IValueObject<TagName, string>.From(string value) => new(value);

    public static TagName Create(string value) =>
        TryCreate(value, out var result, out var error)
            ? result
            : throw new ArgumentException(error, nameof(value));

    /// <summary>Validates and constructs a <see cref="TagName"/> without throwing.</summary>
    public static bool TryCreate(string? value, out TagName result, [NotNullWhen(false)] out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = default;
            error = "TagName cannot be empty";
            return false;
        }

        if (value.Length > 100)
        {
            result = default;
            error = "TagName must not exceed 100 characters";
            return false;
        }

        result = new TagName(value.Trim());
        error = null;
        return true;
    }

    public static TagName From(string value) => Create(value);

    public override string ToString() => Value;
}