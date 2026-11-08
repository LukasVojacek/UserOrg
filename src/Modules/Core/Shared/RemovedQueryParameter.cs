using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Core.Shared;

/// <summary>
/// A query parameter a v2 route no longer binds, and the one that replaced it. ASP.NET ignores an unknown query
/// parameter, so without this a client still sending the removed one would get an unfiltered 200; with it the
/// request is a 400 <c>validation.removed_parameter</c> naming the replacement (ADR-0020).
/// </summary>
internal sealed record RemovedQueryParameter(string Name, string Replacement) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.Request.Query.ContainsKey(Name)
            ? ValueTask.FromResult<object?>(Problems.Validation(
                CommonErrorCodes.RemovedParameter,
                $"The '{Name}' query parameter was removed; use '{Replacement}' instead."))
            : next(context);
}

internal static class RemovedQueryParameterExtensions
{
    extension(RouteHandlerBuilder builder)
    {
        internal RouteHandlerBuilder RejectRemovedQueryParameter(string name, string replacement)
        {
            var removed = new RemovedQueryParameter(name, replacement);
            return builder.WithMetadata(removed).AddEndpointFilter(removed);
        }
    }
}