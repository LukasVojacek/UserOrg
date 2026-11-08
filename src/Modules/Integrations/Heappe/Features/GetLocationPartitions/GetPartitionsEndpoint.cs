using Heappe.Features.GetLocationPartitions.Response;
using Heappe.HostContracts;
using Heappe.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

namespace Heappe.Features.GetLocationPartitions;

/// <summary>
/// Provides endpoint for retrieving available HEAppE location partitions accessible to a project resource.
/// </summary>
public static class GetPartitionsEndpoint
{
    // based on ELocationType
    public const int HpcLocationTypeId = 1;
    public const int QuantumLocationTypeId = 7;

    /// <summary>
    /// Represents the request parameters for retrieving location partitions.
    /// </summary>
    /// <param name="ProjectShortName">The project short name (route parameter).</param>
    /// <param name="ResourceName">The resource name (route parameter).</param>
    internal sealed record Request(
        [FromRoute] string ProjectShortName,
        [FromRoute] string ResourceName
    );

    /// <summary>
    /// Retrieves available location partitions for the specified project resource.
    /// </summary>
    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        [FromServices] IUserorgService userorgService,
        [FromServices] HeappeIntegrationDbContext context,
        CancellationToken cancellationToken)
    {
        List<LocationPartitionResponse> responseData = [];

        // + slug provider_resourcename (u resource) + index providername+resourcename (slug) on the db side
        var locationNamesResult = await userorgService.GetValidLocationNames(
            request.ProjectShortName,
            request.ResourceName,
            [HpcLocationTypeId, QuantumLocationTypeId],
            cancellationToken);

        if (!locationNamesResult.IsSuccess)
        {
            return Results.NotFound();
        }

        var locationNames = locationNamesResult.Value;
        if (locationNames.Count == 0)
        {
            return Results.Ok(responseData);
        }

        var locationIds = locationNames.Select(l => l.LocationId).Distinct().ToList();
        var aggregationNames = locationNames.Select(l => l.AggregationName ?? string.Empty).Distinct().ToList();

        var locationPartitions = await context.LocationPartitions
            .AsNoTracking()
            .Where(w => locationIds.Contains(w.LocationId) && aggregationNames.Contains(w.AggregationName ?? string.Empty))
            .ToListAsync(cancellationToken);

        responseData = locationPartitions
            .Join(
                locationNames,
                partitions => (partitions.LocationId, partitions.AggregationName),
                l => (l.LocationId, l.AggregationName),
                (p, l) => new LocationPartitionResponse(p.Name, p.AggregationName ?? "", l.LocationName, l.ProviderName))
            .ToList();

        return Results.Ok(responseData);
    }

    /// <summary>
    /// Maps the GET endpoint for retrieving location partitions.
    /// </summary>
    public static IEndpointRouteBuilder MapGetPartitionsEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("Partitions/{projectShortName}/{resourceName}", HandleAsync)
            .MapDocumentation();
        return app;
    }

    private static RouteHandlerBuilder MapDocumentation(this RouteHandlerBuilder builder)
        => builder
            .WithName(nameof(GetPartitionsEndpoint))
            .WithSummary("Get available location partitions")
            .WithDescription("Retrieves all HEAppE location partitions accessible to the specified project resource, filtered by user permissions.")
            .WithOpenApiDoc(operation =>
            {
                Describe(operation, "projectShortName", "LEXIS Project Short-Name.");
                Describe(operation, "resourceName", "LEXIS Project Resource name. Only the resource's HPC and Quantum locations are considered; partitions on any other location type are left out.");
                return operation;
            })
            .WithTags("Heappe")
            .Produces<LocationPartitionResponse[]>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status404NotFound);

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