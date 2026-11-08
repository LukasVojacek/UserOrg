using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Shared.OpenApi;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// Guards the premise the gateway's route-existence oracle rests on (ADR-0106): the oracle answers
/// <c>404 route.not_found</c> for an <c>/api</c> path none of the <see cref="ApiDocuments.Published"/>
/// OpenAPI documents describes, which is only sound while every endpoint mapped under <c>/api</c> is described
/// by one of them, with <c>V1Api</c> on and off alike. An endpoint hidden from the description, by <c>.ExcludeFromDescription()</c> or by an
/// <c>ApiAudience</c> tag no document collects, would be served by Core and answered 404 by the gateway.
/// The comparison runs against the real mapped routes and the real generated documents of an in-process
/// host boot, so it needs no container topology.
/// </summary>
[Collection(ProgramHostBootTestCollection.Name)]
public sealed partial class ApiRouteDocumentationCoverageGuardrailTests
{
    private const string ApiPrefix = "/api";

    // why: the Scalar UI and the OpenAPI document endpoints are served under the same "/api" prefix and are
    // excluded from every document by the libraries that map them, so they are the one legitimate population
    // of undescribed "/api" routes. Listing them by hand rather than reading their
    // ExcludeFromDescriptionAttribute is the point: that attribute is exactly the defect this guard exists to
    // catch, so trusting it would exempt the defect along with the documentation surface.
    // warn: the entries are canonical paths, and RouteSurface.DocumentationSurfacePaths
    // (src/Modules/Services/Gateway/UserOrg.Gateway/Features/RouteExistence/RouteSurface.cs) is the oracle's
    // own copy of this population. A route added here without being added there becomes a gateway 404.
    private static readonly HashSet<string> _documentationSurface = new(StringComparer.OrdinalIgnoreCase)
    {
        ApiPrefix,
        "/api/{}",
        "/api/{}/openapi.json",
        "/api/scalar.js",
        "/api/scalar.aspnetcore.js",
        "/api/favicon.svg",
    };

    // why: the documents are generated from the mapped endpoints, so V2Api must be on or the comparison would
    // run over a surface the deployed host does not have; V1Api is the variable under test.
    private static readonly Dictionary<bool, Lazy<ProgramBootFactory>> _factories = new()
    {
        [true] = new(() => new ProgramBootFactory(isV1ApiEnabled: true, isV2ApiEnabled: true)),
        [false] = new(() => new ProgramBootFactory(isV1ApiEnabled: false, isV2ApiEnabled: true)),
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Should_DescribeEveryMappedApiRoute_When_GeneratingThePublishedDocuments(bool isV1ApiEnabled)
    {
        var factory = _factories[isV1ApiEnabled].Value;
        var documentNames = ApiDocuments.Published(isV1ApiEnabled);
        var described = await DescribedPathsAsync(factory, documentNames);
        var mapped = MappedApiRoutes(factory);

        Assert.NotEmpty(mapped);
        Assert.NotEmpty(described);

        var undocumented = mapped
            .Where(route => !_documentationSurface.Contains(route) && !described.Contains(route))
            .ToList();

        Assert.True(
            undocumented.Count == 0,
            $"{undocumented.Count} route(s) are mapped under {ApiPrefix} but described by none of the documents [{string.Join(", ", documentNames)}]: {string.Join(", ", undocumented)}. The gateway's route-existence oracle answers 404 for a path no document describes (ADR-0106), so a served endpoint must never be hidden from the description: drop the .ExcludeFromDescription(), or give the endpoint an ApiAudience tag one of the v2 documents collects.");
    }

    // why: the allowlist above is the one way a mapped route escapes the guard, so a stale entry would hide a
    // real endpoint silently. Pinning every entry to a route that is actually mapped makes the allowlist fail
    // rather than rot when the Scalar or OpenAPI mapping changes shape.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Should_MapEveryDocumentationSurfacePath_When_BootingTheHost(bool isV1ApiEnabled)
    {
        var mapped = MappedApiRoutes(_factories[isV1ApiEnabled].Value);

        var stale = _documentationSurface.Where(path => !mapped.Contains(path, StringComparer.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            stale.Count == 0,
            $"{stale.Count} documentation-surface exemption(s) match no mapped route: {string.Join(", ", stale)}. An exemption that no longer names a real route exempts nothing and hides whatever takes its place; remove it.");
    }

    private static List<string> MappedApiRoutes(ProgramBootFactory factory) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .Where(raw => raw.Length > 0)
            .SelectMany(CanonicalForms)
            .Where(IsUnderApi)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static async Task<HashSet<string>> DescribedPathsAsync(ProgramBootFactory factory, IEnumerable<string> documentNames)
    {
        using var client = factory.CreateClient();
        var described = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var documentName in documentNames)
        {
            using var document = await GetOpenApiDocumentAsync(client, documentName);

            described.UnionWith(DescribedPaths(document));
        }

        return described;
    }

    private static async Task<JsonDocument> GetOpenApiDocumentAsync(HttpClient client, string documentName)
    {
        using var response = await client.GetAsync(new Uri($"{ApiPrefix}/{documentName}/openapi.json", UriKind.Relative));

        Assert.True(
            response.IsSuccessStatusCode,
            $"GET {ApiPrefix}/{documentName}/openapi.json returned {(int)response.StatusCode}; the guard cannot judge a document it could not read.");

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static List<string> DescribedPaths(JsonDocument document)
    {
        var described = new List<string>();

        foreach (var pathEntry in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            if (DescribesAnOperation(pathEntry.Value))
            {
                described.Add(Canonical(pathEntry.Name));
            }
        }

        return described;
    }

    // why: an OpenAPI path item may carry parameters or a summary and no operation at all; such an entry
    // describes no route, so counting it would let an empty stub satisfy the guard.
    private static bool DescribesAnOperation(JsonElement pathItem)
    {
        foreach (var member in pathItem.EnumerateObject())
        {
            if (member.Name is "get" or "post" or "put" or "delete" or "patch" or "head" or "options" or "trace")
            {
                return true;
            }
        }

        return false;
    }

    // why: a trailing optional route parameter serves the shortened path as well as the full one, and that is
    // the only way "/api" itself (the Scalar UI landing page) is mapped at all. Yielding one form would leave
    // the served path out of the comparison.
    private static IEnumerable<string> CanonicalForms(string route)
    {
        yield return Canonical(route);

        var optionalTrailingParameter = OptionalTrailingParameter().Match(route);

        if (optionalTrailingParameter.Success)
        {
            yield return Canonical(route[..optionalTrailingParameter.Index]);
        }
    }

    // why: a route template and a document path spell the same route differently (a leading slash, a route
    // constraint, an optional marker, a trailing slash), and the oracle matches a request path against a
    // template by replacing each parameter with one path segment. Reducing both sides to that shape compares
    // what the oracle compares instead of two spellings.
    private static string Canonical(string route)
    {
        var rooted = route.StartsWith('/') ? route : $"/{route}";
        var withoutParameterNames = PathParameter().Replace(rooted, "{}");

        return withoutParameterNames.Length > 1 ? withoutParameterNames.TrimEnd('/') : withoutParameterNames;
    }

    private static bool IsUnderApi(string path) =>
        string.Equals(path, ApiPrefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith($"{ApiPrefix}/", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\{[^{}]+\}")]
    private static partial Regex PathParameter();

    [GeneratedRegex(@"/\{[^{}]*\?\}$")]
    private static partial Regex OptionalTrailingParameter();
}