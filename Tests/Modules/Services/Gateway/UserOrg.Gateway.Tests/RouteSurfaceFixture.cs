using System.Text;

using UserOrg.Gateway.Features.RouteExistence;
using UserOrg.Shared.OpenApi;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Miniature stand-ins for the five published OpenAPI documents: a v2 document and a legacy one, each
/// describing a couple of real routes, so a test can say "this path is served" and "this one is not"
/// without a running API.
/// </summary>
internal static class RouteSurfaceFixture
{
    internal const string ServedV2Path = "/api/v2/Projects/TESTPRJ";

    internal const string ServedLegacyPath = "/api/Project/TESTPRJ";

    internal const string UnservedV2Path = "/api/v2/there-is-no-such-route";

    internal const string UnservedLegacyPath = "/api/ThereIsNoSuchRoute";

    private const string V2Document = """
        {"paths":{
          "/api/v2/Projects/{projectShortName}":{"get":{},"put":{}},
          "/api/v2/Projects/{projectShortName}/Users":{"get":{}},
          "/api/v2/enums/request-states":{"get":{}},
          "/api/v2/Public/Requests/{slug}":{"get":{}}
        }}
        """;

    private const string LegacyDocument = """
        {"paths":{
          "/api/Project/{ProjectShortName}":{"get":{}},
          "/api/version":{"get":{}}
        }}
        """;

    private static readonly Lazy<RouteSurface> _publishedV1OffSurface = new(() =>
        RouteSurface.FromOpenApiDocuments(
            ApiDocuments.Published(isV1ApiEnabled: false)
                .Select(name => (ReadOnlyMemory<byte>)File.ReadAllBytes(SnapshotPath(name)))
                .ToList()));

    private const string EmptyDocument = """{"paths":{}}""";

    /// <summary>A document whose only path item carries no operation, so it describes no route.</summary>
    internal const string OperationlessDocument = """{"paths":{"/api/v2/Projects":{"parameters":[]}}}""";

    /// <summary>Both spaces loaded: the oracle judges legacy and v2 paths alike.</summary>
    internal static RouteSurface FullSurface() => SurfaceOf(V2Document, LegacyDocument);

    /// <summary>The v2 documents loaded and the legacy ones empty, as when the <c>V1Api</c> flag is off.</summary>
    internal static RouteSurface V2OnlySurface() => SurfaceOf(V2Document, EmptyDocument);

    /// <summary>
    /// The oracle as a gateway with <c>V1Api</c> off builds it: from the committed snapshots of
    /// <see cref="ApiDocuments.Published"/>(false) alone.
    /// </summary>
    internal static RouteSurface PublishedV1OffSurface() => _publishedV1OffSurface.Value;

    internal static RouteSurface SurfaceOf(params string[] documents) =>
        RouteSurface.FromOpenApiDocuments(documents.Select(document => (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(document)));

    internal static RouteSurfaceProvider ProviderOf(RouteSurface surface)
    {
        var provider = new RouteSurfaceProvider();
        provider.Publish(surface);
        return provider;
    }

    /// <summary>The oracle before anything loaded, or after every load failed.</summary>
    internal static RouteSurfaceProvider DegradedProvider() => new();

    /// <summary>
    /// The committed snapshot of a document: <c>API_{name}.json</c> in lower case at the repository root,
    /// where the build-time generator of <c>API.csproj</c> writes it.
    /// </summary>
    private static string SnapshotPath(string documentName) =>
#pragma warning disable CA1308 // The committed snapshot names are lower case, so the fold must be lower case.
        RepoRoot.FindFile($"API_{documentName.ToLowerInvariant()}.json");
#pragma warning restore CA1308
}
