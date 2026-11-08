using Core.DataAccess.Queries.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace UserOrg.Ddi.Features.Service;

/// <summary>
/// <c>GET /api/v2/Service/ProjectsInfo</c> — every active project-resource grouped by its owning provider,
/// with associated projects and location assignments. Reproduces the legacy <c>ProjectInfoModel</c> shape
/// (ADR-0017) over the <see cref="IDdiProjectsInfoQuery"/> read query.
/// </summary>
internal static class GetProjectsInfoEndpoint
{
    internal sealed record Response(
        string ProviderId,
        string? ProviderName,
        string ProviderDescription,
        IReadOnlyList<ResourceModel> Resources);

    internal sealed record ResourceModel(
        Guid Id,
        string Name,
        string? Description,
        DateTimeOffset? StartDate,
        DateTimeOffset? EndDate,
        IReadOnlyList<AssociatedProjectModel> AssociatedProjects,
        IReadOnlyList<AssignmentModel> Assignments);

    internal sealed record AssociatedProjectModel(
        Guid Id,
        string ProjectName,
        string ShortName,
        string CollectorName,
        DateTimeOffset? StartDate,
        DateTimeOffset? EndDate);

    internal sealed record AssignmentModel(
        string? LocationName,
        string? LocationDescription,
        string? ResourceType,
        long? AllocationAmount,
        EAllocationUnitType AllocationUnitType,
        string? AggregationName,
        IReadOnlyList<KeyValuePair<string, string>> Specifications);

    internal static async Task<IResult> HandleAsync(IDdiProjectsInfoQuery query, CancellationToken ct)
    {
        var views = await query.GetProjectsInfoAsync(ct);
        var response = views.Select(MapProvider).ToList();
        return Results.Ok(response);
    }

    private static Response MapProvider(ProviderResourcesView v) => new(
        v.ProviderId,
        v.ProviderName,
        v.ProviderDescription,
        v.Resources.Select(MapResource).ToList());

    private static ResourceModel MapResource(ProjectResourceInfoView r) => new(
        r.Id,
        r.Name,
        r.Description,
        r.StartDate,
        r.EndDate,
        r.AssociatedProjects.Select(a => new AssociatedProjectModel(
            a.Id, a.ProjectName, a.ShortName, a.CollectorName, a.StartDate, a.EndDate)).ToList(),
        r.Assignments.Select(MapAssignment).ToList());

    private static AssignmentModel MapAssignment(ResourceAssignmentInfoView a) => new(
        a.LocationName,
        a.LocationDescription,
        a.ResourceType,
        a.AllocationAmount,
        a.AllocationUnitType,
        a.AggregationName,
        // Legacy ProjectResourceAssignmentServiceModel.Specifications is a KeyValuePair list — preserve that wire shape.
        a.Specifications.Select(s => new KeyValuePair<string, string>(s.Name, s.Value)).ToList());

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetProjectsInfoEndpoint()
        {
            app.MapGet("ProjectsInfo", HandleAsync).MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetProjectsInfo")
            .WithSummary("Projects and resources grouped by provider, for the DDI service.")
            .WithTags("Service")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK);
    }
}