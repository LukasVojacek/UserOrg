using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Serialization;

namespace Core.Domain.Project;

/// <summary>
/// Project short name value object (natural identifier, unique constraint).
/// </summary>
[JsonConverter(typeof(ValueObjectJsonConverter<ProjectShortName, string>))]
public readonly record struct ProjectShortName(string Value) : IValueObject<ProjectShortName, string>, IParsable<ProjectShortName>
{
    private const int MaxLength = 50;

    private static readonly SearchValues<char> AllowedCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    static ProjectShortName IValueObject<ProjectShortName, string>.From(string value) => new(value);

    /// <summary>
    /// A short name is an identifier: ASCII letters, digits, '-' and '_' only, between 2 and
    /// <see cref="MaxLength"/> characters — the same bounds enforced by <see cref="Create"/>.
    /// </summary>
    /// <param name="value">The candidate short name.</param>
    /// <returns><see langword="true"/> when the value can be used as a short name as-is.</returns>
    public static bool IsValid([NotNullWhen(true)] string? value)
        => !string.IsNullOrEmpty(value)
            && value.Length is >= 2 and <= MaxLength
            && !value.AsSpan().ContainsAnyExcept(AllowedCharacters);

    /// <summary>
    /// Creates a short name from an external identifier, folding it to the shape <see cref="IsValid"/> accepts:
    /// diacritics are decomposed to their base letter, everything else outside the allowed set is dropped and the
    /// result is capped at <see cref="MaxLength"/> characters. The folded value is then handed to
    /// <see cref="Create"/>, which still throws below 2 characters.
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
        var buffer = new char[Math.Min(decomposed.Length, MaxLength)];
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

    public static ProjectShortName Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("ProjectShortName cannot be empty", nameof(value));
        }

        if (value.Length is < 2 or > 50)
        {
            throw new ArgumentException("ProjectShortName must be between 2 and 50 characters", nameof(value));
        }

        return new ProjectShortName(value.Trim());
    }

    public static ProjectShortName From(string value) => Create(value);

    public static ProjectShortName Empty => new(string.Empty);

    public override string ToString() => Value;

    public static ProjectShortName Parse(string s, IFormatProvider? provider) => Create(s);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out ProjectShortName result)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            result = default;
            return false;
        }

        if (s.Length is < 2 or > 50)
        {
            result = default;
            return false;
        }

        result = new ProjectShortName(s.Trim());
        return true;
    }
}