using System.Text.Json;

namespace UserOrganizationManagement.Application.Tests;

// Contract bar for the v2 API: a 200 body is always an object or an array, never a bare scalar.
// An object body can gain a field without breaking clients; a scalar cannot, so every v2 endpoint
// returns a Response record even when it carries a single value.
//
// This test fails only when a v2 endpoint is added or changed to return a scalar 200 body. The fix
// is in the endpoint, not here: wrap the payload in an object Response record (see
// GetVersionEndpoint.Response) and rebuild so the OpenAPI snapshot this test reads is regenerated.
//
// A text/plain 200 counts as a scalar too — switching the media type is not a way out. Binary
// media types (application/octet-stream and friends) stay legal: file download is exempt.
public sealed class NoPrimitive200BodyCharacterizationTests
{
    private static readonly string[] _v2DocumentFileNames =
    [
        "API_v2-user.json",
        "API_v2-admin.json",
        "API_v2-provideradmin.json"
    ];

    [Fact]
    public void Should_ReturnNoScalar200Body_When_ScanningV2Documents()
    {
        var violatingOperations = _v2DocumentFileNames
            .SelectMany(FindScalar200BodyOperations)
            .OrderBy(operation => operation, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violatingOperations.Length == 0,
            $"""
             These v2 operations return a scalar 200 body (JSON primitive or text/plain):
             {string.Join(Environment.NewLine, violatingOperations)}
             Wrap the payload in an object Response record and rebuild to regenerate the snapshot.
             """);
    }

    [Theory]
    [InlineData("""{"type": ["null", "object"]}""", true)]
    [InlineData("""{"type": ["null", "array"]}""", true)]
    [InlineData("""{"type": ["null", "integer", "string"]}""", false)]
    [InlineData("""{"type": ["integer", "string"]}""", false)]
    [InlineData("""{"type": ["string", "object"]}""", false)]
    [InlineData("""{"type": "object"}""", true)]
    [InlineData("""{"type": "array"}""", true)]
    [InlineData("""{"type": "integer"}""", false)]
    public void Should_ClassifyByEveryNonNullTypeMember_When_TypeIsBareStringOrUnionArray(
        string schemaJson,
        bool expectedComposite)
    {
        using var doc = JsonDocument.Parse(schemaJson);

        Assert.Equal(expectedComposite, IsCompositeSchema(doc.RootElement));
    }

    private static IEnumerable<string> FindScalar200BodyOperations(string fileName)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RepoRoot.FindFile(fileName)));

        foreach (var pathProperty in doc.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operationProperty in pathProperty.Value.EnumerateObject())
            {
                if (TryGet200Content(operationProperty.Value, out var content) && HasScalarBody(content))
                {
                    yield return $"{operationProperty.Name.ToUpperInvariant()} {pathProperty.Name}";
                }
            }
        }
    }

    private static bool TryGet200Content(JsonElement operation, out JsonElement content)
    {
        content = default;

        return operation.TryGetProperty("responses", out var responses)
            && responses.TryGetProperty("200", out var ok)
            && ok.TryGetProperty("content", out content);
    }

    private static bool HasScalarBody(JsonElement content) =>
        content.TryGetProperty("text/plain", out _)
        || (content.TryGetProperty("application/json", out var json)
            && json.TryGetProperty("schema", out var schema)
            && IsScalarSchema(schema));

    // Detection is by exclusion, not by an allow-list of primitive types: the generator omits "type"
    // for some primitives (an int32 body arrives as {"pattern": "^-?(?:0|[1-9]\d*)$", "format":
    // "int32"}), so a missing "type" must not be read as "not a scalar" — that hole let an int body
    // through the guard. Anything that does not name a composite shape is treated as scalar.
    private static bool IsScalarSchema(JsonElement schema) => !IsCompositeSchema(schema);

    private static bool IsCompositeSchema(JsonElement schema) =>
        schema.TryGetProperty("$ref", out _)
        || schema.TryGetProperty("allOf", out _)
        || schema.TryGetProperty("oneOf", out _)
        || schema.TryGetProperty("anyOf", out _)
        || (schema.TryGetProperty("type", out var typeProperty) && HasCompositeType(typeProperty));

    // OpenAPI 3.1's JSON-Schema "type" is a bare string ("array") until a schema spans several JSON
    // types, then it is a union array of any length ("["null", "integer", "string"]"). A body is
    // scalar unless every non-null member is composite: a union mixing a scalar with a composite
    // (e.g. ["string", "object"]) can still legally arrive as the bare scalar.
    private static bool HasCompositeType(JsonElement typeProperty) => typeProperty.ValueKind switch
    {
        JsonValueKind.String => typeProperty.GetString() is "object" or "array",
        JsonValueKind.Array => typeProperty.EnumerateArray()
            .Where(type => type.GetString() != "null")
            .All(type => type.GetString() is "object" or "array"),
        _ => false
    };
}
