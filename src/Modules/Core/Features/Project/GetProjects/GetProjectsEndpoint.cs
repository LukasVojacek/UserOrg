using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Core.Features.Project.GetProjects;

internal static class GetProjectsEndpoint
{
    internal sealed record Request(
        [FromQuery] string? ShortName,
        [FromQuery] string? ContactEmail,
        [FromQuery] string? DomainTypeName,
        [FromQuery] DateTimeOffset? StartDate,
        [FromQuery] DateTimeOffset? EndDate);

    internal sealed record Response(
        string ShortName,
        string Name,
        string? Email,
        string? ExternalId,
        string Mode,
        string State,
        string ServiceName,
        DateTimeOffset StartDate,
        DateTimeOffset EndDate);

    internal static async Task<IResult> HandleAsync(
        HttpContext http,
        [AsParameters] Request request,
        Caller caller,
        [AsParameters] PageRequest page,
        [FromServices] IProjectQuery projects,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        IOptions<PaginationOptions> pagination,
        CancellationToken ct)
    {
        var limit = page.ResolveLimit(pagination.Value);

        ProjectShortName? shortName = null;
        if (!string.IsNullOrWhiteSpace(request.ShortName))
        {
            if (!ProjectShortName.TryParse(request.ShortName, null, out var sn))
                return Problems.Validation(ProjectErrorCodes.ShortNameInvalid, "Invalid project short name.");
            shortName = sn;
        }

        Email? contactEmail = null;
        if (!string.IsNullOrWhiteSpace(request.ContactEmail))
        {
            if (!Email.TryParse(request.ContactEmail, null, out var ce))
                return Problems.Validation(ProjectErrorCodes.ContactEmailInvalid, "Invalid email address.");
            contactEmail = ce;
        }

        DomainTypeName? domainTypeName = null;
        if (!string.IsNullOrWhiteSpace(request.DomainTypeName))
        {
            if (!DomainTypeName.TryCreate(request.DomainTypeName, out var parsed, out var domainTypeError))
                return Problems.Validation(ProjectErrorCodes.DomainTypeNameInvalid, domainTypeError);
            domainTypeName = parsed;
        }

        // Admin sees every project; everyone else — including a provider identity — sees only the
        // projects in their caller scope. This endpoint carries no provider tier: the provider-wide
        // view lives on the dedicated GET ProviderAdmin/Projects route (ADR-0058 pattern, ADR-0094).
        IReadOnlyList<ProjectShortName>? scope = caller.IsAdmin(roleOptions.Value) ? null : [.. caller.Scope];

        var filter = new ProjectFilter(
            shortName,
            contactEmail,
            domainTypeName,
            request.StartDate,
            request.EndDate,
            scope);

        var queryPage = await projects.GetProjectsAsync(filter, page.EffectivePage, limit, ct);

        var response = queryPage.Items
            .Select(p => new Response(
                p.ShortName.Value,
                p.Name.Value,
                p.Email?.Value,
                p.ExternalId?.Value,
                p.Mode.ToString(),
                p.State.ToString(),
                p.ServiceName.Value,
                p.StartDate,
                p.EndDate))
            .ToList();

        return PagedResponse.Offset(http, response, page.EffectivePage, limit, queryPage.TotalItems);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetProjectsEndpoint()
        {
            app.MapGet("Projects", HandleAsync)
                .RejectRemovedQueryParameter("domainId", "domainTypeName")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetProjectsV2")
            .WithSummary("List projects")
            .WithDescription("Returns projects the authenticated user has a role on, optionally filtered by short name, contact email, domain type, and validity dates. startDate and endDate are ISO-8601 timestamps and bound the project's own validity period inclusively: startDate keeps projects whose StartDate is on or after it, endDate keeps projects whose EndDate is on or before it.")
            .WithTags("Project")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}