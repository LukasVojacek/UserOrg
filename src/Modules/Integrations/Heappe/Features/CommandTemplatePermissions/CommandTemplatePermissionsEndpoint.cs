using Heappe.Features.CommandTemplatePermissions.Response;

using Lukdrasil.Result;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using UserOrg.Shared;

namespace Heappe.Features.CommandTemplatePermissions;

public static partial class CommandTemplatePermissionsEndpoint
{
    /// <summary>
    /// Maps the endpoint for retrieving user permissions on a specific command template to the application's routing
    /// configuration.
    /// </summary>
    /// <remarks>The mapped endpoint responds to HTTP GET requests at the route
    /// "CommandTemplatePermissions/{HeappeIdentifier}" and returns the permissions associated with the specified
    /// command template for the authenticated user. The endpoint requires access to a <see
    /// cref="CommandTemplatePermissionQueryHandler"/> and <see cref="IHttpContextAccessor"/> from the service
    /// container. Authorization requirements may be added as needed.</remarks>
    /// <param name="app">The endpoint route builder to which the template permissions endpoint will be added.</param>
    /// <returns>The <see cref="IEndpointRouteBuilder"/> instance with the template permissions endpoint configured.</returns>
    public static IEndpointRouteBuilder MapTemplatePermissionsEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/CommandTemplatePermissions/{HeappeIdentifier}", async ([FromRoute(Name = "HeappeIdentifier")] string heappeIdentifier, [FromServices] CommandTemplatePermissionQueryHandler permissionQueryHandler, [FromServices] IHttpContextAccessor httpContextAccessor, CancellationToken ct = default) =>
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return Results.Problem("HttpContext is not available.", statusCode: StatusCodes.Status500InternalServerError);
            }

            _ = httpContext.TryGetUserIdentityProviderId(out string? idpUserId);
            var query = new CommandTemplatePermissionQuery(idpUserId, heappeIdentifier);
            var templatePermissions = await permissionQueryHandler.Handle(query, ct);

            return templatePermissions.ToHttpResult();
        })
        .MapDocumentation<UserCommandTemplatePermissionModel>();

        return app;
    }

    /// <summary>
    /// Adds OpenAPI documentation metadata to the route handler.
    /// </summary>
    /// <param name="builder">The <see cref="RouteHandlerBuilder"/> to configure documentation for.</param>
    /// <returns>The configured <see cref="RouteHandlerBuilder"/> instance.</returns>
    /// <remarks>
    /// This method attaches OpenAPI documentation metadata to the endpoint.
    /// It ensures that the endpoint is properly documented in the generated Swagger/OpenAPI specification.
    /// </remarks>
    internal static RouteHandlerBuilder MapDocumentation<T>(this RouteHandlerBuilder builder)
    {
        return builder
            .WithName("GetCommandTemplatePermissions")
            .WithSummary("Get the calling user's command-template permissions on one HEAppE instance")
            .WithDescription(
                "Returns, for the caller, every project queue reachable through the given HEAppE instance together with the command templates of that queue and whether each one is enabled for them. It is built from the caller's currently valid project allocations whose HPC or Quantum locations carry this HEAppE identifier; a caller with no such allocation gets an empty permission list rather than an error. A template is reported as enabled only when its stored state is Active AND it is not killed by an enforced kill-switch on its location — a frozen template reads as not enabled, and no reason for the freeze is disclosed here. The answer is cached per user and HEAppE identifier for five minutes, and is dropped early when a command template of one of those projects changes or any kill-switch is enforced or lifted.")
            .WithOpenApiDoc(operation =>
            {
                var parameter = operation.Parameters?.FirstOrDefault(p => p.Name == "HeappeIdentifier");
                if (parameter is not null)
                {
                    parameter.Description = "The HEAppE instance identifier (the HEAPPE_IDENTIFIER value configured on a location), naming which HEAppE deployment the permissions are asked for.";
                }

                return operation;
            })
            .WithTags("CommandTemplateManagement")
            .Produces<T>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }
}