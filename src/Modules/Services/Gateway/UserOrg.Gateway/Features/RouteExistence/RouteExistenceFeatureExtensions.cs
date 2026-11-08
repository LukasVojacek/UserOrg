using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace UserOrg.Gateway.Features.RouteExistence;

internal static class RouteExistenceFeatureExtensions
{
    /// <summary>
    /// Registers the route-existence oracle (ADR-0106): the surface every request is judged against, and
    /// the background loader that fills it from the published OpenAPI documents.
    /// </summary>
    public static void AddRouteExistenceFeature(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<RouteExistenceOptions>()
            .BindConfiguration(RouteExistenceOptions.SectionName);

        builder.Services.AddSingleton<RouteSurfaceProvider>();

        // why: the documents are read straight from Core through service discovery, which
        // AddServiceDefaults already applies to every client, never through the gateway's own proxy, so
        // loading the oracle cannot depend on the oracle.
        builder.Services.AddHttpClient(RouteSurfaceLoader.HttpClientName, (services, client) =>
        {
            client.BaseAddress = new Uri("https+http://userorg");
            client.Timeout = services.GetRequiredService<IOptions<RouteExistenceOptions>>().Value.RequestTimeout;
        });

        builder.Services.AddHostedService<RouteSurfaceLoader>();
    }
}