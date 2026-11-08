using System;
using System.Collections.Generic;
using System.Text;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using RemI.HostContracts;

namespace RemI.Features.Users;

public static class GetUserSystemRoles
{
    internal sealed record Request([FromRoute] string IdpUserId);

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        [FromServices] IUserorgService userorgService,
        CancellationToken ct)
    {
        var result = await userorgService.GetUserSystemRoles(request.IdpUserId, ct);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
    }

    extension(IEndpointRouteBuilder app)
    {
        public IEndpointRouteBuilder MapGetSystemRolesEndpoint()
        {
            app.MapGet("users/{idpUserId}/SystemRoles", HandleAsync)
                .MapDocumentation();

            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetUserSystemRoles")
            .WithSummary("Get user system roles")
            .WithDescription("Returns a list of user system roles")
            .WithTags("RemI")
            .Produces<IReadOnlyList<UserSystemRolesResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }
}