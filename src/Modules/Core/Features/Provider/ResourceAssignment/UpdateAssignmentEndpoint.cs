using Core.Domain.Shared;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace Core.Features.Provider.ResourceAssignment;

/// <summary>
/// Updates an assignment (details + replace-all specifications) on a location, addressed by its
/// GUID-free slug (ADR-0024). The allocated resource and location never change (legacy parity). Admin may update
/// under any provider; a provider service-key identity only under its own (ADR-0011). Mirrors legacy
/// <c>PUT api/ProjectResourceAssignment</c>.
///
/// Read-only enforcement (ADR-0016): the aggregate guards the assignment's own mode; this endpoint
/// additionally guards the cross-aggregate <see cref="ProjectResource"/> read-only mode (a deliberate
/// tightening over legacy, which checked it only on assign) — verified after the in-memory mutation and
/// before the commit, so a read-only resource leaves nothing persisted.
/// </summary>
internal static class UpdateAssignmentEndpoint
{
    internal sealed class AuditCategory;

    internal sealed record SpecificationItem(string Name, string Value);

    internal sealed record Request(
        EAllocationUnitType AllocationUnitType,
        long? AllocationUnitCount,
        string? Description,
        string? AggregationName,
        IReadOnlyList<SpecificationItem>? Specifications);

    internal static async Task<IResult> HandleAsync(
        [FromRoute] string providerName,
        [FromRoute] string locationName,
        [FromRoute] string slug,
        [FromBody] Request request,
        Caller caller,
        [FromServices] IProviderRepository repository,
        [FromServices] IResourceAssignmentQuery assignmentQuery,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        CancellationToken ct)
    {
        if (!ProviderWriteAuthorization.Authorize(caller, providerName, roleOptions.Value))
        {
            await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.ProviderResourceAssignmentUpdated, caller,
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

        if (ValidateAddress(locationName, slug, out var location) is { } invalid)
            return invalid;

        if (!await assignmentQuery.AllocationUnitTypeExistsAsync((int)request.AllocationUnitType, ct))
            return Problems.Unprocessable("provider.allocation_unit_type_not_found", $"Allocation unit type '{request.AllocationUnitType}' does not exist.");

        // Load with the allocations' resources materialised — the read-only guard and the change-event read
        // ResourceOperationMode/ResourceKey off the existing AssignedLocation's resource navigation (ADR-0044 L2.5).
        var provider = await repository.FindWithAllocatedResourcesAsync(name, ct);
        if (provider is null)
            return Problems.NotFound("provider.not_found", "Provider not found.");

        var specifications = ToSpecifications(request.Specifications);

        var result = provider.UpdateAssignment(
            location,
            slug,
            (int)request.AllocationUnitType,
            request.AllocationUnitCount,
            request.Description,
            request.AggregationName,
            specifications);

        if (!result.IsSuccess)
            return result.Error.ToProblem();

        // Cross-aggregate read-only guard, read off the allocated resource loaded with the aggregate (ADR-0044
        // L2.5). A soft-deleted/missing resource leaves the navigation null and never blocks (cleanup must stay
        // possible); only an existing, read-only resource does. Checked before the commit, so a rejected request
        // persists nothing.
        if (result.Value.ResourceOperationMode == EOperationMode.ReadOnly)
            return Problems.Unprocessable(ProviderErrorCodes.ProviderReadOnly, $"Project resource '{result.Value.ResourceKey?.Name.Value}' is in read-only mode and cannot be modified.");

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProviderResourceAssignmentUpdated, caller,
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

    private static IResult? ValidateAddress(string locationName, string slug, out LocationName location)
    {
        if (!LocationName.TryCreate(locationName, out location, out var locationNameError))
            return Problems.Validation(ProviderErrorCodes.InvalidLocationName, locationNameError);

        if (string.IsNullOrWhiteSpace(slug))
            return Problems.Validation(CommonErrorCodes.Required, "Assignment slug is required.");

        return null;
    }

    private static IReadOnlyCollection<(string Name, string Value)> ToSpecifications(IReadOnlyList<SpecificationItem>? specifications) =>
        specifications is { Count: > 0 }
            ? [.. specifications.Select(s => (s.Name, s.Value))]
            : [];

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapUpdateAssignmentEndpoint()
        {
            app.MapPut("Providers/{providerName}/Locations/{locationName}/Assignments/{slug}", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.ProviderAdmin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("UpdateAssignmentV2")
            .WithSummary("Update a resource assignment")
            .WithDescription("Updates an assignment's details and replaces its specifications (Admin or the owning provider identity). Body {AllocationUnitType, AllocationUnitCount?, Description?, AggregationName?, Specifications?}. Returns 204; 400 on input validation (including an allocation unit type name that is not in GET AllocationUnitTypes); 422 on an allocation unit type with no catalog entry (None) or a read-only resource; 403 when not allowed; 404 when the provider, location, or assignment does not exist; 409 when the assignment is already deleted. Use the machine-readable `code`, not the message.")
            .WithTags("Provider")
            .Accepts<Request>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }
}