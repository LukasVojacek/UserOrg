using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Security.Claims;

using Microsoft.AspNetCore.Http;

using UserOrg.Gateway.Startup;

using Xunit;

using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Drives <see cref="InternalTokenTransformExtensions.AddInternalTokenTransform"/> the way every
/// transform test needs to: build the <see cref="HttpContext"/> the real pipeline would hand it, then
/// apply the transform and hand back the resulting <see cref="RequestTransformContext"/>. Shared by
/// <c>InternalTokenTransformExtensionsTests</c>, <c>InternalTokenTransformProviderIdCharacterizationTests</c>
/// and <c>InternalTokenTransformProviderNameMintTests</c> so the three cannot drift on it again.
/// </summary>
internal static class InternalTokenTransformTestHarness
{
    public static Task<RequestTransformContext> ApplyInternalTokenTransformAsync(HttpContext httpContext)
        => ApplyTransformAsync(builder => builder.AddInternalTokenTransform(), httpContext, proxyAuthorization: null);

    /// <summary>
    /// Applies <see cref="InternalTokenTransformExtensions.AddPermissionCheckTransform"/> to a proxy request
    /// that already carries <paramref name="proxyAuthorization"/>, as YARP's header copy would leave it.
    /// </summary>
    public static Task<RequestTransformContext> ApplyPermissionCheckTransformAsync(HttpContext httpContext, string proxyAuthorization)
        => ApplyTransformAsync(builder => builder.AddPermissionCheckTransform(), httpContext, proxyAuthorization);

    private static async Task<RequestTransformContext> ApplyTransformAsync(Action<TransformBuilderContext> addTransform, HttpContext httpContext, string? proxyAuthorization)
    {
        var builderContext = new TransformBuilderContext();
        addTransform(builderContext);
        var requestContext = new RequestTransformContext
        {
            HttpContext = httpContext,
            ProxyRequest = new HttpRequestMessage()
        };
        if (proxyAuthorization is not null)
        {
            requestContext.ProxyRequest.Headers.TryAddWithoutValidation("Authorization", proxyAuthorization);
        }

        var transform = Assert.Single(builderContext.RequestTransforms);
        await transform.ApplyAsync(requestContext);
        return requestContext;
    }

    public static HttpContext BuildHttpContext(ClaimsPrincipal user, IServiceProvider services, string? incomingAuthorization = null)
    {
        var httpContext = new DefaultHttpContext
        {
            User = user,
            RequestServices = services
        };
        if (incomingAuthorization is not null)
        {
            httpContext.Request.Headers.Authorization = incomingAuthorization;
        }
        return httpContext;
    }

    public static string WriteTokenWithJti(string jti)
        => new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(claims: [new Claim(JwtRegisteredClaimNames.Jti, jti)]));
}
