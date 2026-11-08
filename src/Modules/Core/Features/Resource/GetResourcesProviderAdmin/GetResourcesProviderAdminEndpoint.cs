using Core.Composition;
using Core.Domain.Shared;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Core.Features.Resource.GetResourcesProviderAdmin;

internal static class GetResourcesProviderAdminEndpoint
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

        // Tier the result (ADR-0018): admin sees every resource, everyone else only resources reachable through
        // their project scope. A provider identity adds its own provider's resources to that set — it must not
        // replace it, or the tier would hide the resources it already reaches through a project. The route's
        // provider always narrows the set (FilterProviderName) regardless of tier. Unlike the other
        // ProviderAdmin/* routes, which pin the tier to the own provider, this one deliberately keeps the
        // project union, for now.
        IReadOnlyList<ProjectShortName>? scopeShortNames = null;
        Core.Domain.Provider.ProviderName? scopeProviderName = null;
        switch (caller.ResolveReadScope(roleOptions.Value))
        {
            case ReadScope.All:
            break;
            case ReadScope.Provider providerScope:
            scopeShortNames = [.. caller.Scope];
            scopeProviderName = providerScope.ProviderName;
            break;
            case ReadScope.Scoped scoped:
            scopeShortNames = [.. scoped.Projects];
            break;
        }

        var filter = new ResourceFilter(
            scopeShortNames, shortName, locationType, startDate, endDate,
            ScopeProviderName: scopeProviderName, FilterProviderName: provider);
        var queryPage = await resources.GetResourcesAsync(filter, page.EffectivePage, limit, ct);

        var response = queryPage.Items.Select(ResourceAdminResponse.From).ToList();

        return PagedResponse.Offset(http, response, page.EffectivePage, limit, queryPage.TotalItems);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetResourcesProviderAdminEndpoint()
        {
            app.MapGet("ProviderAdmin/Providers/{providerName}/Resources", HandleAsync)
                .RejectRemovedQueryParameter("locationTypeId", "locationType")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetResourcesProviderAdminV2")
            .WithSummary("List a provider's resources")
            .WithDescription("Returns the provider's resources that the caller can see (assigned to a project the caller has a role on), optionally filtered by project short name, location type name (locationType, for example HPC; Unspecified matches nothing) and validity dates. Each assignment carries its LocationType as a name.")
            .WithTags("ProviderAdmin")
            .WithGroupName(ApiAudience.ProviderAdmin)
            .Produces<IReadOnlyList<ResourceResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);
    }
}