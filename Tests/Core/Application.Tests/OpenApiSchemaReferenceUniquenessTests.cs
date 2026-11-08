using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using UserOrg.Shared.FeatureManagement;

namespace UserOrganizationManagement.Application.Tests;

// Guardrail (regression) test for ADR-0062: every v2 endpoint's nested request/response/body record must
// get its OWN uniquely-named component schema in the generated OpenAPI document. Before the
// OpenApiOptions.CreateSchemaReferenceId delegate is added, Microsoft.AspNetCore.OpenApi keys schema $ref
// ids by the CLR type's SHORT name, so every nested `record Request` / `Response` / `Body` collapses into a
// single component schema shared (last-writer-wins) across many unrelated endpoints — e.g. one `Response`
// schema referenced by ~32 operations in v2-User, and `POST /api/v2/Projects` losing its `ShortName`.
//
// The assertions run against the REAL generated document (obtained in-process via the host's Scalar/OpenAPI
// endpoint), not a re-derivation of the delegate. They go RED against the un-fixed delegate and GREEN once
// each nested type is prefixed with its declaring endpoint name.
[Collection(ProgramHostBootTestCollection.Name)]
public sealed class OpenApiSchemaReferenceUniquenessTests
{
    // Bare short-names produced by the endpoint convention (nested `record Request/Response/Body`). Under the
    // un-fixed schema-id scheme these collapse across endpoints; after the fix they are prefixed with the
    // declaring endpoint name and therefore never appear as bare component-schema ids referenced by 2+ ops.
    private static readonly string[] _bareBodyNames = ["Request", "Response", "Body"];

    // Program is internal, so the factory must be internal too; sharing it through a static field (rather than
    // IClassFixture) keeps the internal type from leaking through this public class and boots the host once.
    private static readonly OfflineApiFactory _factory = new();

    [Theory]
    [InlineData("v2-User")]
    [InlineData("v2-Admin")]
    [InlineData("v2-ProviderAdmin")]
    public async Task Should_GiveEachEndpointBodyItsOwnSchema_When_GeneratingV2Document(string documentName)
    {
        using var document = await GetOpenApiDocumentAsync(documentName);

        var sharing = MapDirectBodySchemasToOperations(document);

        foreach (var bareName in _bareBodyNames)
        {
            if (sharing.TryGetValue(bareName, out var operations) && operations.Count > 1)
            {
                Assert.Fail(
                    $"Component schema '{bareName}' in document '{documentName}' is shared by {operations.Count} operations with different CLR body/response types: {string.Join(", ", operations.Order(StringComparer.Ordinal))}. Each endpoint's nested request/response record must get its own uniquely-named schema (ADR-0062).");
            }
        }
    }

    [Fact]
    public async Task Should_DocumentCreateProjectRequestWithShortName_When_GeneratingUserDocument()
    {
        using var document = await GetOpenApiDocumentAsync("v2-User");
        var root = document.RootElement;

        var createProject = FindOperation(root, "post", path => path.TrimEnd('/').EndsWith("/projects", StringComparison.OrdinalIgnoreCase));
        var requestRef = DirectBodySchemaRef(createProject.GetProperty("requestBody").GetProperty("content"));

        Assert.NotNull(requestRef);

        var properties = root.GetProperty("components").GetProperty("schemas")
            .GetProperty(requestRef!).GetProperty("properties");

        Assert.True(
            properties.TryGetProperty("ShortName", out _),
            $"POST /projects request schema '{requestRef}' must document the required 'ShortName' property (ADR-0062).");
    }

