using Heappe.Features.CommandTemplateManagement;
using Heappe.Features.CommandTemplateManagement.Request;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Heappe.Features.CommandTemplateManagement.Deactivate;

/// <summary>
/// Provides extension methods for configuring the endpoint that deactivates command templates in the application's
/// routing pipeline.
/// </summary>
/// <remarks>This class contains methods for mapping the deactivate command template endpoint and for attaching
/// OpenAPI documentation metadata to the route handler. The endpoint is accessible via an HTTP PUT request at
/// '/CommandTemplate/Deactivate' and is documented for inclusion in Swagger/OpenAPI specifications.</remarks>
public static partial class DeactivateCommandTemplatesEndpoint
{
    /// <summary>
    /// Maps the endpoint for deactivating a command template to the application's routing pipeline using an HTTP PUT
    /// request.
    /// </summary>
    /// <remarks>The mapped endpoint expects a request body containing a <see
    /// cref="CommandTemplateActivateRequest"/> and uses the <see cref="DeactivateCommandTemplateHandler"/> service to
    /// process the deactivation. The endpoint is accessible at '/CommandTemplate/Deactivate'.</remarks>
    /// <param name="app">The endpoint route builder to which the deactivate command template endpoint will be added.</param>
    /// <returns>The same <see cref="IEndpointRouteBuilder"/> instance provided in <paramref name="app"/>, with the deactivate
    /// endpoint configured.</returns>
    public static IEndpointRouteBuilder MapCommandTemplateDeactivateEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPut("/CommandTemplate/Deactivate", async ([FromBody] CommandTemplateActivateRequest template, [FromServices] DeactivateCommandTemplateHandler handler, CancellationToken ct = default) =>
        {
            var result = await handler.Handle(template, ct);
            return result.ToWriteHttpResult();
        })
        .MapDocumentation();

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
    internal static RouteHandlerBuilder MapDocumentation(this RouteHandlerBuilder builder)
    {
        return builder
            .WithName("DeactivateCommandTemplate")
            .WithSummary("Deactivate a HEAppE command template for a project resource")
            .WithDescription(
                "Moves one HEAppE command template of the given project resource from Active to Inactive, on all of its partitions at once — the exact inverse of Activate, taking the same request body. The template is addressed by its id together with the project short name and the resource name, and all three must match or the request is refused with 404. Two guards run before anything is written: the call is refused with 409 when any location the template has a partition on is frozen by an enforced kill-switch — being whitelisted does not exempt a write — and it is refused when the template's partitions are not all currently Active, with the states actually found named in the error. Deactivating is a state change on the template and is not a kill-switch: it is visible to readers as Inactive rather than Frozen. On success it answers 204 and drops the project's cached command-template permissions as well as the cached templates info and the cached HEAppE cluster entries of every HEAppE instance behind the resource.")
            .WithTags("CommandTemplateManagement")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }
}