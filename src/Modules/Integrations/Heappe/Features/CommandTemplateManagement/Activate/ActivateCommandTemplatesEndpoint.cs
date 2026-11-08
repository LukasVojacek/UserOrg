using Heappe.Features.CommandTemplateManagement;
using Heappe.Features.CommandTemplateManagement.Activate;
using Heappe.Features.CommandTemplateManagement.Request;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Provides extension methods for configuring the endpoint that activates command templates in the application.
/// </summary>
/// <remarks>This class contains methods for mapping and documenting the HTTP PUT endpoint used to activate
/// command templates. It is intended for internal use during endpoint registration and OpenAPI documentation setup
/// within the application's routing configuration.</remarks>
public static partial class ActivateCommandTemplatesEndpoint
{
    /// <summary>
    /// Maps the endpoint for activating a command template to the specified route builder.
    /// </summary>
    /// <remarks>The mapped endpoint handles HTTP PUT requests to "/CommandTemplate/Activate" and expects a
    /// request body containing a <see cref="CommandTemplateActivateRequest"/>. The endpoint uses dependency injection
    /// to resolve an <see cref="ActivateCommandTemplateHandler"/> for processing the request.</remarks>
    /// <param name="app">The endpoint route builder to which the activation endpoint will be added.</param>
    /// <returns>The same <see cref="IEndpointRouteBuilder"/> instance provided in <paramref name="app"/>, with the activation
    /// endpoint configured.</returns>
    public static IEndpointRouteBuilder MapCommandTemplateActivateEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPut("/CommandTemplate/Activate", async ([FromBody] CommandTemplateActivateRequest template, [FromServices] ActivateCommandTemplateHandler handler, CancellationToken ct = default) =>
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
            .WithName("ActivateCommandTemplate")
            .WithSummary("Activate a HEAppE command template for a project resource")
            .WithDescription(
                "Moves one HEAppE command template of the given project resource from Inactive to Active, on all of its partitions at once. The template is addressed by its id together with the project short name and the resource name, and all three must match or the request is refused with 404. Two guards run before anything is written: the call is refused with 409 when any location the template has a partition on is frozen by an enforced kill-switch — being whitelisted does not exempt a write — and it is refused when the template's partitions are not all currently Inactive, with the states actually found named in the error. On success it answers 204 and drops the project's cached command-template permissions as well as the cached templates info and the cached HEAppE cluster entries of every HEAppE instance behind the resource.")
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