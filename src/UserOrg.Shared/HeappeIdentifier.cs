using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace UserOrg.Shared;

/// <summary>
/// The name of a HEAppE instance in the NexusApi registry, which is also its segment in the proxy path
/// <c>/api/heappe/{identifier}/</c>. Normalised (trimmed, lower case) and limited to <c>^[a-z0-9][a-z0-9-]*$</c>,
/// <see cref="MaxLength"/> characters at most, so it is safe to put in a URL path and in a cache key.
/// </summary>
/// <param name="Value">The normalised identifier. Use <see cref="TryParse"/> for outside input.</param>
[JsonConverter(typeof(HeappeIdentifierJsonConverter))]
public readonly partial record struct HeappeIdentifier(string Value)
{
    /// <summary>The longest identifier accepted by <see cref="TryParse"/>.</summary>
    public const int MaxLength = 64;

    /// <summary>Parses <paramref name="value"/> after trimming and lower-casing it.</summary>
    /// <returns><c>false</c> (and a default identifier) when the value is outside the proxy-safe shape.</returns>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "The proxy path segment is lower case by definition; this is not security normalization.")]
    public static bool TryParse([NotNullWhen(true)] string? value, out HeappeIdentifier identifier)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        var valid = ProxySafeShape().IsMatch(normalized);
        identifier = valid ? new HeappeIdentifier(normalized) : default;
        return valid;
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    // First and last character must be alphanumeric; '_' and '-' are allowed in between, but not at the
    // start or end. {0,62} for the middle group keeps the total length at MaxLength (64) characters at most.
    // \z, not $, so a trailing newline is rejected.
    [GeneratedRegex(@"^[a-z0-9]([a-z0-9_-]{0,62}[a-z0-9])?\z", RegexOptions.CultureInvariant)]
    private static partial Regex ProxySafeShape();
}

/// <summary>Writes the identifier as a plain JSON string, which HybridCache needs to serialise cached snapshots.</summary>
internal sealed class HeappeIdentifierJsonConverter : JsonConverter<HeappeIdentifier>
{
    public override HeappeIdentifier Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        HeappeIdentifier.TryParse(reader.GetString(), out var identifier)
            ? identifier
            : throw new JsonException("Not a valid HEAppE identifier.");

    public override void Write(Utf8JsonWriter writer, HeappeIdentifier value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}