using System.Collections;
using System.Globalization;

namespace UserOrg.Shared.Hashing;

/// <summary>
/// Renders leaf values into the canonical, order- and culture-independent grammar that
/// <c>IdentityHashGenerator</c>-emitted code feeds to <see cref="IdentityHash.Sha256Hex"/>. Boxing a
/// known, closed set of BCL scalar types and pattern-matching on it is ordinary C#, not reflection —
/// there is no <c>GetProperties()</c>/<c>Invoke()</c> anywhere in this type.
/// </summary>
public static class CanonicalFormat
{
    /// <summary>
    /// Formats a scalar leaf value. Supports <see cref="Guid"/>, <see cref="DateTime"/>,
    /// <see cref="DateTimeOffset"/>, <see cref="bool"/>, <see cref="string"/>, <see cref="decimal"/>,
    /// <see cref="double"/>, <see cref="float"/>, enums, and other primitives — including their
    /// nullable forms, which box to the same cases or to <see langword="null"/>.
    /// </summary>
    public static string Value(object? value) => value switch
    {
        null => "null",

        // GUIDs are pinned to lowercase "D" (dashed) form.
        Guid guid => Quoted(guid.ToString("D", CultureInfo.InvariantCulture)),

        // Dates are UTC, rounded to second precision.
        DateTime dateTime => Quoted(FormatUtcSeconds(dateTime)),
        DateTimeOffset dateTimeOffset => Quoted(FormatUtcSeconds(dateTimeOffset.UtcDateTime)),

        bool boolean => boolean ? "true" : "false",
        string text => EscapedQuoted(text),

        // Decimals/floats are serialized with invariant culture.
        decimal dec => Quoted(dec.ToString(CultureInfo.InvariantCulture)),
        double dbl => Quoted(dbl.ToString("R", CultureInfo.InvariantCulture)),
        float flt => Quoted(flt.ToString("R", CultureInfo.InvariantCulture)),

        Enum enumValue => EscapedQuoted(enumValue.ToString()),

        IFormattable formattable when value.GetType().IsPrimitive =>
            Quoted(formattable.ToString(null, CultureInfo.InvariantCulture)),

        _ => throw new NotSupportedException(
            $"CanonicalFormat.Value does not support '{value.GetType()}'. Mark the property [HashIgnore], implement IIdentityHashed on it, or add support to CanonicalFormat."),
    };

    /// <summary>Formats a collection of plain scalar values: null and empty are the same value, order does not matter.</summary>
    public static string ScalarList(IEnumerable? values) =>
        values is null ? "[]" : RenderSortedList(values.Cast<object?>(), Value);

    /// <summary>Composes a nested identity-hashed value by its own digest; null stays null.</summary>
    public static string Hashed(IIdentityHashed? value) =>
        value is null ? "null" : Quoted(value.ComputeIdentityHash());

    /// <summary>Composes a collection of identity-hashed values by their own digests: null and empty are the same value, order does not matter.</summary>
    public static string HashedList<T>(IEnumerable<T>? values)
        where T : IIdentityHashed =>
        values is null ? "[]" : RenderSortedList(values, v => Quoted(v.ComputeIdentityHash()));

    // Shared by ScalarList/HashedList: render each element once, sort for order-independence, join.
    private static string RenderSortedList<T>(IEnumerable<T> values, Func<T, string> render)
    {
        var rendered = new List<string>();
        foreach (var element in values)
        {
            rendered.Add(render(element));
        }

        rendered.Sort(StringComparer.Ordinal);
        return $"[{string.Join(",", rendered)}]";
    }

    private static string Quoted(string value) => $"\"{value}\"";

    private static string EscapedQuoted(string value)
    {
        // Escape the two characters that could otherwise blur the boundary between values.
        var builder = new System.Text.StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var c in value)
        {
            if (c is '"' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static string FormatUtcSeconds(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
        var truncated = utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerSecond));
        return truncated.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }
}