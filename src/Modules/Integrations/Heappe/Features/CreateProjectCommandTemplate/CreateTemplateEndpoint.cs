using global::Heappe.HostContracts;
using global::Heappe.Persistence;

using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries;
using Heappe.Contracts.Entries.Sync;
using Heappe.Features.CreateProjectCommandTemplate.Request;
using Heappe.Features.KillSwitch;
using Heappe.Helpers.Connection;
using Heappe.Persistence.Entities;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

using UserOrg.Shared;
using UserOrg.Shared.Extensions;

namespace Heappe.Features.CreateProjectCommandTemplate;

/// <summary>
/// Provides endpoint for creating HEAppE and project-level command templates.
/// </summary>
public static class CreateTemplateEndpoint
{
    /// <summary>
    /// Represents prepared resource data used during template creation.
    /// </summary>
    internal sealed record PreparedResourceData(
        ProjectResourceInfoEntry Project,
        ProjectResourceInfoSyncEntry Resource,
        Guid LocationId,
        string LocationName,
        ServiceTokenHeappeConnectionInfo ConnectionInfo,
        LocationPartition Partition);

    /// <summary>
    /// Handles command template creation request asynchronously.
    /// </summary>
    /// <remarks>
    /// The ExecutionScriptPath and PreparationScriptPath in the request must be COMPLETE paths
    /// to the scripts in HEAppE (not just filenames). Example: "/scripts/jobscript.sh"
    /// </remarks>
    internal static async Task<IResult> HandleAsync(
        [FromBody] CreateTemplateRequest request,
        [FromServices] IUserorgService userorgService,
        [FromServices] HeappeIntegrationDbContext context,
        [FromServices] IHeappeService heappeService,
        [FromServices] ILoggerFactory loggerfFactory,
        [FromServices] IHeappeConnectionFactory connectionFactory,
        [FromServices] IProjectCommandTemplateCreateService createService,
        [FromServices] HybridCache cache,
        [FromServices] CommandTemplateWriteGuard writeGuard,
        CancellationToken cancellationToken)
    {
        var logger = loggerfFactory.CreateLogger("Heappe.CreateProjectCommandTemplate");

        try
        {
            var result = await request
                .ValidateAndPrepareResourceDataAsync(
                    userorgService,
                    context,
                    connectionFactory,
                    writeGuard,
                    cancellationToken)
                .GetHeappeProjectAsync(request, heappeService, cancellationToken)
                .GetNodeTypeAsync(heappeService, cancellationToken)
                .BuildCommandTemplateRequest(request)
                .CreateCommandTemplateAsync(createService, cache, request.ProjectIdentifier.ProjectShortName, cancellationToken);

            if (!result.IsSuccess)
            {
                // A frozen-location rejection is a state conflict, not a server error, so it must
                // escape the 500 this catch-all otherwise returns for every other failure.
                var frozenConflict = KillSwitchErrorCodes.FrozenConflictOrNull(result.Error);
                if (frozenConflict is not null)
                {
                    return frozenConflict;
                }

                var pd = result.Error.ToProblemDetails();
                pd.Status = StatusCodes.Status500InternalServerError;
                pd.Title = "Internal Server Error";
                return Results.Problem(pd);
            }

            return Results.Created();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Command template creation for project {ProjectShortName} was canceled",
                request.ProjectIdentifier.ProjectShortName);
            return Results.Problem("Request canceled", statusCode: StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error creating command template for project {ProjectShortName}",
                request.ProjectIdentifier.ProjectShortName);
            return Results.Problem("Failed to create command template", statusCode: StatusCodes.Status500InternalServerError);
        }
    }


    /// <summary>
    /// Maps the POST endpoint for creating command templates.
    /// </summary>
    public static IEndpointRouteBuilder MapPostTemplateEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("CommandTemplates", HandleAsync)
            .MapDocumentation()
            .DisabledWhen("ReadOnlyMode");
        return app;
    }

    private static RouteHandlerBuilder MapDocumentation(this RouteHandlerBuilder builder)
        => builder
            .WithName(nameof(CreateTemplateEndpoint))
            .WithSummary("Create HEAppE command template")
            .WithDescription("Creates a command template in both HEAppE and the project resource management system. Requires valid project resource access and partition availability.")
            .WithTags("Heappe")
            .Accepts<CreateTemplateRequest>("application/json")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status499ClientClosedRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
}