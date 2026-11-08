using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Core.Features.Enums.GetAuthenticationTypes;

internal static class GetAuthenticationTypesEndpoint
{
    internal sealed record Response(string Name);

    internal static IResult HandleAsync(HttpContext http, [FromServices] IOptions<AuthenticationTypeOptions> options)
    {
        var types = options.Value.AllowedAuthenticationTypeNames
            .Select(name => new Response(name))
            .ToList();
        return PagedResponse.Complete(http, types);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetAuthenticationTypesEndpoint()
        {
            app.MapGet("AuthenticationTypes", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetAuthenticationTypesV2")
            .WithSummary("List authentication types")
            .WithDescription("Returns the configured cluster authentication type names, the values PreferredAuthenticationType takes when creating a resource on a location. Whether a location's HEAppE instance offers a type is checked when the resource is provisioned.")
            .WithTags("Enums")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK);
    }
}