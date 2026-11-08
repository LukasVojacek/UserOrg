using System.Diagnostics;

using OpenTelemetry.Instrumentation.AspNetCore;

namespace UserOrg.Gateway.Startup;

/// <summary>
/// Names a proxied request's server span after the endpoint it was routed to.
/// </summary>
/// <remarks>
/// <para>
/// Every proxied request enters through one forwarder mapped on <c>/{**catch-all}</c>, so ASP.NET Core
/// resolves one route for all of them and every span is called <c>GET /{**catch-all}</c>. A trace that
/// cannot tell two endpoints apart is not worth collecting.
/// </para>
/// <para>
/// The rename has to happen here and nowhere else. The instrumentation re-derives the display name and
/// <c>http.route</c> from endpoint metadata when the request ends, so anything a middleware or a YARP
/// transform writes during the request is silently overwritten; <c>EnrichWithHttpResponse</c> is the only
/// hook that runs after that. It is synchronous, which is why the template is resolved earlier, on the
/// authorization path, and left in <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/>.
/// </para>
/// <para>
/// The name is the RBAC mapping's path template, never the request path. A path carries project short
/// names and ids, and span name is an indexed, grouped dimension in every backend - one name per id would
/// be a worse outcome than the single name it replaces. The template set is bounded by the seeded
/// mappings.
/// </para>
/// </remarks>
internal static class GatewayTracing
{
    /// <summary>Where the authorization path leaves the template it matched.</summary>
    internal const string RouteTemplateItemKey = "gateway.route_template";

    public static IServiceCollection AddGatewaySpanNaming(this IServiceCollection services)
    {
        // Configure, not AddAspNetCoreInstrumentation: the instrumentation is registered by
        // AddServiceDefaults and these callbacks accumulate on the same options instance rather than
        // replacing what it configured.
        return services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
            options.EnrichWithHttpResponse = static (activity, response) =>
            {
                if (!response.HttpContext.Items.TryGetValue(RouteTemplateItemKey, out var stashed)
                    || stashed is not string template
                    || template.Length == 0)
                {
                    // Not a proxied request, or one that never reached the permission lookup: the
                    // Blazor pages, the JWKS endpoints and the health endpoints already carry their own
                    // route and must keep it.
                    return;
                }

                activity.DisplayName = $"{response.HttpContext.Request.Method} {template}";
                activity.SetTag("http.route", template);
            });
    }
}