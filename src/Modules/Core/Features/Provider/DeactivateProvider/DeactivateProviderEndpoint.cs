using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;

namespace Core.Features.Provider.DeactivateProvider;

/// <summary>
/// Deactivates (soft-deletes) a provider. Admin may deactivate any provider; a provider service-key
/// identity only its own (ADR-0011). Mirrors legacy <c>DELETE api/Provider/{Id}</c>.
/// </summary>
internal static class DeactivateProviderEndpoint
{
    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [FromRoute] string providerName,
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
                AuditActions.ProviderSelfDeactivated, caller) with
            {
                TargetType = AuditActions.TargetTypes.Provider,
                TargetId = providerName,
                Reason = AuditActions.DenialReasons.ProviderWriteForbidden,
            }, logger);
            return Problems.ProviderWriteForbidden();
        }

        if (!ProviderName.TryCreate(providerName, out var name, out var providerNameError))
            return Problems.Validation(ProviderErrorCodes.InvalidName, providerNameError);

        // Load including deactivated so an already-deactivated provider yields the domain 409 (not 404).
        var provider = await repository.FindIncludingDeactivatedAsync(name, ct);
        if (provider is null)
            return Problems.NotFound("provider.not_found", "Provider not found.");

        var result = provider.Deactivate();
        if (!result.IsSuccess)
            return result.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProviderSelfDeactivated, caller) with
        {
            TargetType = AuditActions.TargetTypes.Provider,
            TargetId = name.Value,
        }, logger);
        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapDeactivateProviderEndpoint()
        {
            app.MapDelete("Providers/{providerName}", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.Admin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("DeactivateProviderV2")
            .WithSummary("Deactivate a provider")
            .WithDescription("Soft-deletes a provider (Admin or the owning provider identity). Returns 204; 403 when not allowed; 404 when the provider does not exist; 409 when it is already deactivated.")
            .WithTags("Provider")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}