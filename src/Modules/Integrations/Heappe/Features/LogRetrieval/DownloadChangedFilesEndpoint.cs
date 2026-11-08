using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries.Sync;
using Heappe.HostContracts;

using Lukdrasil.Result;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;

using UserOrg.Shared;
using UserOrg.Shared.Extensions;

namespace Heappe.Features.LogRetrieval;

/// <summary>
/// Provides the endpoint that downloads files changed by a HEAppE job on the calling user's behalf.
/// </summary>
public static class DownloadChangedFilesEndpoint
{
    /// <summary>
    /// Represents the request parameters for downloading files changed by a HEAppE job.
    /// </summary>
    /// <param name="ProjectShortName">The project short name (route parameter).</param>
    /// <param name="ResourceName">The resource name (route parameter).</param>
    /// <param name="SubmittedJobInfoId">The identifier of the submitted HEAppE job (route parameter).</param>
    internal sealed record Request(
        [FromRoute] string ProjectShortName,
        [FromRoute] string ResourceName,
        [FromRoute] long SubmittedJobInfoId
    );

    /// <summary>
    /// Downloads the files changed by the specified HEAppE job using the original end-user bearer token.
    /// </summary>
    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        OriginalBearerToken? token,
        [FromServices] IUserorgService userorgService,
        [FromServices] IHeappeConnectionFactory connectionFactory,
        [FromServices] IHeappeService heappeService,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(nameof(DownloadChangedFilesEndpoint));

        if (token is null)
        {
            return Results.Unauthorized();
        }

        var allocation = await userorgService.GetProjectResourceAllocationWithLocationsAsync(
            request.ProjectShortName, request.ResourceName, ct);
        if (!allocation.IsSuccess)
        {
            return Results.NotFound();
        }

        var identifier = IdentifierFor(allocation.Value.Resource.Locations);
        if (identifier is null)
        {
            logger.LogWarning("No HEAppE identifier on any location of {ResourceName} for listing/downloading files for job {JobId}", request.ResourceName, request.SubmittedJobInfoId);
            return Results.Problem("No location of the resource carries a HEAppE identifier", statusCode: StatusCodes.Status400BadRequest);
        }

        return await DownloadAsync(request.SubmittedJobInfoId, identifier.Value, token.Value, connectionFactory, heappeService, logger, ct);
    }

    // The first location by ordinal name order that carries an identifier, so the pick does not depend on the order the allocation lists them in.
    private static HeappeIdentifier? IdentifierFor(IEnumerable<LocationAllocationInfoSyncEntry> locations) =>
        locations
            .OrderBy(location => location.LocationName, StringComparer.Ordinal)
            .Select(location => location.HeappeInstance.Identifier)
            .Where(identifier => identifier.Value is not null)
            .Select(identifier => (HeappeIdentifier?)identifier)
            .FirstOrDefault();

    private static async Task<IResult> DownloadAsync(
        long submittedJobInfoId,
        HeappeIdentifier identifier,
        string bearerToken,
        IHeappeConnectionFactory connectionFactory,
        IHeappeService heappeService,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var connection = await connectionFactory.ForUserAsync(identifier, bearerToken, useToken: false, ct);
            if (!connection.IsSuccess)
            {
                return Results.InternalServerError(connection.Error.ToProblemDetails());
            }

            var files = await heappeService.DownloadChangedFilesAsync(connection.Value, submittedJobInfoId, ct);

            return files.Match(
                success => Results.Ok(success),
                failure => Results.InternalServerError(failure.ToProblemDetails()));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Downloading changed files for job {JobId} was canceled", submittedJobInfoId);
            return Results.Problem("Request canceled", statusCode: StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download changed files for job {JobId} from HEAppE instance {Identifier}", submittedJobInfoId, identifier);
            return Results.Problem("Failed to download changed files", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Maps the GET endpoint for downloading files changed by a HEAppE job.
    /// </summary>
    public static IEndpointRouteBuilder MapDownloadChangedFilesEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("ListChangedFilesForJob/{projectShortName}/{resourceName}/{submittedJobInfoId:long}", HandleAsync)
            .MapDocumentation();
        return app;
    }

    private static RouteHandlerBuilder MapDocumentation(this RouteHandlerBuilder builder)
        => builder
            .WithName(nameof(DownloadChangedFilesEndpoint))
            .WithSummary("Download files changed for a HEAppE job")
            .WithDescription("Find files changed for the specified HEAppE job and download their contents.")
            .WithOpenApiDoc(operation =>
            {
                Describe(operation, "projectShortName", "LEXIS Project Short-Name. Used together with the resource name to find the HEAppE instance that holds the job.");
                Describe(operation, "resourceName", "LEXIS Project Resource name. Its first location carrying a HEAppE instance URI is the one queried.");
                Describe(operation, "submittedJobInfoId", "Identifier of the submitted job in HEAppE, whose changed files are listed and downloaded.");
                return operation;
            })
            .WithTags("Heappe")
            .Produces<IDictionary<string, DownloadedFileModel>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status499ClientClosedRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

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