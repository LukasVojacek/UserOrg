using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Core.Shared;

/// <summary>
/// Email address value object for business identifiers (not cross-aggregate refs).
/// Used for User.Email, Project.Email, Provider.SupportEmail, Request.ContactEmail, etc.
/// </summary>
[JsonConverter(typeof(ValueObjectJsonConverter<Email, string>))]
public readonly partial record struct Email(string Value) : IValueObject<Email, string>, IParsable<Email>
{
    static Email IValueObject<Email, string>.From(string value) => new(value);
    public const int MaxLenght = 100;

    [GeneratedRegex(@"^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    public static Email Create(string emailAddress)
    {
        if (string.IsNullOrWhiteSpace(emailAddress))
        {
            throw new ArgumentException("Email cannot be empty", nameof(emailAddress));
        }

        var trimmed = emailAddress.Trim();

        if (trimmed.Length > MaxLenght)
        {
            throw new ArgumentException($"Email must not exceed {MaxLenght} characters", nameof(emailAddress));
        }

        if (!EmailRegex().IsMatch(trimmed))
        {
            throw new ArgumentException("Invalid email format", nameof(emailAddress));
        }

        return new Email(trimmed.ToLowerInvariant());
    }

    public override string ToString() => Value;

    public static Email Parse(string s, IFormatProvider? provider) => Create(s);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Email result)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            result = default;
            return false;
        }

        var trimmed = s.Trim();

        if (trimmed.Length > MaxLenght || !EmailRegex().IsMatch(trimmed))
        {
            result = default;
            return false;
        }

        result = new Email(trimmed.ToLowerInvariant());
        return true;
    }
}