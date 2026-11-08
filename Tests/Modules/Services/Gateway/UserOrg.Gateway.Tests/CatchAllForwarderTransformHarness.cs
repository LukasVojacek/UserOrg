using System.Net.Http;
using System.Security.Claims;

using Microsoft.AspNetCore.Http;

using UserOrg.Gateway.Startup;

using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Drives the catch-all forwarder's real request transform chain
/// (<see cref="UserOrgForwardingExtensions.ConfigureUserOrgForwarderTransforms"/>) the way YARP drives it,
/// so a test asserts the answer the gateway gives rather than the answer one transform gives.
/// </summary>
internal static class CatchAllForwarderTransformHarness
{
    public static async Task ApplyCatchAllTransformsAsync(HttpContext httpContext)
    {
        var builderContext = new TransformBuilderContext();
        builderContext.ConfigureUserOrgForwarderTransforms();

        var requestContext = new RequestTransformContext
        {
            HttpContext = httpContext,
            ProxyRequest = new HttpRequestMessage()
        };

        foreach (var transform in builderContext.RequestTransforms)
        {
            await transform.ApplyAsync(requestContext);

            // see: YARP StructuredTransformer.TransformRequestAsync, which stops the chain on
            // RequestUtilities.IsResponseSet - a transform that answered the request ends it.
            if (httpContext.Response.StatusCode != StatusCodes.Status200OK || httpContext.Response.HasStarted)
            {
                break;
            }
        }
    }

    public static HttpContext BuildHttpContext(
        string path,
        ClaimsPrincipal user,
        IServiceProvider services,
        Stream responseBody)
    {
        var httpContext = new DefaultHttpContext
        {
            User = user,
            RequestServices = services
        };
        httpContext.Request.Path = path;
        httpContext.Request.Method = "GET";
        httpContext.Response.Body = responseBody;
        return httpContext;
    }

    public static ClaimsPrincipal AnonymousUser() => new(new ClaimsIdentity());
}