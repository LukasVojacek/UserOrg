using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace UserOrg.Gateway.Startup;

/// <summary>
/// Classification of a failed bearer authentication, stored in <see cref="HttpContext.Items"/> by the
/// JwtBearer <c>OnAuthenticationFailed</c> event and rendered later — by <c>OnChallenge</c> or by the
/// forwarder transform — so the event itself never starts the response and anonymous routes keep working.
/// </summary>
internal sealed record AuthenticationFailure(int Status, string Code, string Title, string Detail);

/// <summary>
/// Single writer for the Gateway's RFC 9457 <c>application/problem+json</c> error bodies
/// (<c>code</c> and <c>requestId</c> extensions).
/// </summary>
internal static class ProblemResponse
{
    internal const string AuthenticationFailureItemKey = "auth.failure";

    internal const string ContentType = "application/problem+json; charset=utf-8";

    private const string UnauthorizedCode = "auth.unauthorized";

    internal static async Task WriteAsync(HttpContext httpContext, int status, string code, string title, string detail)
    {
        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = status,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["requestId"] = httpContext.TraceIdentifier;

        await httpContext.Response.WriteAsJsonAsync(problem, options: null, contentType: ContentType);
    }

    /// <summary>
    /// Writes the classification stored by <c>OnAuthenticationFailed</c>, or a plain 401
    /// <c>auth.unauthorized</c> when the request carried no usable credential at all.
    /// A 401 carries the RFC 6750 <c>WWW-Authenticate</c> header.
    /// </summary>
    internal static Task WriteAuthenticationFailureAsync(HttpContext httpContext)
    {
        var failure = httpContext.Items[AuthenticationFailureItemKey] as AuthenticationFailure;

        if (failure is null)
        {
            httpContext.Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";
            return WriteAsync(httpContext, StatusCodes.Status401Unauthorized, UnauthorizedCode,
                "Not authenticated", "User is not authenticated. Provide a valid bearer token.");
        }

        if (failure.Status == StatusCodes.Status401Unauthorized)
        {
            httpContext.Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer error=\"invalid_token\"";
        }

        return WriteAsync(httpContext, failure.Status, failure.Code, failure.Title, failure.Detail);
    }
}