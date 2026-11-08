using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace UserOrg.AppHost;

/// <summary>
/// Registers a WireMock.Net server that stands in for the external HEAppE HTTP API
/// (ADR-0051). The Core API is redirected to this server purely via seed data — an
/// <c>AssignedLocationValue</c> row with <c>Name = "HEAPPE_URL"</c> and
/// <c>Value = "__HEAPPE_MOCK_URL__"</c> that the test fixture substitutes at runtime —
/// so no production code depends on this resource. The resource is only registered when
/// <c>Parameters:UseHeappeMock</c>, which defaults to on under <c>aspire run</c> (see
/// <c>Program.cs</c>); it listens on a port
/// the run allocates. Static request/response
/// mappings live under the <c>heappe-mappings/</c> folder next to the AppHost project —
/// the same folder also serves the E2E in-process WireMock (ADR-0069).
/// </summary>
internal static class WireMockExtensions
{
    /// <summary>Aspire resource name of the HEAppE HTTP fake.</summary>
    internal const string HeappeMockResourceName = "heappe-mock";

    /// <summary>Endpoint name exposed by the WireMock resource.</summary>
    internal const string HeappeMockEndpointName = "http";

    /// <summary>
    /// Adds the HEAppE WireMock fake to the application model, gated on
    /// <c>Parameters:UseHeappeMock</c> (see <c>Program.cs</c>); excluded from any manifest.
    /// The Core API reaches it through the seeded endpoint value, which carries whichever
    /// port the run allocated, so no port is pinned here.
    /// </summary>
    internal static IResourceBuilder<WireMockServerResource> AddHeappeMock(this IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var mappingsPath = Path.Combine(builder.AppHostDirectory, "heappe-mappings");

        return builder
            .AddWireMock(HeappeMockResourceName)
            .WithMappingsPath(mappingsPath)
            .WithReadStaticMappings()
            .WithWatchStaticMappings()
            .WithWireMockInspectorCommand()
            .WithUrlForEndpoint(HeappeMockEndpointName, static url =>
            {
                url.DisplayText = "🧪 HEAppE mock";
                url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
            })
            .ExcludeFromManifest();
    }
}