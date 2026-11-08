using System.Text.Json;
using System.Text.Json.Nodes;

namespace ParityTests.Comparison;

/// <summary>
/// A single flagged structural difference: the JSON path where it occurred and a
/// human-readable reason (kind mismatch, missing/extra property, array length, or a
/// must-match value mismatch).
/// </summary>
public sealed record ShapeMismatch(string Path, string Reason)
{
    /// <inheritdoc />
    public override string ToString() => $"{Path}: {Reason}";
}

/// <summary>
/// The outcome of a <see cref="JsonShapeComparer"/> run. <see cref="IsMatch"/> is true
/// only when no mismatches were recorded.
/// </summary>
public sealed class JsonShapeComparisonResult
{
    internal JsonShapeComparisonResult(IReadOnlyList<ShapeMismatch> mismatches) => Mismatches = mismatches;

    /// <summary>The flagged differences; empty when the shapes match.</summary>
    public IReadOnlyList<ShapeMismatch> Mismatches { get; }

    /// <summary>True when the two shapes are equivalent under the given rules.</summary>
    public bool IsMatch => Mismatches.Count == 0;

    /// <summary>A newline-separated, path-annotated diff for assertion messages.</summary>
    public string ToDiffString() =>
        IsMatch ? "<shapes match>" : string.Join(Environment.NewLine, Mismatches);

    /// <summary>Throws <see cref="JsonShapeMismatchException"/> when not a match.</summary>
    public void ThrowIfMismatch()
    {
        if (!IsMatch)
        {
            throw new JsonShapeMismatchException(this);
        }
    }
}

/// <summary>Thrown by <see cref="JsonShapeComparisonResult.ThrowIfMismatch"/>.</summary>
public sealed class JsonShapeMismatchException(JsonShapeComparisonResult result)
    : Exception($"JSON shapes diverged:{Environment.NewLine}{result.ToDiffString()}")
{
    /// <summary>The result that triggered the throw.</summary>
    public JsonShapeComparisonResult Result { get; } = result;
}

/// <summary>
/// Recursive <em>structural</em> comparer over two <see cref="JsonElement"/> values,
/// the core of the F7 v1↔v2 parity suite (ADR-0055 D1/D5).
/// </summary>
/// <remarks>
/// <para>
/// Objects are compared by property-name set and, per common property, recursively by
/// <see cref="JsonValueKind"/> — <b>not</b> scalar value. Arrays are compared by element
/// count and per-element structure <b>in order</b> (index-by-index), which is how the
/// suite proves ORDER BY parity. Scalar values are deliberately ignored (GUIDs and
/// timestamps differ legitimately between the two stacks) <em>except</em> for the named
/// <c>mustMatchFields</c> — ordering keys / natural keys — whose values must be equal.
/// </para>
/// <para>
/// Declared divergences are absorbed by <see cref="IShapeTransform"/>s applied to one or
/// both sides <em>before</em> comparison. Route re-shape is handled at the spec/path
/// level and HTTP-status remap at the status-compare level — neither belongs here.
/// </para>
/// </remarks>
public static class JsonShapeComparer
{
    /// <summary>
    /// Compares <paramref name="expected"/> (v1) with <paramref name="actual"/> (v2).
    /// </summary>
    /// <param name="expected">The v1 response element.</param>
    /// <param name="actual">The v2 response element.</param>
    /// <param name="mustMatchFields">
    /// Property names whose <em>values</em> must be equal wherever they appear (ordering
    /// keys / natural keys). Structural comparison still applies to everything else.
    /// </param>
    /// <param name="transforms">
    /// Declared divergence transforms applied to the relevant side(s) before comparing.
    /// </param>
    public static JsonShapeComparisonResult Compare(
        JsonElement expected,
        JsonElement actual,
        IReadOnlyCollection<string>? mustMatchFields = null,
        IReadOnlyCollection<IShapeTransform>? transforms = null)
    {
        var expectedNode = JsonNode.Parse(expected.GetRawText());
        var actualNode = JsonNode.Parse(actual.GetRawText());

        if (transforms is not null)
        {
            foreach (var transform in transforms)
            {
                if (transform.Side is ComparisonSide.V1 or ComparisonSide.Both)
                {
                    expectedNode = transform.Apply(expectedNode);
                }

                if (transform.Side is ComparisonSide.V2 or ComparisonSide.Both)
                {
                    actualNode = transform.Apply(actualNode);
                }
            }
        }

        using var expectedDoc = JsonDocument.Parse(expectedNode?.ToJsonString() ?? "null");
        using var actualDoc = JsonDocument.Parse(actualNode?.ToJsonString() ?? "null");

        var mustMatch = mustMatchFields is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(mustMatchFields, StringComparer.Ordinal);

        var mismatches = new List<ShapeMismatch>();
        CompareElement(expectedDoc.RootElement, actualDoc.RootElement, "$", mustMatch, mismatches);

        return new JsonShapeComparisonResult(mismatches);
    }

