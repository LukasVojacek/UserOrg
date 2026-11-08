using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Core.Domain.Project;

[JsonConverter(typeof(ValueObjectJsonConverter<DomainTypeName, string>))]
public readonly record struct DomainTypeName(string Value) : IValueObject<DomainTypeName, string>
{
    static DomainTypeName IValueObject<DomainTypeName, string>.From(string value) => new(value);

    public static DomainTypeName Create(string value) =>
        TryCreate(value, out var result, out var error)
            ? result
            : throw new ArgumentException(error, nameof(value));

    /// <summary>Validates and constructs a <see cref="DomainTypeName"/> without throwing.</summary>
    public static bool TryCreate(string? value, out DomainTypeName result, [NotNullWhen(false)] out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = default;
            error = "DomainTypeName cannot be empty";
            return false;
        }

        if (value.Length > 100)
        {
            result = default;
            error = "DomainTypeName must not exceed 100 characters";
            return false;
        }

        result = new DomainTypeName(value.Trim());
        error = null;
        return true;
    }

    public override string ToString() => Value;
}