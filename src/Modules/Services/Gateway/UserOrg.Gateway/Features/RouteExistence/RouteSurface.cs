using System.Collections.Frozen;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UserOrg.Gateway.Features.RouteExistence;

/// <summary>
/// What the gateway knows about a requested path, from the published API surface alone.
/// </summary>
internal enum ERouteExistence
{
    /// <summary>
    /// The surface says nothing about this path: it lies outside the documented API, or the document
    /// space it belongs to was never loaded. The request carries on into the auth stage unchanged.
    /// </summary>
    Unknown,

    /// <summary>A published OpenAPI document describes this path.</summary>
    Served,

    /// <summary>No published OpenAPI document describes this path, in a space that was loaded.</summary>
    NotServed
}

/// <summary>
/// The gateway's route-existence oracle: an immutable snapshot of the path templates the five published
/// OpenAPI documents describe (ADR-0106). It is deliberately not the RBAC endpoint map - a map miss means
/// "no permission entry", which stays a 403 (ADR-0059) - and it answers <see cref="ERouteExistence.Unknown"/>
/// wherever it cannot be sure, so a missing or stale surface can never turn the API into a 404.
/// </summary>
internal sealed class RouteSurface
{
    /// <summary>The state before anything loaded: every path is <see cref="ERouteExistence.Unknown"/>.</summary>
    public static RouteSurface Empty { get; } = FromOpenApiDocuments([]);

    private const string ApiPrefix = "/api";

    private const string V2Prefix = "/api/v2/";

    private const string OpenApiDocumentSuffix = "/openapi.json";

    // warn: these are served by Core outside every OpenAPI document, so judging them would 404 the
    // documentation surface the oracle reads its own truth from. They coincide with entries of
    // PublicEndpointsOptions.GetDefaultPaths, but they are not derived from it: that list answers which
    // authentication scheme handles a path, and binding the two would silently switch the 404 off for
    // every prefix later made public.
    private static readonly FrozenSet<string> DocumentationSurfacePaths =
        new[] { ApiPrefix, "/api/scalar.js", "/api/scalar.aspnetcore.js" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex PathParameter = new(@"\{[^{}]+\}", RegexOptions.Compiled);

    // why: a path item may carry any of these verbs; the superset is the safe direction, because a verb
    // left out would make a described path look unserved and answer 404.
    private static readonly FrozenSet<string> HttpMethodNames =
        new[] { "get", "post", "put", "delete", "patch", "head", "options", "trace" }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // invariant: null means "no document described this space", which is what keeps the space unjudged.
    // The two spaces are the two document families: the legacy User/Administration filter and the v2 one.
    private readonly DescribedSpace? _legacySpace;

    private readonly DescribedSpace? _v2Space;

    private RouteSurface(DescribedSpace? legacySpace, DescribedSpace? v2Space)
    {
        _legacySpace = legacySpace;
        _v2Space = v2Space;
    }

    /// <summary>
    /// Compiles the path templates of the supplied OpenAPI documents into a surface. Every document must
    /// be supplied together: a space no document described stays unjudged, so a document set that came
    /// back empty can never make a live API look non-existent.
    /// </summary>
    public static RouteSurface FromOpenApiDocuments(IEnumerable<ReadOnlyMemory<byte>> openApiDocuments)
    {
        ArgumentNullException.ThrowIfNull(openApiDocuments);

        var legacy = new SpaceBuilder();
        var v2 = new SpaceBuilder();

        foreach (var document in openApiDocuments)
        {
            using var parsed = JsonDocument.Parse(document);

            if (!parsed.RootElement.TryGetProperty("paths", out var paths)
                || paths.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var pathEntry in paths.EnumerateObject())
            {
                if (!IsUnderApi(pathEntry.Name) || !DescribesAnOperation(pathEntry.Value))
                {
                    continue;
                }

                var template = Normalize(pathEntry.Name);
                (template.StartsWith(V2Prefix, StringComparison.OrdinalIgnoreCase) ? v2 : legacy).Add(template);
            }
        }

        return new RouteSurface(legacy.Build(), v2.Build());
    }

    /// <summary>
    /// Whether any route serves <paramref name="path"/>, as far as the published surface can say.
    /// The decision is on the path alone: a method a route does not serve keeps the answer the RBAC map
    /// gives it today rather than becoming a 404 the gateway invents.
    /// </summary>
    public ERouteExistence Evaluate(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value))
        {
            return ERouteExistence.Unknown;
        }

        var normalized = Normalize(value);

        if (!IsUnderApi(normalized) || IsDocumentationSurface(normalized))
        {
            return ERouteExistence.Unknown;
        }

        var space = normalized.StartsWith(V2Prefix, StringComparison.OrdinalIgnoreCase) ? _v2Space : _legacySpace;

        if (space is null)
        {
            return ERouteExistence.Unknown;
        }

        return space.Describes(normalized) ? ERouteExistence.Served : ERouteExistence.NotServed;
    }

    private static string Normalize(string path) => path.Length > 1 ? path.TrimEnd('/') : path;

    private static bool IsUnderApi(string path) =>
        string.Equals(path, ApiPrefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(ApiPrefix + "/", StringComparison.OrdinalIgnoreCase);

    private static bool IsDocumentationSurface(string path) =>
        path.EndsWith(OpenApiDocumentSuffix, StringComparison.OrdinalIgnoreCase)
        || DocumentationSurfacePaths.Contains(path);

    // why: an OpenAPI path item can carry parameters or a summary and no operation at all; such an entry
    // documents no route and must not make its space look described.
    private static bool DescribesAnOperation(JsonElement pathItem) =>
        pathItem.ValueKind == JsonValueKind.Object
        && pathItem.EnumerateObject().Any(member => HttpMethodNames.Contains(member.Name));

    private static Regex CompileTemplate(string pathTemplate)
    {
        // why: split on the placeholders and escape only the literal runs. Escaping first would mangle the
        // braces; a named-group compiler like Forge.Core's would throw on an OpenAPI parameter name that is
        // not a legal .NET group name, which the document language permits and this one does not care about.
        var pattern = string.Concat(
            "^",
            string.Join("[^/]+", PathParameter.Split(pathTemplate).Select(Regex.Escape)),
            "$");

        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// The templates of one document space. Parameterless templates are half the published surface and
    /// need no regex, so they answer from a set lookup and only the rest are scanned.
    /// </summary>
    private sealed class DescribedSpace(FrozenSet<string> literals, Regex[] templates)
    {
        public bool Describes(string path)
        {
            if (literals.Contains(path))
            {
                return true;
            }

            foreach (var template in templates)
            {
                if (template.IsMatch(path))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private sealed class SpaceBuilder
    {
        private readonly HashSet<string> _literals = new(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _templates = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string template)
        {
            if (template.Contains('{', StringComparison.Ordinal))
            {
                _templates.Add(template);
            }
            else
            {
                _literals.Add(template);
            }
        }

        public DescribedSpace? Build() =>
            _literals.Count == 0 && _templates.Count == 0
                ? null
                : new DescribedSpace(
                    _literals.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                    _templates.Select(CompileTemplate).ToArray());
    }
}