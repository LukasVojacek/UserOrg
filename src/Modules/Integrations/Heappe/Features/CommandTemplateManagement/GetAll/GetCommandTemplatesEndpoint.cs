using Heappe.Features.CommandTemplateManagement.GetAll.Response;

using Lukdrasil.Result;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi;

namespace Heappe.Features.CommandTemplateManagement.GetAll;

/// <summary>
/// Provides extension methods for configuring the endpoint that retrieves command templates for a specified project and
/// resource.
/// </summary>
/// <remarks>This class contains methods for mapping and documenting the command template retrieval endpoint
/// within an ASP.NET Core application. It is intended for internal use when setting up API routes and OpenAPI
/// documentation.</remarks>
public static partial class GetCommandTemplatesEndpoint
{
    /// <summary>
    /// Adds an HTTP GET endpoint to the route builder for retrieving command templates associated with a specified
    /// project and resource.
    /// </summary>
    /// <remarks>The mapped endpoint responds to GET requests at
    /// '/CommandTemplate/{ProjectShortName}/{ProjectResourceName}' and requires the project short name and resource
    /// name as route parameters. The endpoint returns the command templates for the specified project resource.
    /// Authorization and documentation mapping are applied according to the application's configuration.</remarks>
    /// <param name="app">The endpoint route builder to which the command template endpoint will be added.</param>
    /// <returns>The same <see cref="IEndpointRouteBuilder"/> instance, enabling further configuration of endpoints.</returns>
    public static IEndpointRouteBuilder MapCommandTemplateEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/CommandTemplate/{ProjectShortName}/{ProjectResourceName}", async ([FromRoute(Name = "ProjectShortName")] string projectShortName, [FromRoute(Name = "ProjectResourceName")] string resourceName, [FromServices] GetCommandTemplateQueryHandler queryHandler, CancellationToken ct = default) =>
        {
            var query = new GetCommandTemplateQuery(projectShortName, resourceName);
            var commandTemplates = await queryHandler.Handle(query, ct);
            return commandTemplates.ToHttpResult();
        })
        .MapDocumentation<IList<CommandTemplateResponse>>();
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
            .WithName("GetCommandTemplates")
            .WithSummary("Get the HEAppE command templates of one project resource")
            .WithDescription(
                "Lists every HEAppE command template held for the given project resource, with its id, name, type (system- or user-defined) and state. The state is the template's stored state folded over its partitions, and is reported as Expired when the project allocation or the resource allocation is outside its validity window. Frozen is reported separately and is true when at least one of the template's partitions sits on a location frozen by an enforced kill-switch; the reason for such a freeze is never disclosed here. A project resource that cannot be resolved yields 404.")
            .WithOpenApiDoc(operation =>
            {
                Describe(operation, "ProjectShortName", "LEXIS Project Short-Name.");
                Describe(operation, "ProjectResourceName", "LEXIS Project Resource name, as assigned to that project.");
                return operation;
            })
            .WithTags("CommandTemplateManagement")
            .Produces<T>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }

    /// <summary>
    /// Sets a parameter's description by NAME, and does nothing when the operation has no such parameter.
    /// </summary>
    private static void Describe(OpenApiOperation operation, string name, string description)
    {
        var parameter = operation.Parameters?.FirstOrDefault(p => p.Name == name);
        if (parameter is not null)
        {
            parameter.Description = description;
        }
    }
}