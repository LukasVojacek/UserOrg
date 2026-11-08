using Core.Composition;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

using DomainProviderName = Core.Domain.Provider.ProviderName;

namespace Core.Features.Location.GetLocationsProviderAdmin;

internal static class GetLocationsProviderAdminEndpoint
{
    internal sealed record Response(
      string Name,
      string Endpoint);

    internal static async Task<IResult> HandleAsync(
        HttpContext http,
        Caller caller,
        [AsParameters] PageRequest page,
        [FromServices] ILocationQuery locations,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        IOptions<PaginationOptions> pagination,
        CancellationToken ct)
    {
        var limit = page.ResolveLimit(pagination.Value);

        // Read-scope the provider-admin listing (ADR-0058, mirroring the sibling GetLocations, ADR-0018):
        // a platform admin sees every location; a provider identity only its own provider's locations,
        // narrowed through LocationFilter.ProviderName. Drops the pre-F0 unfiltered global projection.
        //
        // Own-provider here is the route's contract, not an oversight: this is the administrator's view of the
        // provider they administer, which is why it exists beside GET /locations rather than inside it. So
        // unlike the general listings, the provider tier deliberately does NOT union the caller's project
        // scope — since D5 an interactive ProviderAdmin lands in this tier, and reads their project-reachable
        // locations from GET /Locations, which does union. Matches the sibling ProviderAdmin/Projects.
        // Unioning here is therefore a behaviour change, not a bug fix: GetLocationsProviderAdminEndpointTests
        // .Should_ScopeToOwnProviderOnly_When_CallerIsInteractiveProviderAdminWithProjects fails on purpose.
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

        var filter = new LocationFilter(filterProviderName, null, scopeShortNames);
        var queryPage = await locations.GetLocationsAsync(filter, page.EffectivePage, limit, ct);

        var response = queryPage.Items.Select(l => new Response(
            l.Name, l.Endpoint)).ToList();

        return PagedResponse.Offset(http, response, page.EffectivePage, limit, queryPage.TotalItems);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetLocationsProviderAdminEndpoint()
        {
            app.MapGet("ProviderAdmin/Locations", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetLocationsProviderAdminV2")
            .WithSummary("List locations")
            .WithDescription("Returns active locations scoped to the caller: all for a platform admin, or the caller's own provider's locations for a provider identity.")
            .WithTags("ProviderAdmin")
            .WithGroupName(ApiAudience.ProviderAdmin)
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK);
    }
}