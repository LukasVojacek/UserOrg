using System.Text.Json;

namespace IntegrationTests.AuthMatrix;

/// <summary>A single (path, HTTP method) route, as it appears in an OpenAPI document.</summary>
public sealed record Route(string Path, string HttpMethod);

/// <summary>
/// Enumerates the v2 API surface from OpenAPI JSON documents. Pure: takes document text, does no
/// HTTP/file IO itself, so callers can feed either live-fetched or checked-in-snapshot documents.
/// </summary>
internal static class RouteDiscovery
{
    private static readonly string[] HttpMethodNames = ["get", "post", "put", "delete", "patch"];

    /// <summary>
    /// Returns every (path, method) pair whose path starts with "/api/v2/", deduplicated across
    /// all supplied documents (the same v2 route can legitimately appear in more than one document).
    /// </summary>
    internal static IReadOnlySet<Route> DiscoverV2Routes(IEnumerable<string> openApiDocuments)
    {
        var routes = new HashSet<Route>();

        foreach (var document in openApiDocuments)
        {
            using var parsed = JsonDocument.Parse(document);

            foreach (var pathEntry in parsed.RootElement.GetProperty("paths").EnumerateObject())
            {
                if (!pathEntry.Name.StartsWith("/api/v2/", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var operation in pathEntry.Value.EnumerateObject())
                {
                    if (Array.IndexOf(HttpMethodNames, operation.Name) < 0)
                    {
                        continue;
                    }

                    routes.Add(new Route(pathEntry.Name, operation.Name.ToUpperInvariant()));
                }
            }
        }

        return routes;
    }

    private static readonly string[] LiveDocumentNames =
        ["User", "Administration", "Service", "v2-User", "v2-ProviderAdmin", "v2-Admin"];

    /// <summary>
    /// Fetches the 6 live OpenAPI documents through the running Gateway (anonymous GET) and discovers
    /// the same v2 route set as the pure overload — the source of truth for the completeness assertion.
    /// </summary>
    internal static async Task<IReadOnlySet<Route>> DiscoverLiveV2RoutesAsync(
        HttpClient anonymousGatewayClient,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(anonymousGatewayClient);

        var documents = new List<string>(LiveDocumentNames.Length);
        foreach (var documentName in LiveDocumentNames)
        {
            using var response = await anonymousGatewayClient.GetAsync(
                new Uri($"/api/{documentName}/openapi.json", UriKind.Relative), ct);
            response.EnsureSuccessStatusCode();
            documents.Add(await response.Content.ReadAsStringAsync(ct));
        }

        return DiscoverV2Routes(documents);
    }
}
