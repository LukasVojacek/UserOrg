using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;

namespace Core.Features.Provider.ReactivateProvider;

/// <summary>
/// Reactivates a previously deactivated provider. Admin may reactivate any provider; a provider
/// service-key identity only its own (ADR-0011). Loads through the soft-delete filter so the
/// deactivated target is visible.
/// </summary>
internal static class ReactivateProviderEndpoint
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
                AuditActions.ProviderSelfReactivated, caller) with
            {
                TargetType = AuditActions.TargetTypes.Provider,
                TargetId = providerName,
                Reason = AuditActions.DenialReasons.ProviderWriteForbidden,
            }, logger);
            return Problems.ProviderWriteForbidden();
        }

        if (!ProviderName.TryCreate(providerName, out var name, out var providerNameError))
            return Problems.Validation(ProviderErrorCodes.InvalidName, providerNameError);

        // FindAsync filters out deactivated providers, so the reactivation target must be loaded
        // including soft-deleted rows.
        var provider = await repository.FindIncludingDeactivatedAsync(name, ct);
        if (provider is null)
            return Problems.NotFound("provider.not_found", "Provider not found.");

        var result = provider.Reactivate();
        if (!result.IsSuccess)
            return result.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProviderSelfReactivated, caller) with
        {
            TargetType = AuditActions.TargetTypes.Provider,
            TargetId = name.Value,
        }, logger);
        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapReactivateProviderEndpoint()
        {
            app.MapPost("Providers/{providerName}/Reactivate", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.ProviderAdmin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("ReactivateProviderV2")
            .WithSummary("Reactivate a provider")
            .WithDescription("Reactivates a deactivated provider (Admin or the owning provider identity). Returns 204; 403 when not allowed; 404 when the provider does not exist; 409 when it is not deactivated.")
            .WithTags("Provider")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}