    // Strengthened post-fix guarantee: uniqueness over the ACTUAL generated (prefixed) ids, not just the
    // bare-name canary above. Verifies that every endpoint body id is endpoint-qualified and that any id shared
    // by more than one operation is a single, consistently-referenced reused type.
    [Theory]
    [InlineData("v2-User")]
    [InlineData("v2-Admin")]
    [InlineData("v2-ProviderAdmin")]
    public async Task Should_QualifyEveryEndpointBodyIdAndShareOnlyReusedTypes_When_GeneratingV2Document(string documentName)
    {
        using var document = await GetOpenApiDocumentAsync(documentName);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        // id -> distinct operations that use it as a DIRECT request/2xx-response body (single-use ids included).
        var references = MapDirectBodySchemasToOperations(document);

        // (A) Uniqueness over the ACTUAL generated (prefixed) ids. Every endpoint's nested request/response/body
        // record must carry its declaring-endpoint prefix, so NO bare id ("Request"/"Response"/"Body") survives
        // as a request/response body ANYWHERE — stricter than the "not shared by 2+" canary above: this also
        // fails a single endpoint whose body was left un-prefixed. A prefixed id embeds the unique endpoint name,
        // so "no bare id remains" is the document-observable form of "each endpoint body has its own unique id".
        //
        // (Two DISTINCT prefixed ids that happen to collide would be merged last-writer-wins into one component
        // carrying a single winner's shape; that merge is invisible in the emitted document by construction, so
        // it cannot be detected document-only. This check guards the realistic regression: the delegate failing
        // to apply to some/all endpoint bodies, which re-introduces a bare/collapsed id here.)
        foreach (var bareName in _bareBodyNames)
        {
            Assert.False(
                references.ContainsKey(bareName),
                $"Document '{documentName}' still exposes a bare '{bareName}' component schema as a request/response body: {(references.TryGetValue(bareName, out var bareOps) ? string.Join(", ", bareOps.Order(StringComparer.Ordinal)) : string.Empty)}. Every endpoint body must be qualified by its declaring endpoint name (ADR-0062).");
        }

        // (B) Any component schema referenced as a request/response body by more than one operation must resolve
        // to a single defined component (the same reused type) — i.e. a genuine top-level reused type such as
        // ResourceResponse or DataStagingLocationKey, never a dangling $ref left behind by a collapse.
        foreach (var (schemaId, operations) in references)
        {
            if (operations.Count <= 1)
            {
                continue;
            }

            Assert.True(
                schemas.TryGetProperty(schemaId, out _),
                $"Component schema '{schemaId}' in '{documentName}' is referenced by {operations.Count} operations ([{string.Join(", ", operations.Order(StringComparer.Ordinal))}]) but is not defined in components.schemas (dangling $ref).");
        }
    }

