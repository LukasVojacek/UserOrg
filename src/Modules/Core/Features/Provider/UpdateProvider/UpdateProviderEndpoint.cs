using Core.Domain.Shared;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;

using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.Features.Provider.UpdateProvider;

/// <summary>
/// Updates a provider's description, support email, privacy, and country. The name is the natural key
/// and is not mutable. Admin may update any provider; every other caller only the provider its
/// <c>providerName</c> claim names, whether it authenticated interactively or with a service key
/// (ADR-0011, ADR-0105). Mirrors legacy <c>PUT api/Provider</c>.
/// </summary>
internal static class UpdateProviderEndpoint
{
    internal sealed class AuditCategory;

    internal sealed record Request(
        string CountryCode,
        string? Description,
        string? SupportEmail,
        bool IsPrivate);

    internal static async Task<IResult> HandleAsync(
        [FromRoute] string providerName,
        [FromBody] Request request,
        Caller caller,
        [FromServices] IProviderRepository repository,
        [FromServices] ICountryQuery countryQuery,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        CancellationToken ct)
    {
        if (!ProviderWriteAuthorization.Authorize(caller, providerName, roleOptions.Value))
        {
            await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.ProviderSelfUpdated, caller) with
            {
                TargetType = AuditActions.TargetTypes.Provider,
                TargetId = providerName,
                Reason = AuditActions.DenialReasons.ProviderWriteForbidden,
            }, logger);
            return Problems.ProviderWriteForbidden();
        }

        if (!ProviderName.TryCreate(providerName, out var name, out var providerNameError))
            return Problems.Validation(ProviderErrorCodes.InvalidName, providerNameError);

        if (ValidateRequest(request, out var description, out var supportEmail) is { } invalid)
            return invalid;

        var countryId = await countryQuery.FindIdByCodeAsync(request.CountryCode, ct);
        if (countryId is null)
            return Problems.Validation(ProviderErrorCodes.CountryNotFound, "Country code does not exist.");

        var provider = await repository.FindAsync(name, ct);
        if (provider is null)
            return Problems.NotFound("provider.not_found", "Provider not found.");

        if (ApplyChanges(provider, description, supportEmail, request.IsPrivate, countryId.Value) is { } rejected)
            return rejected;

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProviderSelfUpdated, caller) with
        {
            TargetType = AuditActions.TargetTypes.Provider,
            TargetId = name.Value,
        }, logger);
        return Results.NoContent();
    }

    private static IResult? ValidateRequest(Request request, out Description? description, out Email? supportEmail)
    {
        description = null;
        supportEmail = null;

        if (string.IsNullOrWhiteSpace(request.CountryCode))
            return Problems.Validation(CommonErrorCodes.Required, "Country code is required.");

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            if (request.Description.Length > 500)
                return Problems.Validation(CommonErrorCodes.InvalidFormat, "Description must not exceed 500 characters.");
            description = Domain.Shared.Description.Create(request.Description);
        }

        if (!string.IsNullOrWhiteSpace(request.SupportEmail))
        {
            if (!Email.TryParse(request.SupportEmail, null, out var parsed))
                return Problems.Validation(CommonErrorCodes.InvalidFormat, "Invalid email address.");
            supportEmail = parsed;
        }

        return null;
    }

    private static IResult? ApplyChanges(
        ProviderAggregate provider, Description? description, Email? supportEmail, bool isPrivate, int countryId)
    {
        if (provider.UpdateDetails(description, supportEmail) is { IsSuccess: false } detailsResult)
            return detailsResult.Error.ToProblem();
        if (provider.SetPrivacy(isPrivate) is { IsSuccess: false } privacyResult)
            return privacyResult.Error.ToProblem();
        if (provider.ChangeCountry(countryId) is { IsSuccess: false } countryResult)
            return countryResult.Error.ToProblem();

        return null;
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapUpdateProviderEndpoint()
        {
            app.MapPut("Providers/{providerName}", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.ProviderAdmin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("UpdateProviderV2")
            .WithSummary("Update a provider")
            .WithDescription("Updates a provider's description, support email, privacy, and country (Admin or the owning provider identity). Body {CountryCode, Description?, SupportEmail?, IsPrivate}. Returns 204; 400 on validation / unknown country; 403 when not allowed; 404 when the provider does not exist.")
            .WithTags("Provider")
            .Accepts<Request>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}