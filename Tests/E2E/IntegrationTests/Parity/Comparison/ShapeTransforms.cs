using System.Text.Json.Nodes;

namespace ParityTests.Comparison;

/// <summary>
/// Renames a property (wherever it appears in the tree) from <paramref name="From"/> to
/// <paramref name="To"/>. Absorbs the "v1 calls it X, v2 calls it Y" divergence.
/// </summary>
public sealed record FieldRenameTransform(string From, string To, ComparisonSide Side) : IShapeTransform
{
    /// <inheritdoc />
    public JsonNode? Apply(JsonNode? node) => JsonKeyWalk.Rebuild(node, key => key == From ? To : key);
}

/// <summary>
/// Drops a property (wherever it appears in the tree) named <paramref name="Name"/>. Absorbs the
/// "v1 exposes X, v2 does not" divergence.
/// </summary>
public sealed record FieldRemoveTransform(string Name, ComparisonSide Side) : IShapeTransform
{
    /// <inheritdoc />
    public JsonNode? Apply(JsonNode? node) => JsonKeyWalk.Rebuild(node, key => key == Name ? null : key);
}

/// <summary>
/// Renders the scalar value of every property named <paramref name="Name"/> as a JSON string. Absorbs the
/// "v1 emits a lookup's number, v2 its name" divergence while the property itself stays in the comparison.
/// </summary>
public sealed record FieldKindChangeTransform(string Name, ComparisonSide Side) : IShapeTransform
{
    /// <inheritdoc />
    public JsonNode? Apply(JsonNode? node) => JsonKeyWalk.Rebuild(
        node,
        key => key,
        (key, scalar) => key == Name ? JsonValue.Create(scalar.ToJsonString()) : scalar.DeepClone());
}

/// <summary>
/// Unwraps a value-object envelope — <c>{ "Value": x }</c> becomes <c>x</c>. When
/// <see cref="PropertyName"/> is set, only the value of a property with that name is
/// unwrapped; when it is <see langword="null"/>, every single-<c>Value</c> envelope in
/// the tree (including the root) is unwrapped.
/// </summary>
public sealed record ValueObjectEnvelopeTransform(string? PropertyName, ComparisonSide Side) : IShapeTransform
{
    private const string EnvelopeKey = "Value";

    /// <inheritdoc />
    public JsonNode? Apply(JsonNode? node) => Rebuild(node, ownerProperty: null);

    private JsonNode? Rebuild(JsonNode? node, string? ownerProperty)
    {
        switch (node)
        {
            case JsonObject obj:
                var rebuilt = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    rebuilt[key] = Rebuild(value, key);
                }

                if (IsEnvelope(rebuilt) && Matches(ownerProperty))
                {
                    var inner = rebuilt[EnvelopeKey];
                    rebuilt.Remove(EnvelopeKey);
                    return inner;
                }

                return rebuilt;

            case JsonArray array:
                var rebuiltArray = new JsonArray();
                foreach (var item in array)
                {
                    rebuiltArray.Add(Rebuild(item, ownerProperty: null));
                }

                return rebuiltArray;

            default:
                return node?.DeepClone();
        }
    }

    private bool Matches(string? ownerProperty) =>
        PropertyName is null || PropertyName == ownerProperty;

    private static bool IsEnvelope(JsonObject obj) =>
        obj.Count == 1 && obj.ContainsKey(EnvelopeKey);
}

/// <summary>The tree walk behind the key transforms: every property key goes through a mapper, and a <see langword="null"/> key drops the property. A scalar mapper, when given, rebuilds each scalar property value from its original key.</summary>
file static class JsonKeyWalk
{
    public static JsonNode? Rebuild(JsonNode? node, Func<string, string?> mapKey, Func<string, JsonValue, JsonNode>? mapScalar = null)
    {
        switch (node)
        {
            case JsonObject obj:
                var rebuiltObject = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    if (mapKey(key) is { } name)
                    {
                        rebuiltObject[name] = value is JsonValue scalar && mapScalar is not null
                            ? mapScalar(key, scalar)
                            : Rebuild(value, mapKey, mapScalar);
                    }
                }

                return rebuiltObject;

            case JsonArray array:
                var rebuiltArray = new JsonArray();
                foreach (var item in array)
                {
                    rebuiltArray.Add(Rebuild(item, mapKey, mapScalar));
                }

                return rebuiltArray;

            default:
                return node?.DeepClone();
        }
    }
}