    private static async Task<JsonDocument> GetOpenApiDocumentAsync(string documentName)
    {
        using var client = _factory.CreateClient();

        // Scalar/OpenAPI documents are served under the "api" route prefix (ScalarUiOptions.RoutePrefix
        // default) and mapped with AllowAnonymous, so no auth/infrastructure is required to read the spec.
        using var response = await client.GetAsync(new Uri($"/api/{documentName}/openapi.json", UriKind.Relative));

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"GET /api/{documentName}/openapi.json returned {(int)response.StatusCode}.");

        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    // For every operation, maps each DIRECT request-body and 2xx-response-body component-schema $ref (resolving
    // one level of array element) to the set of operations that reference it. A bare-named schema shared by two
    // operations means two distinct nested CLR types collapsed onto one component schema.
    private static Dictionary<string, HashSet<string>> MapDirectBodySchemasToOperations(JsonDocument document)
    {
        var sharing = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var pathEntry in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var methodEntry in pathEntry.Value.EnumerateObject())
            {
                if (!IsHttpMethod(methodEntry.Name))
                {
                    continue;
                }

                var operation = methodEntry.Value;
                var opId = $"{methodEntry.Name.ToUpperInvariant()} {pathEntry.Name}";

                if (operation.TryGetProperty("requestBody", out var requestBody) &&
                    requestBody.TryGetProperty("content", out var requestContent) &&
                    DirectBodySchemaRef(requestContent) is { } requestRef)
                {
                    Add(sharing, requestRef, $"{opId} (request)");
                }

                if (operation.TryGetProperty("responses", out var responses))
                {
                    foreach (var response in responses.EnumerateObject())
                    {
                        if (!response.Name.StartsWith('2') ||
                            !response.Value.TryGetProperty("content", out var responseContent) ||
                            DirectBodySchemaRef(responseContent) is not { } responseRef)
                        {
                            continue;
                        }

                        Add(sharing, responseRef, $"{opId} (response {response.Name})");
                    }
                }
            }
        }

        return sharing;
    }

    private static void Add(Dictionary<string, HashSet<string>> sharing, string schema, string operation)
    {
        if (!sharing.TryGetValue(schema, out var operations))
        {
            operations = new HashSet<string>(StringComparer.Ordinal);
            sharing[schema] = operations;
        }

        operations.Add(operation);
    }

    private static string? DirectBodySchemaRef(JsonElement content)
    {
        foreach (var mediaType in content.EnumerateObject())
        {
            if (mediaType.Value.TryGetProperty("schema", out var schema))
            {
                return SchemaRef(schema);
            }
        }

        return null;
    }

    private static string? SchemaRef(JsonElement schema)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            return reference.GetString()?.Split('/')[^1];
        }

        if (schema.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String &&
            type.GetString() == "array" && schema.TryGetProperty("items", out var items) &&
            items.TryGetProperty("$ref", out var itemsReference))
        {
            return itemsReference.GetString()?.Split('/')[^1];
        }

        return null;
    }

    private static JsonElement FindOperation(JsonElement root, string method, Func<string, bool> pathPredicate)
    {
        foreach (var pathEntry in root.GetProperty("paths").EnumerateObject())
        {
            if (pathPredicate(pathEntry.Name) &&
                pathEntry.Value.TryGetProperty(method, out var operation))
            {
                return operation;
            }
        }

        throw new InvalidOperationException($"No '{method}' operation matching the path predicate was found.");
    }

    private static bool IsHttpMethod(string name) => name is "get" or "put" or "post" or "delete" or "patch";

    // Boots the API host in-process without any external infrastructure. Vault, auth-metadata and mail URLs are
    // pointed at unreachable placeholders (the Vault provider swallows connection failures) and all hosted
    // services (DB migration/seed) are removed, mirroring the build-time OpenAPI document generation path.
    internal sealed class OfflineApiFactory : WebApplicationFactory<Program>
    {
        static OfflineApiFactory()
        {
            // Set before the entry point runs: AddVaultSecrets throws if these are absent, and the ValidateOnStart
            // options need the auth/mail/Heappe placeholders. Values are intentionally unreachable/offline.
            SetIfAbsent("VaultCredentials__Address", "http://localhost:1");
            SetIfAbsent("VaultCredentials__MountPoint", "test");
            SetIfAbsent("AuthenticationConfiguration__MetadataAddress", "https://openapi-build.invalid");
            SetIfAbsent("MailConfiguration__MailSenderUrl", "https://openapi-build.invalid");
            SetIfAbsent("Heappe__ClusterAuthTypes__AllowedAuthenticationTypeNames__0", "SshCertificate");
            SetIfAbsent("EncryptedSpool__SpoolDirectory", Path.Combine(Path.GetTempPath(), "userorg-audit-boot-tests-spool"));
            SetIfAbsent("EncryptedSpool__EndpointBaseUrl", "https://openapi-build.invalid");
            SetIfAbsent("OghamSealing__KeyEndpoint", "https://openapi-build.invalid/.well-known/ogham-sealing-keys.json");
            SetIfAbsent("OghamSealing__CacheDirectory", Path.Combine(Path.GetTempPath(), "userorg-audit-boot-tests-keys"));
            SetIfAbsent("ImmuDb__Username", "boot-tests");
            SetIfAbsent("ImmuDb__Password", "boot-tests");
            SetIfAbsent("ImmuDb__Database", "boot-tests");
            SetIfAbsent(ProgramBootEnvironment.NexusBaseUrlKey, ProgramBootEnvironment.DefaultNexusBaseUrl);
            // The v2 documents are empty unless the v2 endpoints are composed and mapped, exactly as
            // the build-time document generation path turns the flag on.
            SetIfAbsent($"FeatureManagement__{FeatureFlags.V2Api}", "true");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                foreach (var hostedService in services.Where(s => s.ServiceType == typeof(IHostedService)).ToList())
                {
                    services.Remove(hostedService);
                }
            });
        }

        private static void SetIfAbsent(string key, string value)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
