using System.Text;
using System.Text.RegularExpressions;

namespace IntegrationTests.AuthMatrix;

/// <summary>
/// Builds the auth-matrix test vectors from the pure B01 pieces and turns a <see cref="Route"/> into a
/// concrete live request. The theory's data source is derived from the checked-in OpenAPI snapshots (the
/// same <c>API_*.json</c> B01 reads) so it is enumerable at test-discovery time without a running server;
/// <see cref="AuthMatrixCompletenessTests"/> then asserts the live-discovered route set equals this one.
/// </summary>
internal static class AuthMatrixData
{
    private static readonly Regex RouteParameter = new(@"\{[^{}]+\}", RegexOptions.Compiled);

    // The Gateway scopes project-routed permissions by membership: a caller only clears authorization on
    // "/api/v2/Projects/{projectShortName}/..." when it belongs to that project (Admin clears globally).
    // The per-role clients are seeded into this one project, so its short name is the only substitution
    // that reaches an Allowed outcome for the Owner/Manager/Member tiers rather than a membership 403.
    private const string SeededProjectShortName = "TESTPRJ";

    /// <summary>The 4 human tiers B01 models plus the anonymous tier, in a stable order.</summary>
    internal static readonly string[] Roles = ["Admin", "Owner", "Manager", "Member", "Anonymous"];

    /// <summary>
    /// The single route whose authorization target is also the seed resource it would mutate: DELETE
    /// project has no request body to fail validation on and no leaf segment to point at a missing child,
    /// so an Allowed-tier call genuinely soft-deletes <see cref="SeededProjectShortName"/>. The theory
    /// heals the seed after exercising it (every other write lands on 400/404 before touching state).
    /// </summary>
    internal static bool IsSeedProjectDelete(string path, string httpMethod) =>
        httpMethod == "DELETE" && path == "/api/v2/Projects/{projectShortName}";

    // Live-verified divergences where the end-to-end authorization outcome is stricter than the pure
    // Gateway-RBAC prediction B01 computes. DELETE /api/v2/ProjectTags/{tagName} is a global catalogue
    // route (no {projectShortName}), yet roleManagmentRBAC.json requires perm:project:tag:remove — a
    // "perm:project:*" key the Gateway can only clear inside a project it can resolve membership for.
    // With no project in the path, Owner and Manager are rejected end-to-end (403), while the seed grants
    // them the permission and DeleteProjectTagEndpoint itself is Admin-only. The matrix asserts the true
    // contract; this entry records the divergence as a finding rather than hiding it behind a skip.
    private static readonly Dictionary<(string Path, string HttpMethod, string Role), ExpectedOutcome> LiveVerifiedOverrides = new()
    {
        [("/api/v2/ProjectTags/{tagName}", "DELETE", "Owner")] = ExpectedOutcome.Forbidden403,
        [("/api/v2/ProjectTags/{tagName}", "DELETE", "Manager")] = ExpectedOutcome.Forbidden403
    };

    /// <summary>
    /// Paths inside the documented v2 space that no published OpenAPI document describes. The gateway
    /// answers them itself with a route-existence 404 (ADR-0106), for every tier including the anonymous
    /// one: a typo must not look like a missing token. They are deliberately kept out of the legacy space,
    /// which the V1Api feature flag can leave unjudged - the gateway unit tests pin that half.
    /// </summary>
    internal static readonly (string Path, string HttpMethod)[] UnservedPaths =
    [
        ("/api/v2/there-is-no-such-route", "GET"),
        ("/api/v2/Projects/TESTPRJ/there-is-no-such-sub-route", "GET"),
        ("/api/v2/there-is-no-such-route", "POST")
    ];

    /// <summary>The v2 route set discovered from the checked-in OpenAPI snapshots (the theory's routes).</summary>
    internal static IReadOnlySet<Route> SnapshotRoutes() => RouteDiscovery.DiscoverV2Routes(ReadOpenApiSnapshots());

    /// <summary>
    /// One row per (route, role) — 84 routes × 5 tiers, plus <see cref="UnservedPaths"/> × 5 tiers —
    /// carried as primitives so xUnit serializes each
    /// case (named, individually runnable). The expected outcome is B01's <see cref="RbacExpectationBuilder"/>
    /// verbatim: this suite's job is to prove the live Gateway matches that pure prediction.
    /// </summary>
    internal static TheoryData<string, string, string, ExpectedOutcome> BuildCases()
    {
        var rbac = RbacDocument.Parse(ReadRbacSeed());
        var cases = new TheoryData<string, string, string, ExpectedOutcome>();

        var routes = SnapshotRoutes()
            .OrderBy(route => route.Path, StringComparer.Ordinal)
            .ThenBy(route => route.HttpMethod, StringComparer.Ordinal);

        foreach (var route in routes)
        {
            foreach (var role in Roles)
            {
                var expected = LiveVerifiedOverrides.TryGetValue((route.Path, route.HttpMethod, role), out var overridden)
                    ? overridden
                    : RbacExpectationBuilder.Expect(route, role, rbac);
                cases.Add(route.Path, route.HttpMethod, role, expected);
            }
        }

        foreach (var unserved in UnservedPaths)
        {
            foreach (var role in Roles)
            {
                cases.Add(unserved.Path, unserved.HttpMethod, role, ExpectedOutcome.NotFound404);
            }
        }

        return cases;
    }

    /// <summary>
    /// Builds the live request for a discovered route. The <c>projectShortName</c> segment is the seeded
    /// project (so project-scoped calls clear the Gateway's membership check into an Allowed outcome);
    /// every other placeholder is a well-formed-but-absent value. That combination keeps writes
    /// mutation-safe: a body-carrying write fails model validation (400) and a leaf-addressed write points
    /// at a missing child (404) — both non-401/403 — before any state changes, so the theory asserts the
    /// authorization outcome alone, never a persisted side effect (the one exception,
    /// <see cref="IsSeedProjectDelete"/>, is healed by the caller).
    /// </summary>
    internal static HttpRequestMessage BuildRequest(string path, string httpMethod)
    {
        var request = new HttpRequestMessage(new HttpMethod(httpMethod), new Uri(Substitute(path), UriKind.Relative));

        if (httpMethod is "POST" or "PUT")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static string Substitute(string path) =>
        RouteParameter.Replace(
            path,
            match => match.Value switch
            {
                "{projectShortName}" => SeededProjectShortName,
                // requestId is the only uuid-typed segment; a well-formed-but-absent guid keeps it past
                // route-constraint matching without resolving to a real request.
                _ when match.Value.Contains("requestId", StringComparison.OrdinalIgnoreCase)
                    => "ffffffff-ffff-ffff-ffff-ffffffffffff",
                // Every other leaf (tag/provider/location/resource/user/slug/token) is a free string; an
                // absent value reaches authorization then 404s on the missing child — never a mutation.
                _ => "authmatrix-absent"
            });

    private static string[] ReadOpenApiSnapshots()
    {
        var repoRoot = RepositoryRoot();
        string[] documentFiles =
        [
            "API_user.json",
            "API_administration.json",
            "API_service.json",
            "API_v2-user.json",
            "API_v2-provideradmin.json",
            "API_v2-admin.json"
        ];

        return documentFiles
            .Select(name => File.ReadAllText(Path.Combine(repoRoot, name)))
            .ToArray();
    }

    private static string ReadRbacSeed() => File.ReadAllText(Path.Combine(RepositoryRoot(), "roleManagmentRBAC.json"));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "roleManagmentRBAC.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root (roleManagmentRBAC.json) could not be located.");
    }
}
