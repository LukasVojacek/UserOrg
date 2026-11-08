using Heappe.Contracts.Entries.Sync;

using Heappe.Features.CommandTemplateSynchronization.Request;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Heappe.Features.CommandTemplateSynchronization;

/// <summary>
/// Provides extension methods for configuring command template synchronization endpoints and related documentation in
/// the application's routing configuration.
/// </summary>
/// <remarks>This static class contains methods to map the command template synchronization POST endpoint and to
/// standardize API documentation and response metadata for command template management endpoints. These methods are
/// intended for internal use within the application's endpoint routing setup.</remarks>
public static partial class ProjectCommandTemplatesSyncEndpoint
{
    /// <summary>
    /// Maps the command template synchronization POST endpoint to the application's routing configuration.
    /// </summary>
    /// <remarks>This method registers a POST endpoint at '/CommandTemplate/Synchronization' that processes
    /// synchronization requests for command templates. The endpoint expects a <see cref="CommandTemplateSyncRequest"/>
    /// in the request body and uses the <see cref="ProjectCommandTemplateSyncService"/> to perform the synchronization.
    /// The response indicates whether the synchronization was successful.</remarks>
    /// <param name="app">The <see cref="IEndpointRouteBuilder"/> instance used to configure endpoint routing.</param>
    /// <returns>The updated <see cref="IEndpointRouteBuilder"/> instance, enabling further endpoint configuration.</returns>
    public static IEndpointRouteBuilder MapCommandTemplateSyncEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/CommandTemplate/Synchronization", async ([FromBody] CommandTemplateSyncRequest request, [FromServices] IProjectCommandTemplateSyncService service, CancellationToken ct = default) =>
        {
            var result = await service.SyncCommandTemplatesForResource(request.projectShortName, request.resourceName, ct);
            if (!result.IsSuccess)
            {
                return Results.BadRequest(new { success = false, error = result.Error });
            }

            var syncResult = result.Value;
            if (!syncResult.AnySucceeded)
            {
                return Results.BadRequest(new { success = false, result = syncResult });
            }

            return Results.Ok(syncResult);
        })
        .MapDocumentation<CommandTemplateSyncResult>();

        return app;
    }

    /// <summary>
    /// Configures the route handler for command template documentation endpoints, including route naming, tagging, and
    /// response types.
    /// </summary>
    /// <remarks>This method is intended for internal use to standardize API documentation and response
    /// metadata for command template management endpoints.</remarks>
    /// <typeparam name="T">The type of the command template being managed by the route.</typeparam>
    /// <param name="builder">The route handler builder used to configure the documentation endpoint.</param>
    /// <returns>The updated route handler builder with documentation-related settings applied.</returns>
    internal static RouteHandlerBuilder MapDocumentation<T>(this RouteHandlerBuilder builder)
    {
        return builder
            .WithName("SyncCommandTemplate")
            .WithSummary("Synchronize a project resource's command templates with HEAppE")
            .WithDescription(
                "Re-reads the project resource named in the body from every HEAppE instance behind it and creates or updates the stored command templates and location partitions to match, location by location. Locations frozen by an enforced kill-switch are skipped rather than synchronized. The run is partial by design: the response reports the outcome per location, so some locations can succeed while others fail. It answers 200 only when at least one location succeeded; when every location failed, or the resource has no HEAppE location at all, or every location it has was frozen and skipped, it answers 400 carrying the same per-location result or the error that stopped it.")
            .WithTags("CommandTemplateManagement")
            .Produces<T>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }
}