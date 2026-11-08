using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

using DomainProviderName = Core.Domain.Provider.ProviderName;

namespace Core.Features.Provider.GetProvider;

internal static class GetProviderEndpoint
{
    internal sealed record Request([FromRoute] string ProviderName);

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        Caller caller,
        [FromServices] IProviderQuery providers,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        CancellationToken ct)
    {
        if (!DomainProviderName.TryCreate(request.ProviderName, out var providerName, out var providerNameError))
            return Problems.Validation(ProviderErrorCodes.InvalidName, providerNameError);

        // Tier the read (ADR-0018): admin and a provider service key resolve any active provider (scope == null);
        // a user — and a human ProviderAdmin asking about another provider — resolves a private provider only
        // when reachable through one of their projects. The query turns out-of-scope into the same 404 as
        // not-found so existence never leaks.
        var scope = caller.ResolveProjectScopeFor(providerName, roleOptions.Value);

        var provider = await providers.GetProviderDetailsAsync(providerName, scope, ct);
        if (provider is null)
            return Problems.NotFound("provider.not_found", "Provider not found.");
        return Results.Ok(GetProviderResponse.From(provider));
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetProviderEndpoint()
        {
            app.MapGet("Providers/{providerName}", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetProviderByNameV2")
            .WithSummary("Get provider by name")
            .WithDescription("Returns the provider details for the specified provider name.")
            .WithTags("Provider")
            .Produces<GetProviderResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}