    private static void CompareElement(
        JsonElement expected,
        JsonElement actual,
        string path,
        HashSet<string> mustMatch,
        List<ShapeMismatch> mismatches)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            mismatches.Add(new ShapeMismatch(
                path,
                $"kind mismatch: expected {expected.ValueKind}, actual {actual.ValueKind}"));
            return;
        }

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                CompareObject(expected, actual, path, mustMatch, mismatches);
                break;

            case JsonValueKind.Array:
                CompareArray(expected, actual, path, mustMatch, mismatches);
                break;

            default:
                // Scalar kinds match structurally once their kinds agree; individual
                // scalar values are only asserted via must-match at the property level.
                break;
        }
    }

    private static void CompareObject(
        JsonElement expected,
        JsonElement actual,
        string path,
        HashSet<string> mustMatch,
        List<ShapeMismatch> mismatches)
    {
        var expectedProps = PropertyNames(expected);
        var actualProps = PropertyNames(actual);

        foreach (var missing in expectedProps.Except(actualProps).Order(StringComparer.Ordinal))
        {
            mismatches.Add(new ShapeMismatch($"{path}.{missing}", $"missing property '{missing}' on actual"));
        }

        foreach (var extra in actualProps.Except(expectedProps).Order(StringComparer.Ordinal))
        {
            mismatches.Add(new ShapeMismatch($"{path}.{extra}", $"unexpected property '{extra}' on actual"));
        }

        foreach (var name in expectedProps.Intersect(actualProps).Order(StringComparer.Ordinal))
        {
            var childPath = $"{path}.{name}";
            var expectedChild = expected.GetProperty(name);
            var actualChild = actual.GetProperty(name);

            if (mustMatch.Contains(name) && !JsonValuesEqual(expectedChild, actualChild))
            {
                mismatches.Add(new ShapeMismatch(
                    childPath,
                    $"must-match value mismatch: expected {Render(expectedChild)}, actual {Render(actualChild)}"));
            }

            CompareElement(expectedChild, actualChild, childPath, mustMatch, mismatches);
        }
    }

    private static void CompareArray(
        JsonElement expected,
        JsonElement actual,
        string path,
        HashSet<string> mustMatch,
        List<ShapeMismatch> mismatches)
    {
        var expectedLength = expected.GetArrayLength();
        var actualLength = actual.GetArrayLength();

        if (expectedLength != actualLength)
        {
            mismatches.Add(new ShapeMismatch(
                path,
                $"array length mismatch: expected {expectedLength}, actual {actualLength}"));
        }

        var shared = Math.Min(expectedLength, actualLength);
        for (var i = 0; i < shared; i++)
        {
            CompareElement(expected[i], actual[i], $"{path}[{i}]", mustMatch, mismatches);
        }
    }

    private static bool JsonValuesEqual(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var aProps = OrderedProperties(a);
                var bProps = OrderedProperties(b);
                if (aProps.Count != bProps.Count)
                {
                    return false;
                }

                for (var i = 0; i < aProps.Count; i++)
                {
                    if (aProps[i].Name != bProps[i].Name || !JsonValuesEqual(aProps[i].Value, bProps[i].Value))
                    {
                        return false;
                    }
                }

                return true;

            case JsonValueKind.Array:
                if (a.GetArrayLength() != b.GetArrayLength())
                {
                    return false;
                }

                for (var i = 0; i < a.GetArrayLength(); i++)
                {
                    if (!JsonValuesEqual(a[i], b[i]))
                    {
                        return false;
                    }
                }

                return true;

            case JsonValueKind.String:
                return a.GetString() == b.GetString();

            case JsonValueKind.Number:
                return a.GetRawText() == b.GetRawText();

            default:
                // True / False / Null — kind equality already proved value equality.
                return true;
        }
    }

    private static HashSet<string> PropertyNames(JsonElement element)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            names.Add(property.Name);
        }

        return names;
    }

    private static List<JsonProperty> OrderedProperties(JsonElement element)
    {
        var properties = new List<JsonProperty>();
        foreach (var property in element.EnumerateObject())
        {
            properties.Add(property);
        }

        properties.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
        return properties;
    }

    private static string Render(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => $"\"{element.GetString()}\"",
        JsonValueKind.Object or JsonValueKind.Array => element.GetRawText(),
        _ => element.GetRawText(),
    };
}
