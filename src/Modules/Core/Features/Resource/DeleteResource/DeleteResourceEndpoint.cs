using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;

using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.Features.Resource.DeleteResource;

/// <summary>
/// Soft-deletes a resource under a provider (F4 Phase C; ADR-0026 / ADR-0030). Resolved by its full natural key
/// <c>(providerName, resourceName)</c> — the aggregate is loaded and deleted through
/// <see cref="ResourceAggregate.Delete"/> (sets <c>IsDeleted</c>). A resource that is missing, already deleted,
/// or under another provider resolves to the identical 404 (C3). Admin may write any provider, a provider
/// service-key identity only its own (ADR-0011).
/// </summary>
internal static class DeleteResourceEndpoint
{
    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [FromRoute] string providerName,
        [FromRoute] string resourceName,
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

        if (!ProviderWriteAuthorization.Authorize(caller, provider, roleOptions.Value))
        {
            await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.ResourceSelfDeleted, caller, data: new Dictionary<string, object?> { ["provider"] = provider.Value }) with
            {
                TargetType = AuditActions.TargetTypes.Resource,
                TargetId = name.Value,
                Reason = AuditActions.DenialReasons.ProviderWriteForbidden,
            }, logger);
            return Problems.ProviderWriteForbidden();
        }

        // Load the aggregate by its natural key (ADR-0044 L2.4). Missing / wrong-provider / already-deleted all
        // resolve to null → the same 404 (C3); the surrogate stays server-side inside the repository.
        var resource = await repository.FindAsync(new ProjectResourceKey(provider, name), ct);
        if (resource is null)
            return Problems.NotFound("resource.not_found", "Resource not found.");

        resource.Delete();
        resource.UpdateState(EProjectResourceState.Deleted);

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ResourceSelfDeleted, caller, data: new Dictionary<string, object?> { ["provider"] = provider.Value }) with
        {
            TargetType = AuditActions.TargetTypes.Resource,
            TargetId = name.Value,
        }, logger);

        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapDeleteResourceEndpoint()
        {
            app.MapDelete("Providers/{providerName}/Resources/{resourceName}", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.ProviderAdmin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("DeleteResourceV2")
            .WithSummary("Delete a provider's resource")
            .WithDescription("Soft-deletes a resource under a provider (Admin or the owning provider identity). Returns 204; 403 when not allowed; 404 when the resource does not exist under this provider. Use the machine-readable `code`, not the message.")
            .WithTags("Resource")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}