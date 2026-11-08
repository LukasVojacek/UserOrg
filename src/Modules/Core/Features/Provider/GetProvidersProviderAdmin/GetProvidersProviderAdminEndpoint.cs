using Core.Composition;
using Core.Features.Provider.GetProvider;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

using DomainProviderName = Core.Domain.Provider.ProviderName;

namespace Core.Features.Provider.GetProvidersProviderAdmin;

internal static class GetProvidersProviderAdminEndpoint
{
    internal static async Task<IResult> HandleAsync(
        HttpContext http,
        Caller caller,
        [AsParameters] PageRequest page,
        [FromServices] IProviderQuery providers,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        IOptions<PaginationOptions> pagination,
        CancellationToken ct,
        [FromQuery] bool includeInactive = false)
    {
        var limit = page.ResolveLimit(pagination.Value);

        IReadOnlyCollection<DomainProviderName>? names;
        switch (caller.ResolveReadScope(roleOptions.Value))
        {
            case ReadScope.All:
            names = null;
            break;
            case ReadScope.Provider providerScope:
            names = [providerScope.ProviderName];
            break;
            default:
            return PagedResponse.Offset<GetProviderResponse>(http, [], page.EffectivePage, limit, 0);
        }

        var queryPage = await providers.GetAdministeredProvidersAsync(
            new AdministeredProviderFilter(names, includeInactive), page.EffectivePage, limit, ct);

        return PagedResponse.Offset(http, [.. queryPage.Items.Select(GetProviderResponse.From)], page.EffectivePage, limit, queryPage.TotalItems);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetProvidersProviderAdminEndpoint()
        {
            app.MapGet("ProviderAdmin/Providers", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetProvidersProviderAdminV2")
            .WithSummary("List administered providers")
            .WithDescription("Paged via ?page=&limit= (bare array; X-Total-Count and Link headers). includeInactive=true also lists deactivated providers, flagged IsDeleted: true, with their non-deleted locations. All providers for a platform admin, the caller's own provider for a provider identity.")
            .WithTags("ProviderAdmin")
            .WithGroupName(ApiAudience.ProviderAdmin)
            .Produces<IReadOnlyList<GetProviderResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}