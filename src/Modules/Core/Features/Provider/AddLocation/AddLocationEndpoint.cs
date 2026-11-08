using Core.Domain.Shared;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;

namespace Core.Features.Provider.AddLocation;

/// <summary>
/// Adds a location under a provider. Locations are children of the Provider aggregate, so this loads
/// and mutates the provider. Resource allocations are assigned separately (they require an existing
/// ProjectResource — legacy parity). Admin may add under any provider; a provider service-key identity
/// only under its own (ADR-0011). Mirrors legacy <c>POST api/Location</c>.
/// </summary>
internal static class AddLocationEndpoint
{
    internal sealed class AuditCategory;

    internal sealed record Request(
        string Name,
        string Endpoint,
        ELocationType LocationType,
        bool IsPrivate,
        bool IsShared,
        string? Description);

    internal sealed record Response(string ProviderName, string Name);

    internal static async Task<IResult> HandleAsync(
        [FromRoute] string providerName,
        [FromBody] Request request,
        Caller caller,
        [FromServices] IProviderRepository repository,
        [FromServices] ILocationQuery locationQuery,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        CancellationToken ct)
    {
        if (!ProviderWriteAuthorization.Authorize(caller, providerName, roleOptions.Value))
        {
            await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.ProviderLocationAdded, caller, data: new Dictionary<string, object?> { ["provider"] = providerName }) with
            {
                TargetType = AuditActions.TargetTypes.Location,
                TargetId = request.Name,
                Reason = AuditActions.DenialReasons.ProviderWriteForbidden,
            }, logger);
            return Problems.ProviderWriteForbidden();
        }

        if (!ProviderName.TryCreate(providerName, out var name, out var providerNameError))
            return Problems.Validation(ProviderErrorCodes.InvalidName, providerNameError);

        if (!LocationName.TryCreate(request.Name, out var locationName, out var locationNameError))
            return Problems.Validation(ProviderErrorCodes.InvalidLocationName, locationNameError);

        if (ValidateRequest(request) is { } invalid)
            return invalid;

        var provider = await repository.FindAsync(name, ct);
        if (provider is null)
            return Problems.NotFound("provider.not_found", "Provider not found.");

        if (!await IsKnownLocationTypeAsync(request.LocationType, locationQuery, ct))
            return Problems.Validation(ProviderErrorCodes.InvalidLocationType, "LocationType must be a known location type.");

        var result = provider.AddLocation(
            locationName,
            request.Endpoint,
            request.LocationType,
            request.IsPrivate,
            request.IsShared,
            request.Description);

        if (!result.IsSuccess)
            return result.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProviderLocationAdded, caller, data: new Dictionary<string, object?> { ["provider"] = name.Value }) with
        {
            TargetType = AuditActions.TargetTypes.Location,
            TargetId = locationName.Value,
        }, logger);

        // Addressed by its natural key (providerName, locationName) — both known here. The Location is built
        // as an explicit URL (not CreatedAtRoute) so it is set at this point, not deferred to response
        // execution: that both keeps it GUID-free (ADR-0024) and guards the dead-route class of bug.
        return Results.Created($"v2/Providers/{name.Value}/Locations/{locationName.Value}", new Response(name.Value, locationName.Value));
    }

    private static IResult? ValidateRequest(Request request)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint) || request.Endpoint.Length > 200)
            return Problems.Validation(ProviderErrorCodes.InvalidEndpoint, "Endpoint is required and must not exceed 200 characters.");

        if (request.Description is { Length: > 150 })
            return Problems.Validation(CommonErrorCodes.InvalidFormat, "Description must not exceed 150 characters.");

        return null;
    }

    private static async Task<bool> IsKnownLocationTypeAsync(ELocationType locationType, ILocationQuery locationQuery, CancellationToken ct) =>
        locationType != ELocationType.Unspecified
        && Enum.IsDefined(locationType)
        && await locationQuery.LocationTypeExistsAsync(locationType, ct);

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapAddLocationEndpoint()
        {
            app.MapPost("Providers/{providerName}/Locations", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.ProviderAdmin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("AddLocationV2")
            .WithSummary("Add a location to a provider")
            .WithDescription("Adds a location under a provider (Admin or the owning provider identity). Body {Name, Endpoint, LocationType, IsPrivate, IsShared, Description?}; LocationType is required and given by name (e.g. \"HPC\"). Returns 201 with a Location header; 400 on validation, including provider.invalid_location_type for an Unspecified, undefined or unseeded LocationType; 403 when not allowed; 404 when the provider does not exist; 409 when a location with the same name already exists.")
            .WithTags("Provider")
            .Accepts<Request>("application/json")
            .Produces<Response>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}