using System.Net;
using System.Text.Json.Nodes;

using UserOrg.Shared.OpenApi;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// The ungated legacy endpoints (RemI, <c>api/version</c>, <c>api/DataStaging</c> and <c>api/Heappe</c>) are
/// described by the always-published <see cref="ApiDocuments.Service"/> document alone, with the operations and
/// schemas they had in the legacy pair unchanged. The baseline <c>OpenApiBaselines/ungated-legacy-operations.json</c>
/// holds those operations and the component schemas they reach, as the legacy pair published them before
/// the move.
/// </summary>
[Trait("Category", "Integration")]
[Collection(ProgramHostBootTestCollection.Name)]
public sealed class ServiceOpenApiDocumentTests
{
    private const string BaselinePath = "Tests/Core/Application.Tests/OpenApiBaselines/ungated-legacy-operations.json";

    private static readonly Lazy<ProgramBootFactory> _v1On = new(() => new ProgramBootFactory(isV1ApiEnabled: true, isV2ApiEnabled: true));

    private static readonly Lazy<ProgramBootFactory> _v1Off = new(() => new ProgramBootFactory(isV1ApiEnabled: false, isV2ApiEnabled: true));

    private static readonly Lazy<JsonObject> _baseline = new(() =>
        JsonNode.Parse(File.ReadAllText(RepoRoot.FindFile(BaselinePath)))!.AsObject());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Should_DescribeExactlyTheUngatedLegacyOperations_When_GeneratingTheServiceDocument(bool isV1ApiEnabled)
    {
        var document = await GetDocumentAsync(isV1ApiEnabled ? _v1On.Value : _v1Off.Value, ApiDocuments.Service);

        Assert.Equal(
            BaselineOperations().Select(entry => entry.Key).Order(StringComparer.Ordinal),
            Operations(document).Select(entry => entry.Key).Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task Should_KeepEveryMovedOperationUnchanged_When_ItIsDescribedByTheServiceDocument()
    {
        var operations = Operations(await GetDocumentAsync(_v1On.Value, ApiDocuments.Service));

        var changed = BaselineOperations()
            .Where(expected => !operations.TryGetValue(expected.Key, out var actual) || !JsonNode.DeepEquals(expected.Value, actual))
            .Select(expected => expected.Key)
            .ToList();

        Assert.True(changed.Count == 0, $"Moved operations whose description changed: {string.Join(", ", changed)}");
    }

    [Fact]
    public async Task Should_KeepEveryReferencedSchemaUnchanged_When_TheServiceDocumentDescribesIt()
    {
        var document = await GetDocumentAsync(_v1On.Value, ApiDocuments.Service);
        var schemas = document["components"]?["schemas"]?.AsObject();

        var changed = _baseline.Value["schemas"]!.AsObject()
            .Where(expected => schemas is null || !schemas.TryGetPropertyValue(expected.Key, out var actual) || !JsonNode.DeepEquals(expected.Value, actual))
            .Select(expected => expected.Key)
            .ToList();

        Assert.True(changed.Count == 0, $"Schemas the moved operations reach whose shape or id changed: {string.Join(", ", changed)}");
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Administration")]
    public async Task Should_DescribeNoUngatedLegacyOperation_When_GeneratingALegacyDocument(string documentName)
    {
        var operations = Operations(await GetDocumentAsync(_v1On.Value, documentName));

        var leaked = BaselineOperations().Select(entry => entry.Key).Where(operations.ContainsKey).ToList();

        Assert.NotEmpty(operations);
        Assert.True(leaked.Count == 0, $"{documentName} still describes ungated legacy operations: {string.Join(", ", leaked)}");
    }

    [Fact]
    public async Task Should_DescribeTheFlaggedResourceAudit_When_V1ApiIsOff()
    {
        var operations = Operations(await GetDocumentAsync(_v1Off.Value, ApiDocuments.Service));

        Assert.Contains("POST /api/Heappe/ResourceAudit/run", operations.Keys);
    }

    private static IEnumerable<KeyValuePair<string, JsonNode?>> BaselineOperations() =>
        _baseline.Value["operations"]!.AsObject();

    /// <summary>Every operation of a document, keyed <c>METHOD /path</c>.</summary>
    private static Dictionary<string, JsonNode?> Operations(JsonObject document) =>
        document["paths"]!.AsObject()
            .SelectMany(path => path.Value!.AsObject()
                .Where(member => member.Key != "parameters")
                .Select(member => (Key: $"{member.Key.ToUpperInvariant()} {path.Key}", member.Value)))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

    private static async Task<JsonObject> GetDocumentAsync(ProgramBootFactory factory, string documentName)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri($"/api/{documentName}/openapi.json", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
    }
}