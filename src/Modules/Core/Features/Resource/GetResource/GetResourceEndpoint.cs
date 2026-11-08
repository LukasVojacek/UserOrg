using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Core.Features.Resource.GetResource;

internal static class GetResourceEndpoint
{
    internal sealed record Request([FromRoute] string ProviderName, [FromRoute] string ResourceName);

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        Caller caller,
        [FromServices] IResourceQuery resources,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        CancellationToken ct)
    {
        // A resource is addressed by its full natural key (providerName, resourceName), not a surrogate GUID —
        // the name alone is not unique across providers (ADR-0022).
        if (!Core.Domain.Provider.ProviderName.TryCreate(request.ProviderName, out var provider, out _))
            return Problems.Validation(ProviderErrorCodes.InvalidName, "Provider name is invalid.");

        if (!Core.Domain.Provider.ResourceName.TryParse(request.ResourceName, null, out var name))
            return Problems.Validation("resource.invalid_name", "Resource name is invalid.");

        // Tier the read (ADR-0018): admin and a provider service key resolve any active resource (scope == null);
        // a user — and a human ProviderAdmin asking about another provider — resolves it only when assigned to
        // one of their projects. Passing caller.Scope unconditionally would 404 admins and service keys (their
        // scope is empty).
        var scope = caller.ResolveProjectScopeFor(provider, roleOptions.Value);

        var resource = await resources.GetResourceByProviderAndNameAsync(provider, name, scope, ct);

        // Genuinely-missing, wrong-provider and out-of-scope (the query returns null for all) emit the identical
        // problem so existence never leaks (ADR-0018 rider).
        return resource is null
            ? Problems.NotFound("resource.not_found", "Resource not found.")
            : Results.Ok(ResourceResponse.From(resource));
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetResourceEndpoint()
        {
            app.MapGet("Providers/{providerName}/Resources/{resourceName}", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetResourceByNameV2")
            .WithSummary("Get a provider's resource by name")
            .WithDescription("Returns a single resource by its (provider, name) natural key if it is assigned to a project the caller has a role on; otherwise 404. Each assignment carries its LocationType as a name.")
            .WithTags("Resource")
            .Produces<ResourceResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}