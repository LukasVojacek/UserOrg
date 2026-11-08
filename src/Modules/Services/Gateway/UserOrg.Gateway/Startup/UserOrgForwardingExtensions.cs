using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Forge.Core.Features.UserOrgRoles;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UserOrg.Gateway.Features.Authorization;
using UserOrg.Gateway.Features.Diagnostics;
using UserOrg.Gateway.Features.InternalToken;
using UserOrg.Shared;

using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace UserOrg.Gateway.Startup
{
    internal static class UserOrgForwardingExtensions
    {
        /// <summary>
        /// The gateway's RFC 9457 code for a diagnostics path that reached the catch-all forwarder, which
        /// means this process did not map it locally. It is deliberately not
        /// <see cref="RouteExistenceTransformExtensions.NotFoundCode"/>: the route-existence oracle judges
        /// only the <c>/api</c> spaces and answers <c>Unknown</c> here, so it never said these paths are
        /// unserved, and the two 404s have different causes (a caller's wrong path against a gateway that
        /// is missing its own diagnostics endpoints).
        /// </summary>
        internal const string DiagnosticsNotFoundCode = "diagnostics.not_served";

        public static IEndpointConventionBuilder MapUserOrgCatchAllForwarder(this IEndpointRouteBuilder app)
        {
            var builder = app.MapForwarder(
                "/{**catch-all}",
                "https+http://userorg",
                static transformBuilder => transformBuilder.ConfigureUserOrgForwarderTransforms());

            return builder;
        }

        /// <summary>
        /// The catch-all forwarder's request transform chain, in the order YARP applies it. The order is
        /// load bearing: the route-existence check must precede the two transforms that own the 401 and
        /// the 403 (ADR-0106).
        /// </summary>
        internal static void ConfigureUserOrgForwarderTransforms(this TransformBuilderContext transformBuilder)
        {
            // why: diagnostics are served by the gateway itself and must never reach the upstream API.
            transformBuilder.AddRequestTransform(async context =>
            {
                var path = context.HttpContext.Request.Path;
                if (path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWithSegments("/alive", StringComparison.OrdinalIgnoreCase))
                {
                    await ProblemResponse.WriteAsync(
                        context.HttpContext,
                        StatusCodes.Status404NotFound,
                        DiagnosticsNotFoundCode,
                        "Not found",
                        "Diagnostics endpoints are served by the gateway itself and are never proxied.");
                }
            });

            // invariant: the route-existence check runs before AddPermissionCheckTransform and
            // AddInternalTokenTransform, which own the 401 and the 403. It only ever answers 404 or
            // defers, so it can neither grant access nor run after a denial (ADR-0106).
            transformBuilder.AddRouteExistenceTransform();

            transformBuilder.AddRequestHeaderRemove("X-Real-IP");
            transformBuilder.AddRequestHeaderRemove("X-ClientId");
            transformBuilder.AddXForwardedFor(action: ForwardedTransformActions.Append);
            transformBuilder.AddXForwardedProto(action: ForwardedTransformActions.Set);
            transformBuilder.AddXForwardedHost(action: ForwardedTransformActions.Set);
            transformBuilder.AddXForwardedPrefix(action: ForwardedTransformActions.Set);
            transformBuilder.AddOriginalHost(true);

            transformBuilder.AddStandardTracing();

            transformBuilder.AddRequestTransform(async context =>
            {
                var httpCtx = context.HttpContext;
                var endpoint = httpCtx.GetEndpoint() as RouteEndpoint;
                var matchedTemplate = endpoint?.RoutePattern?.RawText ?? httpCtx.GetEndpoint()?.DisplayName ?? "<unknown>";
                var concretePath = (httpCtx.Request.PathBase + httpCtx.Request.Path).Value ?? httpCtx.Request.Path.Value ?? string.Empty;
                // No http.route / http.path set here: the ASP.NET Core instrumentation re-derives
                // http.route from endpoint metadata when the request ends, overwriting anything written
                // during it, and http.path was a non-standard duplicate of url.path. GatewayTracing does
                // the rename from EnrichWithHttpResponse, which is the one hook that runs after that.
                var logger = httpCtx.RequestServices.GetService(typeof(ILogger<Program>)) as ILogger<Program>;
                logger?.LogDebug("[GATEWAY] Request path {Path}, matched route template {Route}", concretePath, matchedTemplate);
                await ValueTask.CompletedTask;
            });

            transformBuilder.AddPermissionCheckTransform();

            transformBuilder.AddInternalTokenTransform();

            transformBuilder.AddPermissionsChangedTransform();
        }
    }
}