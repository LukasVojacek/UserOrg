using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;

using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.Features.Resource.UpdateResource;

/// <summary>
/// Updates a resource's metadata under a provider (F4 Phase C; ADR-0026 / ADR-0030). Resolved by its full
/// natural key <c>(providerName, resourceName)</c> — the aggregate is loaded and mutated through
/// <see cref="ResourceAggregate.UpdateDetails"/>. Name and owning provider are identity and immutable (rename is
/// disabled, D2). A resource that is missing, soft-deleted, or under another provider resolves to the identical
/// 404 (C3). Admin may write any provider, a provider service-key identity only its own (ADR-0011).
/// </summary>
internal static class UpdateResourceEndpoint
{
    internal sealed class AuditCategory;

    internal sealed record Request(
        string? Description,
        string PrimaryInvestigatorEmail,
        DateTimeOffset StartDate,
        DateTimeOffset EndDate,
        string? LicenseUrl);

    internal static async Task<IResult> HandleAsync(
        [FromRoute] string providerName,
        [FromRoute] string resourceName,
        [FromBody] Request request,
        Caller caller,
        [FromServices] IRepository<ResourceAggregate, ProjectResourceKey> repository,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        CancellationToken ct)
    {
        if (!Core.Domain.Provider.ProviderName.TryCreate(providerName, out var provider, out var providerError))
            return Problems.Validation(ProviderErrorCodes.InvalidName, providerError);

        if (!Core.Domain.Provider.ResourceName.TryParse(resourceName, null, out var name))
            return Problems.Validation("resource.invalid_name", "Resource name is invalid.");

        if (ValidateRequest(request, out var email) is { } invalid)
            return invalid;

        if (!ProviderWriteAuthorization.Authorize(caller, provider, roleOptions.Value))
        {
            await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.ResourceSelfUpdated, caller, data: new Dictionary<string, object?> { ["provider"] = provider.Value }) with
            {
                TargetType = AuditActions.TargetTypes.Resource,
                TargetId = name.Value,
                Reason = AuditActions.DenialReasons.ProviderWriteForbidden,
            }, logger);
            return Problems.ProviderWriteForbidden();
        }

        // Load the aggregate by its natural key (ADR-0044 L2.4). Missing / wrong-provider / soft-deleted all
        // resolve to null → the same 404 (C3); the surrogate stays server-side inside the repository.
        var resource = await repository.FindAsync(new ProjectResourceKey(provider, name), ct);
        if (resource is null)
            return Problems.NotFound("resource.not_found", "Resource not found.");

        // operationMode / externalId are not part of the contract (mirrors create, C2) — preserve the loaded
        // values so a metadata update never wipes fields the client never sent.
        resource.UpdateDetails(
            request.StartDate,
            request.EndDate,
            request.Description,
            resource.OperationMode,
            email,
            resource.ExternalId,
            request.LicenseUrl);

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ResourceSelfUpdated, caller, data: new Dictionary<string, object?> { ["provider"] = provider.Value }) with
        {
            TargetType = AuditActions.TargetTypes.Resource,
            TargetId = name.Value,
        }, logger);

        return Results.NoContent();
    }

    private static IResult? ValidateRequest(Request request, out Email email)
    {
        email = default;

        if (request.Description is { Length: > 150 })
            return Problems.Validation(CommonErrorCodes.InvalidFormat, "Description must not exceed 150 characters.");

        if (string.IsNullOrWhiteSpace(request.PrimaryInvestigatorEmail)
            || request.PrimaryInvestigatorEmail.Length > 100
            || !Email.TryParse(request.PrimaryInvestigatorEmail, null, out email))
            return Problems.Validation(CommonErrorCodes.InvalidFormat, "A valid primary investigator email (max 100 characters) is required.");

        if (request.EndDate <= request.StartDate)
            return Problems.Validation(CommonErrorCodes.InvalidFormat, "EndDate must be after StartDate.");

        return null;
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapUpdateResourceEndpoint()
        {
            app.MapPut("Providers/{providerName}/Resources/{resourceName}", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.ProviderAdmin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("UpdateResourceV2")
            .WithSummary("Update a provider's resource")
            .WithDescription("Updates a resource's metadata under a provider (Admin or the owning provider identity). The name and owning provider are immutable (rename is disabled, ADR-0030) — body {Description?, PrimaryInvestigatorEmail, StartDate, EndDate}. Returns 204; 400 on validation; 403 when not allowed; 404 when the resource does not exist under this provider. Use the machine-readable `code`, not the message.")
            .WithTags("Resource")
            .Accepts<Request>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}