using System.Net;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using UserOrg.Shared.FeatureManagement;

namespace UserOrganizationManagement.Application.Tests;

// End-to-end characterization of the flag-off boot path, exercised through a real host boot rather than
// composition inspection alone (that's V2CompositionGateTests): with FeatureManagement:V2Api off,
// MapV2CoreEndpoints() never runs, so a Core v2 route must 404 — while /api/version, a legacy v1 route
// and the always-mapped Ddi api/v2/* routes (outside AddV2Core) are still routed, each asserted against
// its concrete status rather than merely "not 404".
[Collection(ProgramHostBootTestCollection.Name)]
public sealed class V2ApiFlagOffBootTests
{
    private static readonly V2ApiFlagOffFactory _factory = new();

    [Fact]
    public async Task Should_Return404_When_RequestingCoreV2RouteWithFlagOff()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v2/Projects", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Should_Return200_When_RequestingVersionEndpointWithFlagOff()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/version", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // 401 is what proves the route is still mapped: legacy v1 routes are authorized as a group
    // (Extensions.RegisterApiRoutes maps them under app.MapGroup("api").RequireAuthorization()), so the
    // auth challenge can only fire for a route that routing actually matched — an unmapped route would
    // 404 here instead. Asserting the challenge rather than a handler result keeps this test off every
    // external dependency (Vault, DB), which is also why the path must stay one whose handler needs
    // none: do not "strengthen" this into an assertion that requires the handler to run.
    [Fact]
    public async Task Should_Return401_When_RequestingLegacyRouteWithFlagOff()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/Enums/LocationTypes", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Ddi routes require authorization; an unauthenticated request is challenged and rejected before
    // reaching the handler, proving the route survived the flag without depending on any external
    // infrastructure (Vault, DB) actually succeeding.
    [Theory]
    [InlineData("/api/v2/DataStaging/WorkerData")]
    [InlineData("/api/v2/DataStaging/DataStagingLocations")]
    [InlineData("/api/v2/Service/ProjectsInfo")]
    [InlineData("/api/v2/Service/SyncDetails")]
    public async Task Should_Return401_When_RequestingDdiRouteWithFlagOff(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Boots the API host offline (no external infrastructure) with FeatureManagement:V2Api forced off,
    // regardless of the process-wide env var OpenApiSchemaReferenceUniquenessTests.OfflineApiFactory's
    // static constructor may already have set to "true" — a webhost UseSetting always outranks
    // environment variables in ASP.NET Core's configuration precedence, unlike an added
    // ConfigureAppConfiguration source (which loses to an env var Program.cs's own builder already read).
    private sealed class V2ApiFlagOffFactory : WebApplicationFactory<Program>
    {
        static V2ApiFlagOffFactory()
        {
            // Set before the entry point runs: AddVaultSecrets throws if these are absent, and the
            // ValidateOnStart options need the auth/mail/Heappe placeholders. Values are intentionally
            // unreachable/offline.
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
            // Registers the "GatewayJwt" handler (AuthenticationExtensions only adds it when both are
            // set) so an unauthenticated request gets a real 401 challenge instead of an unhandled
            // "no authentication handler is registered" 500.
            SetIfAbsent("GatewayInternal__Authority", "https://gateway-boot-test.invalid");
            SetIfAbsent("GatewayInternal__Audience", "gateway-internal");
            SetIfAbsent(ProgramBootEnvironment.NexusBaseUrlKey, ProgramBootEnvironment.DefaultNexusBaseUrl);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting($"FeatureManagement:{FeatureFlags.V2Api}", "false");

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
