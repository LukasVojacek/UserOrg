using Core.Domain.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace Core.Features.Project.GetExtendedProject;

internal static class GetExtendedProjectEndpoint
{
    internal sealed record SpecificationEntry(string Name, string Value);

    internal sealed record AssignedResourceResponse(
        string ResourceName,
        string? ResourceDescription,
        string LocationName,
        ELocationType LocationType,
        long? AllocationAmount,
        EAllocationUnitType AllocationUnitType,
        string? AggregationName,
        IReadOnlyList<SpecificationEntry> Specifications);

    internal sealed record Response(
        string ShortName,
        string Name,
        string? Description,
        string? Email,
        DateTimeOffset StartDate,
        DateTimeOffset EndDate,
        string DomainTypeName,
        IReadOnlyList<string> AssignedTags,
        IReadOnlyList<AssignedResourceResponse> AssignedResources);

    internal static async Task<IResult> HandleAsync(
        [FromRoute] ProjectShortName projectShortName,
        Caller caller,
        [FromServices] IProjectQuery projects,
        CancellationToken ct)
    {
        if (caller.RequireProjectScope(projectShortName) is { } denied)
            return denied;

        var result = await projects.GetExtendedProjectAsync(projectShortName, ct);
        if (result is null)
            return Problems.ProjectNotFound();

        return Results.Ok(new Response(
            result.ShortName.Value,
            result.Name.Value,
            result.Description?.Value is { Length: > 0 } desc ? desc : null,
            result.Email?.Value is { Length: > 0 } email ? email : null,
            result.StartDate,
            result.EndDate,
            result.DomainTypeName.Value,
            [.. result.AssignedTags.Select(t => t.Value)],
            result.AssignedResources
                .Select(r => new AssignedResourceResponse(
                    r.ResourceName,
                    r.ResourceDescription,
                    r.LocationName,
                    r.LocationType,
                    r.AllocationAmount,
                    r.AllocationUnitType,
                    r.AggregationName,
                    r.Specifications.Select(s => new SpecificationEntry(s.Name, s.Value)).ToList()))
                .ToList()));
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetExtendedProjectEndpoint()
        {
            app.MapGet("Projects/{projectShortName}/Extended", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetExtendedProjectV2")
            .WithSummary("Get extended project info")
            .WithDescription("Returns a project with its full resource assignment and location allocation details. Each assigned resource carries its location's LocationType as a name.")
            .WithTags("Project")
            .Produces<Response>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}