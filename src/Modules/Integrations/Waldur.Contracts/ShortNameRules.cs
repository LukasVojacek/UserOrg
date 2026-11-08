using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Waldur.Contracts;

/// <summary>
/// String extensions enforcing the text rules Waldur payloads must satisfy: control characters
/// are never part of a legitimate value, and a short name is an identifier restricted to a
/// narrow ASCII charset and a maximum length.
/// </summary>
public static class ShortNameRules
{
    /// <summary>
    /// The maximum length a valid short name may have.
    /// </summary>
    public const int MaxLength = 50;

    private static readonly SearchValues<char> AllowedShortNameCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    /// <summary>
    /// Removes control and format characters (C0/C1, BOM, zero-width and bidi marks) from the text.
    /// Printable non-ASCII characters (diacritics, dashes) are preserved.
    /// </summary>
    /// <param name="text">The text to strip.</param>
    /// <returns>The text without control and format characters, or the same instance when it holds none.</returns>
    public static string RemoveControlCharacters(this string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var printableCount = 0;
        foreach (var character in text)
        {
            if (IsPrintable(character))
            {
                printableCount++;
            }
        }

        return printableCount == text.Length
            ? text
            : string.Create(printableCount, text, static (destination, source) =>
            {
                var written = 0;
                foreach (var character in source)
                {
                    if (IsPrintable(character))
                    {
                        destination[written++] = character;
                    }
                }
            });
    }

    private static bool IsPrintable(char character)
        => !char.IsControl(character) && char.GetUnicodeCategory(character) != UnicodeCategory.Format;

    /// <summary>
    /// Determines whether the value can be used as a short name as-is: non-empty, at most
    /// <see cref="MaxLength"/> characters, and restricted to ASCII letters, digits, '-' and '_'.
    /// </summary>
    /// <param name="value">The candidate short name.</param>
    /// <returns><see langword="true"/> when the value is a valid short name.</returns>
    public static bool IsValidShortName([NotNullWhen(true)] this string? value)
        => !string.IsNullOrEmpty(value)
            && value.Length <= MaxLength
            && !value.AsSpan().ContainsAnyExcept(AllowedShortNameCharacters);
}