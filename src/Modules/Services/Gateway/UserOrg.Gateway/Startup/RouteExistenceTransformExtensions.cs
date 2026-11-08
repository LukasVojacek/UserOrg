using UserOrg.Gateway.Features.RouteExistence;

using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace UserOrg.Gateway.Startup;

internal static class RouteExistenceTransformExtensions
{
    /// <summary>The gateway's RFC 9457 code for a path no published API route serves (ADR-0106).</summary>
    internal const string NotFoundCode = "route.not_found";

    /// <summary>
    /// Answers 404 for a path the route surface says no route serves, and defers on everything else.
    /// It runs ahead of the transforms that own the 401 and the 403, writes no audit record (a typo is
    /// not a security event), and never grants access: its only two outcomes are a 404 and carrying on.
    /// </summary>
    public static void AddRouteExistenceTransform(this TransformBuilderContext transformBuilder)
    {
        transformBuilder.AddRequestTransform(async context =>
        {
            var httpContext = context.HttpContext;
            var routeSurface = httpContext.RequestServices.GetRequiredService<RouteSurfaceProvider>().Current;

            if (routeSurface.Evaluate(httpContext.Request.Path) is not ERouteExistence.NotServed)
            {
                return;
            }

            await ProblemResponse.WriteAsync(
                httpContext,
                StatusCodes.Status404NotFound,
                NotFoundCode,
                "Not found",
                "No API route serves this path.");
        });
    }
}