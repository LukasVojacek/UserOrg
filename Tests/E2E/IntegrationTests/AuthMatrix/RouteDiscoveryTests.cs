namespace IntegrationTests.AuthMatrix;

/// <summary>
/// RouteDiscovery is pure: fed OpenAPI JSON text, no HTTP/topology dependency. The 6 documents read
/// here are the build-time <c>API_*.json</c> snapshots UserOrg.Core.API commits at the repo root
/// (Program.cs regenerates them on every build), which mirror exactly what
/// HostCompositionCharacterizationTests serves live at "/api/{documentName}/openapi.json" for
/// User, Administration, Service, v2-User, v2-ProviderAdmin, v2-Admin.
/// </summary>
public sealed class RouteDiscoveryTests
{
    [Fact]
    public void Should_Discover87V2Routes_When_GivenThePublishedOpenApiDocuments()
    {
        var routes = RouteDiscovery.DiscoverV2Routes(ReadOpenApiSnapshots());

        // 82 Modules/Core *Endpoint.cs + 5 Modules/Services/Ddi *Endpoint.cs (WorkerData, GET+POST
        // DataStagingLocations, SyncDetails, ProjectsInfo) = 87, counted directly off the 6 real
        // documents. Was 83 until GetAuthenticationTypes (GET authentication-types) was added,
        // 84 until the per-project onboarding request list was removed (ADR-0107), 83 until
        // GET ProviderAdmin/Providers was added, 84 until POST
        // Providers/{providerName}/Resources/{resourceName}/CheckAccountAccess and POST
        // Providers/{providerName}/Resources/{resourceName}/ProvisionAccounts were added (78e3940a,
        // not acknowledged here at the time), and 86 until
        // GET ProviderAdmin/Providers/{providerName}/Resources was added; this expected value is the
        // checked-in snapshot, deliberately hardcoded so an unintended route add/remove has to be
        // acknowledged here.
        Assert.Equal(87, routes.Count);
    }

    [Fact]
    public void Should_OnlyReturnV2PrefixedPaths_When_GivenThePublishedOpenApiDocuments()
    {
        var routes = RouteDiscovery.DiscoverV2Routes(ReadOpenApiSnapshots());

        Assert.All(routes, route => Assert.StartsWith("/api/v2/", route.Path, StringComparison.Ordinal));
    }

    [Fact]
    public void Should_IncludeTheFourServiceTokenRoutes_When_GivenThePublishedOpenApiDocuments()
    {
        var routes = RouteDiscovery.DiscoverV2Routes(ReadOpenApiSnapshots());

        Assert.Contains(new Route("/api/v2/DataStaging/WorkerData", "GET"), routes);
        Assert.Contains(new Route("/api/v2/DataStaging/DataStagingLocations", "GET"), routes);
        Assert.Contains(new Route("/api/v2/Service/SyncDetails", "GET"), routes);
        Assert.Contains(new Route("/api/v2/Service/ProjectsInfo", "GET"), routes);
    }

    private static string[] ReadOpenApiSnapshots()
    {
        var repoRoot = ResolveRepoRoot();
        string[] documentNames =
        [
            "API_user.json",
            "API_administration.json",
            "API_service.json",
            "API_v2-user.json",
            "API_v2-provideradmin.json",
            "API_v2-admin.json"
        ];

        return documentNames
            .Select(name => File.ReadAllText(Path.Combine(repoRoot, name)))
            .ToArray();
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "roleManagmentRBAC.json")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root (roleManagmentRBAC.json) could not be located.");
    }
}
