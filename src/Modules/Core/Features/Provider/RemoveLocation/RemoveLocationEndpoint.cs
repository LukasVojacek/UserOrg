using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;

namespace Core.Features.Provider.RemoveLocation;

/// <summary>
/// Soft-deletes a location through its provider aggregate. Admin may remove under any provider; a
/// provider service-key identity only under its own (ADR-0011). Mirrors legacy <c>DELETE api/Location/{Id}</c>.
/// </summary>
internal static class RemoveLocationEndpoint
{
    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [FromRoute] string providerName,
        [FromRoute] string locationName,
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
                AuditActions.ProviderLocationRemoved, caller, data: new Dictionary<string, object?> { ["provider"] = providerName }) with
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

        var provider = await repository.FindAsync(name, ct);
        if (provider is null)
            return Problems.NotFound("provider.not_found", "Provider not found.");

        var result = provider.RemoveLocation(location);
        if (!result.IsSuccess)
            return result.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProviderLocationRemoved, caller, data: new Dictionary<string, object?> { ["provider"] = name.Value }) with
        {
            TargetType = AuditActions.TargetTypes.Location,
            TargetId = location.Value,
        }, logger);
        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapRemoveLocationEndpoint()
        {
            app.MapDelete("Providers/{providerName}/Locations/{locationName}", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.ProviderAdmin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("RemoveLocationV2")
            .WithSummary("Remove a provider's location")
            .WithDescription("Soft-deletes a location (Admin or the owning provider identity). Returns 204; 403 when not allowed; 404 when the provider or location does not exist; 409 when the location is already deleted.")
            .WithTags("Provider")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}