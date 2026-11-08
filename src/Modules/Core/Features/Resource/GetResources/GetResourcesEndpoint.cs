using Core.Domain.Shared;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Core.Features.Resource.GetResources;

internal static class GetResourcesEndpoint
{
    internal static async Task<IResult> HandleAsync(
        HttpContext http,
        [FromRoute] string providerName,
        [FromQuery] string? projectShortName,
        [FromQuery] ELocationType? locationType,
        [FromQuery] DateTimeOffset? startDate,
        [FromQuery] DateTimeOffset? endDate,
        Caller caller,
        [AsParameters] PageRequest page,
        [FromServices] IResourceQuery resources,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        IOptions<PaginationOptions> pagination,
        CancellationToken ct)
    {
        var limit = page.ResolveLimit(pagination.Value);

        // A resource's natural key is (providerName, resourceName); the provider comes from the route and
        // narrows the caller-scoped result to that provider's resources (ADR-0022).
        if (!Core.Domain.Provider.ProviderName.TryCreate(providerName, out var provider, out _))
            return Problems.Validation(ProviderErrorCodes.InvalidName, "Provider name is invalid.");

        ProjectShortName? shortName = null;
        if (!string.IsNullOrWhiteSpace(projectShortName))
        {
            if (!ProjectShortName.TryParse(projectShortName, null, out var sn))
                return Problems.Validation("request.invalid_project_short_name", "Invalid project short name.");
            shortName = sn;
        }

        // Admin sees every resource; everyone else — including a provider identity — sees only the resources
        // reachable through their project scope. This route carries no provider tier: the provider-wide view
        // lives on the dedicated GET ProviderAdmin/Providers/{providerName}/Resources route (ADR-0058, ADR-0094).
        // The route's provider always narrows the set (FilterProviderName) regardless of tier.
        IReadOnlyList<ProjectShortName>? scopeShortNames = caller.IsAdmin(roleOptions.Value) ? null : [.. caller.Scope];

        var filter = new ResourceFilter(
            scopeShortNames, shortName, locationType, startDate, endDate,
            ScopeProviderName: null, FilterProviderName: provider);
        var queryPage = await resources.GetResourcesAsync(filter, page.EffectivePage, limit, ct);

        var response = queryPage.Items.Select(ResourceResponse.From).ToList();

        return PagedResponse.Offset(http, response, page.EffectivePage, limit, queryPage.TotalItems);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetResourcesEndpoint()
        {
            app.MapGet("Providers/{providerName}/Resources", HandleAsync)
                .RejectRemovedQueryParameter("locationTypeId", "locationType")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetResourcesV2")
            .WithSummary("List a provider's resources")
            .WithDescription("Returns the provider's resources that the caller can see (assigned to a project the caller has a role on), optionally filtered by project short name, location type name (locationType, for example HPC; Unspecified matches nothing) and validity dates. Each assignment carries its LocationType as a name.")
            .WithTags("Resource")
            .Produces<IReadOnlyList<ResourceResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);
    }
}