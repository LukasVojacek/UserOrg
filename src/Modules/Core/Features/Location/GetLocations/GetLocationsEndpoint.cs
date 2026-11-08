using Core.Domain.Shared;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

using DomainProviderName = Core.Domain.Provider.ProviderName;

namespace Core.Features.Location.GetLocations;

internal static class GetLocationsEndpoint
{
    internal sealed record Response(
        string Name,
        string? Description,
        string Endpoint,
        bool IsPrivate,
        bool IsShared,
        ELocationType LocationType,
        string ProviderName,
        string Mode,
        string? DefaultServiceIntegrationUrl);

    internal static async Task<IResult> HandleAsync(
        HttpContext http,
        [FromQuery] string? providerName,
        [FromQuery] ELocationType? locationType,
        Caller caller,
        [AsParameters] PageRequest page,
        [FromServices] ILocationQuery locations,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        IOptions<PaginationOptions> pagination,
        CancellationToken ct)
    {
        var limit = page.ResolveLimit(pagination.Value);

        // Narrow the result by tier (ADR-0018): admin sees all; everyone else only locations linked to a project
        // in their scope. (Fixes the pre-F0 leak where any role saw every location, including private ones.) A
        // provider identity adds its own provider's locations to that set — it must not replace it, or the tier
        // would hide the locations it already reaches through a project. The optional user-facing providerName
        // filter narrows whatever the tier allows, in every tier.
        DomainProviderName? filterProviderName = null;
        if (!string.IsNullOrWhiteSpace(providerName))
        {
            if (!DomainProviderName.TryCreate(providerName, out var queryName, out var providerNameError))
                return Problems.Validation(ProviderErrorCodes.InvalidName, providerNameError);
            filterProviderName = queryName;
        }

        IReadOnlyList<ProjectShortName>? scopeShortNames = null;
        DomainProviderName? ownProviderName = null;
        switch (caller.ResolveReadScope(roleOptions.Value))
        {
            case ReadScope.All:
            break;
            case ReadScope.Provider providerScope:
            scopeShortNames = [.. caller.Scope];
            ownProviderName = providerScope.ProviderName;
            break;
            case ReadScope.Scoped scoped:
            scopeShortNames = [.. scoped.Projects];
            break;
        }

        var filter = new LocationFilter(filterProviderName, locationType, scopeShortNames, ownProviderName);
        var queryPage = await locations.GetLocationsAsync(filter, page.EffectivePage, limit, ct);

        var response = queryPage.Items.Select(l => new Response(
            l.Name, l.Description, l.Endpoint,
            l.IsPrivate, l.IsShared, l.LocationType,
            l.ProviderName, l.Mode.ToString(), l.DefaultServiceIntegrationUrl)).ToList();

        return PagedResponse.Offset(http, response, page.EffectivePage, limit, queryPage.TotalItems);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetLocationsEndpoint()
        {
            app.MapGet("Locations", HandleAsync)
                .RejectRemovedQueryParameter("locationTypeId", "locationType")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetLocationsV2")
            .WithSummary("List locations")
            .WithDescription("Returns all active locations, optionally filtered by provider name or by location type name (locationType, for example HPC; Unspecified matches nothing). Each location carries its LocationType as a name.")
            .WithTags("Location")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}