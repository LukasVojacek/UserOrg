using Core.Domain.Shared;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;

namespace Core.Features.Provider.ResourceAssignment;

/// <summary>
/// Soft-deletes an assignment on a location, addressed by its GUID-free slug (ADR-0024). Admin may remove
/// under any provider; a provider service-key identity only under its own (ADR-0011). Mirrors legacy
/// <c>DELETE api/ProjectResourceAssignment/{id}</c>.
///
/// Read-only enforcement (ADR-0016): the aggregate guards the assignment's own mode; this endpoint
/// additionally guards the cross-aggregate <see cref="ProjectResource"/> read-only mode (a deliberate
/// tightening over legacy) — verified after the in-memory mutation and before the commit. A
/// soft-deleted/missing resource never blocks, so a dangling assignment can always be cleaned up.
/// </summary>
internal static class RemoveAssignmentEndpoint
{
    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [FromRoute] string providerName,
        [FromRoute] string locationName,
        [FromRoute] string slug,
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
                AuditActions.ProviderResourceAssignmentRemoved, caller,
                data: new Dictionary<string, object?>
                {
                    ["provider"] = providerName,
                    ["location"] = locationName,
                }) with
            {
                TargetType = AuditActions.TargetTypes.ResourceAssignment,
                TargetId = slug,
                Reason = AuditActions.DenialReasons.ProviderWriteForbidden,
            }, logger);
            return Problems.ProviderWriteForbidden();
        }

        if (!ProviderName.TryCreate(providerName, out var name, out var providerNameError))
            return Problems.Validation(ProviderErrorCodes.InvalidName, providerNameError);

        if (!LocationName.TryCreate(locationName, out var location, out var locationNameError))
            return Problems.Validation(ProviderErrorCodes.InvalidLocationName, locationNameError);

        if (string.IsNullOrWhiteSpace(slug))
            return Problems.Validation(CommonErrorCodes.Required, "Assignment slug is required.");

        // Load with the allocations' resources materialised — the read-only guard and the change-event read
        // ResourceOperationMode/ResourceKey off the existing AssignedLocation's resource navigation (ADR-0044 L2.5).
        var provider = await repository.FindWithAllocatedResourcesAsync(name, ct);
        if (provider is null)
            return Problems.NotFound("provider.not_found", "Provider not found.");

        var result = provider.RemoveAssignment(location, slug);
        if (!result.IsSuccess)
            return result.Error.ToProblem();

        // Cross-aggregate read-only guard, read off the allocated resource loaded with the aggregate (ADR-0044
        // L2.5). A soft-deleted/missing resource leaves the navigation null and never blocks (a dangling
        // assignment can always be cleaned up); only an existing, read-only resource does. Checked before the
        // commit, so a rejected request persists nothing.
        if (result.Value.ResourceOperationMode == EOperationMode.ReadOnly)
            return Problems.Unprocessable(ProviderErrorCodes.ProviderReadOnly, $"Project resource '{result.Value.ResourceKey?.Name.Value}' is in read-only mode and cannot be modified.");

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProviderResourceAssignmentRemoved, caller,
            data: new Dictionary<string, object?>
            {
                ["provider"] = name.Value,
                ["location"] = location.Value,
            }) with
        {
            TargetType = AuditActions.TargetTypes.ResourceAssignment,
            TargetId = slug,
        }, logger);
        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapRemoveAssignmentEndpoint()
        {
            app.MapDelete("Providers/{providerName}/Locations/{locationName}/Assignments/{slug}", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.ProviderAdmin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("RemoveAssignmentV2")
            .WithSummary("Remove a resource assignment")
            .WithDescription("Soft-deletes an assignment (Admin or the owning provider identity). Returns 204; 422 when the resource is read-only; 403 when not allowed; 404 when the provider, location, or assignment does not exist; 409 when the assignment is already deleted. Use the machine-readable `code`, not the message.")
            .WithTags("Provider")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }
}