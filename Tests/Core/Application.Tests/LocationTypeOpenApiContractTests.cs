using System.Net;
using System.Text.Json;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// ADR-0020 §4 over the generated v2-User document: every v2 contract that exposes a location type carries it
/// as one enum field serialized by name, and both list filters bind that name.
/// </summary>
[Collection(ProgramHostBootTestCollection.Name)]
public sealed class LocationTypeOpenApiContractTests
{
    private static readonly OpenApiSchemaReferenceUniquenessTests.OfflineApiFactory _factory = new();

    [Theory]
    [InlineData("/api/v2/Locations", "LocationType")]
    [InlineData("/api/v2/Providers/{providerName}/Locations/{locationName}", "LocationType")]
    [InlineData("/api/v2/Providers/{providerName}/Resources", "LocationType")]
    [InlineData("/api/v2/Providers/{providerName}/Resources/{resourceName}", "LocationType")]
    [InlineData("/api/v2/Projects/{projectShortName}/Extended", "LocationType")]
    [InlineData("/api/v2/LocationTypes", "Name")]
    public async Task Should_CarryLocationTypeAsEnumName_When_ResponseExposesIt(string path, string property)
    {
        using var document = await GetUserDocumentAsync();
        var root = document.RootElement;
        var response = root.GetProperty("paths").GetProperty(path).GetProperty("get")
            .GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");

        var properties = CollectProperties(root, response);

        Assert.DoesNotContain("LocationTypeId", properties.Keys);
        Assert.DoesNotContain("LocationTypeName", properties.Keys);
        Assert.True(properties.TryGetValue(property, out var schema), $"{path}: no '{property}' property in the 200 response.");
        Assert.Contains("HPC", EnumNames(root, schema));
    }

    [Theory]
    [InlineData("/api/v2/Locations")]
    [InlineData("/api/v2/Providers/{providerName}/Resources")]
    public async Task Should_FilterByLocationTypeName_When_ListingByLocationType(string path)
    {
        using var document = await GetUserDocumentAsync();
        var root = document.RootElement;
        var parameters = root.GetProperty("paths").GetProperty(path).GetProperty("get").GetProperty("parameters")
            .EnumerateArray()
            .ToDictionary(p => p.GetProperty("name").GetString()!, p => p.GetProperty("schema"), StringComparer.Ordinal);

        Assert.DoesNotContain("locationTypeId", parameters.Keys);
        Assert.True(parameters.TryGetValue("locationType", out var schema), $"{path}: no 'locationType' query parameter.");
        Assert.Contains("HPC", EnumNames(root, schema));
    }

    private static async Task<JsonDocument> GetUserDocumentAsync()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/api/v2-User/openapi.json", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static Dictionary<string, JsonElement> CollectProperties(JsonElement root, JsonElement schema)
    {
        var found = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        Walk(root, schema, found, []);
        return found;
    }

    private static void Walk(JsonElement root, JsonElement schema, Dictionary<string, JsonElement> found, HashSet<string> visited)
    {
        if (schema.ValueKind != JsonValueKind.Object)
            return;

        if (schema.TryGetProperty("$ref", out var reference))
        {
            if (visited.Add(reference.GetString()!))
                Walk(root, Resolve(root, reference.GetString()!), found, visited);
            return;
        }

        if (schema.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                found.TryAdd(property.Name, property.Value);
                Walk(root, property.Value, found, visited);
            }
        }

        if (schema.TryGetProperty("items", out var items))
            Walk(root, items, found, visited);

        foreach (var composite in new[] { "allOf", "anyOf", "oneOf" })
        {
            if (schema.TryGetProperty(composite, out var members))
            {
                foreach (var member in members.EnumerateArray())
                    Walk(root, member, found, visited);
            }
        }
    }

    private static List<string?> EnumNames(JsonElement root, JsonElement schema)
    {
        if (schema.TryGetProperty("$ref", out var reference))
            return EnumNames(root, Resolve(root, reference.GetString()!));

        if (schema.TryGetProperty("enum", out var values))
            return [.. values.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : null)];

        foreach (var composite in new[] { "allOf", "anyOf", "oneOf" })
        {
            if (schema.TryGetProperty(composite, out var members))
                return [.. members.EnumerateArray().SelectMany(member => EnumNames(root, member))];
        }

        return [];
    }

    private static JsonElement Resolve(JsonElement root, string reference) =>
        root.GetProperty("components").GetProperty("schemas").GetProperty(reference["#/components/schemas/".Length..]);
}
