using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

using DomainProviderName = Core.Domain.Provider.ProviderName;

namespace Core.Features.Provider.GetProviders;

internal static class GetProvidersEndpoint
{
    internal sealed record Request(
        [FromQuery] string? Name,
        [FromQuery] string? CountryCode);

    internal sealed record Response(
        string Name,
        string? Description,
        string? SupportEmail,
        string? ExternalId,
        string Mode,
        string ServiceName,
        bool IsPrivate,
        string CountryCode);

    internal static async Task<IResult> HandleAsync(
        HttpContext http,
        [AsParameters] Request request,
        Caller caller,
        [AsParameters] PageRequest page,
        [FromServices] IProviderQuery providers,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        IOptions<PaginationOptions> pagination,
        CancellationToken ct)
    {
        var limit = page.ResolveLimit(pagination.Value);

        DomainProviderName? name = null;
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            if (!DomainProviderName.TryCreate(request.Name, out var filterName, out var providerNameError))
                return Problems.Validation(ProviderErrorCodes.InvalidName, providerNameError);
            name = filterName;
        }

        // Tier the result (ADR-0018): admin sees every provider; everyone else sees the public providers plus
        // any private provider reachable through their project scope. Fixes the pre-F0 leak where any role saw
        // every provider, including private ones. A provider identity adds its own provider to that set — it
        // must not replace it, or the tier would hide the public and reachable providers it can see today.
        var onlyPublic = false;
        IReadOnlyList<ProjectShortName>? scopeShortNames = null;
        DomainProviderName? ownProviderName = null;
        switch (caller.ResolveReadScope(roleOptions.Value))
        {
            case ReadScope.All:
            break;
            case ReadScope.Provider providerScope:
            onlyPublic = true;
            scopeShortNames = [.. caller.Scope];
            ownProviderName = providerScope.ProviderName;
            break;
            case ReadScope.Scoped scoped:
            onlyPublic = true;
            scopeShortNames = [.. scoped.Projects];
            break;
        }

        var filter = new ProviderFilter(name, CountryCodeFilter(request.CountryCode), onlyPublic, scopeShortNames, ownProviderName);

        var queryPage = await providers.GetProvidersAsync(filter, page.EffectivePage, limit, ct);

        var response = queryPage.Items
            .Select(p => new Response(
                p.Name.Value,
                string.IsNullOrEmpty(p.Description?.Value) ? null : p.Description?.Value,
                string.IsNullOrEmpty(p.SupportEmail?.Value) ? null : p.SupportEmail?.Value,
                p.ExternalId?.Value,
                p.Mode.ToString(),
                p.ServiceName.Value,
                p.IsPrivate,
                p.CountryCode))
            .ToList();

        return PagedResponse.Offset(http, response, page.EffectivePage, limit, queryPage.TotalItems);
    }

    private static string? CountryCodeFilter(string? countryCode) =>
        string.IsNullOrWhiteSpace(countryCode) ? null : countryCode.Trim();

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetProvidersEndpoint()
        {
            app.MapGet("Providers", HandleAsync)
                .RejectRemovedQueryParameter("countryId", "countryCode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetProvidersV2")
            .WithSummary("List providers")
            .WithDescription("Returns providers, optionally filtered by name and country. Paged via ?page=&limit= (default 50, max 200); the response body is the bare array, with pagination in the X-Total-Count and Link response headers.")
            .WithTags("Provider")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}