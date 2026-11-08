using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using RemI.HostContracts;

namespace RemI.Features.Users;

/// <summary>
/// <c>GET /api/remi/users</c> for the RemI service. Every user, unfiltered and unscoped: the caller is a
/// service identity carrying <c>perm:service:remi</c>, gated at the Gateway, not per project role.
/// </summary>
public static class GetUsersEndpoint
{
    internal static async Task<IResult> HandleAsync(
        [FromServices] IUserorgService userorgService,
        CancellationToken ct)
    {
        var result = await userorgService.GetUsersAsync(ct);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
    }

    extension(IEndpointRouteBuilder app)
    {
        public IEndpointRouteBuilder MapGetUsersEndpoint()
        {
            app.MapGet("users", HandleAsync)
                .MapDocumentation();

            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetUsers")
            .WithSummary("Get users")
            .WithDescription("Returns a list of users")
            .WithTags("RemI")
            .Produces<IReadOnlyList<UserResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }
}