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

namespace Core.Features.Provider.CreateProvider;

/// <summary>
/// Creates a provider. Admin-only, and the one recorded exception to the shared provider-write gate
/// (ADR-0105): a <c>ProviderAdmin</c> — interactive or service key — may manage the provider its
/// <c>providerName</c> claim names, but holds no claim for a provider that does not exist yet, so it may
/// not create a new top-level one (ADR-0011). Mirrors legacy <c>POST api/Provider</c>.
/// </summary>
internal static class CreateProviderEndpoint
{
    internal sealed class AuditCategory;

    internal sealed record Request(
        string Name,
        string CountryCode,
        string? Description,
        string? SupportEmail,
        bool IsPrivate);

    internal sealed record Response(string Name);

    internal static async Task<IResult> HandleAsync(
        [FromBody] Request request,
        Caller caller,
        [FromServices] IProviderRepository repository,
        [FromServices] IProviderQuery providerQuery,
        [FromServices] ICountryQuery countryQuery,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        CancellationToken ct)
    {
        // Create is Admin-only. The Gateway gate lets any provider-perm holder (incl. provider
        // service-keys) through, so Core must reject non-Admins here.
        if (!caller.IsAdmin(roleOptions.Value))
        {
            await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.ProviderSelfCreated, caller) with
            {
                TargetType = AuditActions.TargetTypes.Provider,
                TargetId = request.Name,
                Reason = AuditActions.DenialReasons.ProviderWriteForbidden,
            }, logger);
            return Problems.Forbidden("provider.write_forbidden", "Only an administrator may create a provider.");
        }

        if (!ProviderName.TryCreate(request.Name, out var name, out var nameError))
            return Problems.Validation(ProviderErrorCodes.InvalidName, nameError);

        if (ValidateRequest(request, out var description, out var supportEmail) is { } invalid)
            return invalid;

        var countryId = await countryQuery.FindIdByCodeAsync(request.CountryCode, ct);
        if (countryId is null)
            return Problems.Validation(ProviderErrorCodes.CountryNotFound, "Country code does not exist.");

        // Uniqueness includes deactivated providers — the name is the natural key (ADR-0011).
        if (await providerQuery.ProviderExistsAsync(name, ct))
            return Problems.Conflict(ProviderErrorCodes.NameAlreadyExists, $"A provider named '{name.Value}' already exists.");

        var provider = ProviderAggregate.Create(name, countryId.Value, description, supportEmail, isPrivate: request.IsPrivate);

        await repository.AddAsync(provider, ct);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProviderSelfCreated, caller) with
        {
            TargetType = AuditActions.TargetTypes.Provider,
            TargetId = name.Value,
        }, logger);

        return Results.CreatedAtRoute("GetProviderByNameV2", new { providerName = name.Value }, new Response(name.Value));
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

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapCreateProviderEndpoint()
        {
            app.MapPost("Providers", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.Admin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("CreateProviderV2")
            .WithSummary("Create a provider")
            .WithDescription("Creates a provider (Admin only). Body {Name, CountryCode, Description?, SupportEmail?, IsPrivate}. Returns 201; 400 on validation / unknown country; 403 for non-Admins; 409 when the name already exists.")
            .WithTags("Provider")
            .Accepts<Request>("application/json")
            .Produces<Response>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}