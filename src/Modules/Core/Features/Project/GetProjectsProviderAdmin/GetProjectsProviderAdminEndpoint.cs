using Core.Composition;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

using DomainProviderName = Core.Domain.Provider.ProviderName;

namespace Core.Features.Project.GetProjectsProviderAdmin;

internal static class GetProjectsProviderAdminEndpoint
{
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
        Caller caller,
        [AsParameters] PageRequest page,
        [FromServices] IProjectQuery projects,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        IOptions<PaginationOptions> pagination,
        CancellationToken ct)
    {
        var limit = page.ResolveLimit(pagination.Value);

        // Read-scope the provider-admin listing (ADR-0058, mirroring the sibling GetLocationsProviderAdmin,
        // ADR-0018): a platform admin sees every project; a provider identity sees only the projects that
        // consume its provider's resources, narrowed through ProjectFilter.ProviderName; anyone else falls
        // back to their ordinary caller scope.
        DomainProviderName? filterProviderName = null;
        IReadOnlyList<ProjectShortName>? scopeShortNames = null;
        switch (caller.ResolveReadScope(roleOptions.Value))
        {
            case ReadScope.All:
            break;
            case ReadScope.Provider providerScope:
            filterProviderName = providerScope.ProviderName;
            break;
            case ReadScope.Scoped scoped:
            scopeShortNames = [.. scoped.Projects];
            break;
        }

        var filter = new ProjectFilter(
            ShortName: null,
            ContactEmail: null,
            DomainTypeName: null,
            StartDate: null,
            EndDate: null,
            ScopeShortNames: scopeShortNames,
            ProviderName: filterProviderName);

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
        internal IEndpointRouteBuilder MapGetProjectsProviderAdminEndpoint()
        {
            app.MapGet("ProviderAdmin/Projects", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetProjectsProviderAdminV2")
            .WithSummary("List projects")
            .WithDescription("Returns projects scoped to the caller: all for a platform admin, the projects consuming the caller's own provider's resources for a provider identity, or the caller's own project scope otherwise.")
            .WithTags("ProviderAdmin")
            .WithGroupName(ApiAudience.ProviderAdmin)
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK);
    }
}