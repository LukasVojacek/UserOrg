using Microsoft.AspNetCore.Http.Features;

using UserOrg.Shared;

using UserOrganizationManagement.Application.LogMessages;

namespace UserOrganizationManagement.API.Middleware;

/// <summary>
/// Request size check middleware
/// </summary>
/// <param name="next">
/// Request delegate
/// </param>
/// <param name="logger">
/// Logger
/// </param>
internal sealed class RequestSizeMiddleware(RequestDelegate next, ILogger<RequestSizeMiddleware> logger)
{

    /// <summary>
    /// Invoke Middleware
    /// </summary>
    /// <param name="context">Context</param>
    /// <returns></returns>
    public async Task InvokeAsync(HttpContext context)
    {
        Endpoint endpoint = context?.Features.Get<IEndpointFeature>()?.Endpoint;
        SizeAttribute attribute = endpoint?.Metadata.GetMetadata<SizeAttribute>();
        if (attribute != null && context.Request.ContentLength > attribute.Size)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsync("Status Code: 413; Payload Too Large");

            logger.RequestSizeExceeded();
            logger.HttpRequest(context.Request.Scheme, context.Request.Host.ToString(), context.Request.Path.Value, context.Request.QueryString.ToString(), context.Request.ContentLength, $"{context.Request.Body}");
        }

        await next(context);
    }
}