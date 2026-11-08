using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;

namespace Core.Features.Provider.UpdateLocation;

/// <summary>
/// Updates a location's endpoint and description through its provider aggregate. Admin may update under
/// any provider; a provider service-key identity only under its own (ADR-0011). Mirrors legacy
/// <c>PUT api/Location</c>.
/// </summary>
internal static class UpdateLocationEndpoint
{
    internal sealed class AuditCategory;

    internal sealed record Request(string Endpoint, string? Description);

    internal static async Task<IResult> HandleAsync(
        [FromRoute] string providerName,
        [FromRoute] string locationName,
        [FromBody] Request request,
        Caller caller,
        [FromServices] IProviderRepository repository,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        CancellationToken ct)
    {
        if (!ProviderWriteAuthorization.Authorize(caller, providerName, roleOptions.Value))
        {
            await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.ProviderLocationUpdated, caller, data: new Dictionary<string, object?> { ["provider"] = providerName }) with
            {
                TargetType = AuditActions.TargetTypes.Location,
                TargetId = locationName,
                Reason = AuditActions.DenialReasons.ProviderWriteForbidden,
            }, logger);
            return Problems.ProviderWriteForbidden();
        }

        if (!ProviderName.TryCreate(providerName, out var name, out var providerNameError))
            return Problems.Validation(ProviderErrorCodes.InvalidName, providerNameError);

        if (!LocationName.TryCreate(locationName, out var location, out var locationNameError))
            return Problems.Validation(ProviderErrorCodes.InvalidLocationName, locationNameError);

        if (ValidateRequest(request) is { } invalid)
            return invalid;

        var provider = await repository.FindAsync(name, ct);
        if (provider is null)
            return Problems.NotFound("provider.not_found", "Provider not found.");

        var result = provider.UpdateLocation(location, request.Endpoint, request.Description);
        if (!result.IsSuccess)
            return result.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProviderLocationUpdated, caller, data: new Dictionary<string, object?> { ["provider"] = name.Value }) with
        {
            TargetType = AuditActions.TargetTypes.Location,
            TargetId = location.Value,
        }, logger);
        return Results.NoContent();
    }

    private static IResult? ValidateRequest(Request request)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint) || request.Endpoint.Length > 200)
            return Problems.Validation(ProviderErrorCodes.InvalidEndpoint, "Endpoint is required and must not exceed 200 characters.");

        if (request.Description is { Length: > 150 })
            return Problems.Validation(CommonErrorCodes.InvalidFormat, "Description must not exceed 150 characters.");

        return null;
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapUpdateLocationEndpoint()
        {
            app.MapPut("Providers/{providerName}/Locations/{locationName}", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.ProviderAdmin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("UpdateLocationV2")
            .WithSummary("Update a provider's location")
            .WithDescription("Updates a location's endpoint and description (Admin or the owning provider identity). Body {Endpoint, Description?}. Returns 204; 400 on validation; 403 when not allowed; 404 when the provider or location does not exist; 409 when the location is deleted.")
            .WithTags("Provider")
            .Accepts<Request>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}