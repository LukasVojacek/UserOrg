using Core.Domain.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Core.Features.Location.GetLocation;

internal static class GetLocationEndpoint
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

    internal sealed record Request([FromRoute] string ProviderName, [FromRoute] string LocationName);

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        Caller caller,
        [FromServices] ILocationQuery locations,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        CancellationToken ct)
    {
        // A location's natural key spans its provider (it belongs to exactly one), so it is addressed by the
        // nested (providerName, locationName) pair, not a surrogate GUID (ADR-0022, ADR-0029).
        if (!Core.Domain.Provider.ProviderName.TryCreate(request.ProviderName, out var providerName, out _))
            return Problems.Validation(ProviderErrorCodes.InvalidName, "Provider name is invalid.");
        if (!Core.Domain.Provider.LocationName.TryCreate(request.LocationName, out var locationName, out _))
            return Problems.Validation("location.invalid_name", "Location name is invalid.");

        // Tier the read (ADR-0018): admin and a provider service key resolve any active location (scope == null);
        // a user — and a human ProviderAdmin asking about another provider — resolves it only when reachable
        // through one of their projects. A name pair that matches nothing and one hidden out of scope both yield
        // null and emit the identical 404 (ADR-0018 rider).
        var scope = caller.ResolveProjectScopeFor(providerName, roleOptions.Value);

        var result = await locations.GetLocationByProviderAndNameAsync(providerName, locationName, scope, ct);
        if (result is null)
            return Problems.NotFound("location.not_found", "Location not found.");

        return Results.Ok(new Response(
            result.Name, result.Description, result.Endpoint,
            result.IsPrivate, result.IsShared, result.LocationType,
            result.ProviderName, result.Mode.ToString(), result.DefaultServiceIntegrationUrl));
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetLocationEndpoint()
        {
            app.MapGet("Providers/{providerName}/Locations/{locationName}", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetLocationByProviderAndNameV2")
            .WithSummary("Get location by provider and name")
            .WithDescription("Returns a single active location addressed by its provider and name, with its LocationType as a name.")
            .WithTags("Location")
            .Produces<Response>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}