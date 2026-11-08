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
/// Assigns a project resource to a location (creates an <c>AssignedLocation</c> under the Provider
/// aggregate). Admin may assign under any provider; a provider service-key identity only under its own
/// (ADR-0011). Mirrors legacy <c>POST api/ProjectResourceAssignment</c>.
///
/// Read-only enforcement (ADR-0016): the location and assignment guards live in the aggregate; the
/// cross-aggregate <see cref="ProjectResource"/> read-only guard is enforced here from
/// <see cref="ResourceAssignmentTarget.Mode"/>, since the resource is a separate aggregate.
/// </summary>
internal static class AssignResourceEndpoint
{
    internal sealed class AuditCategory;

    internal sealed record SpecificationItem(string Name, string Value);

    internal sealed record Request(
        string ResourceName,
        EAllocationUnitType AllocationUnitType,
        long? AllocationUnitCount,
        string? Description,
        string? AggregationName,
        IReadOnlyList<SpecificationItem>? Specifications);

    internal sealed record Response(string Slug);

    internal static async Task<IResult> HandleAsync(
        [FromRoute] string providerName,
        [FromRoute] string locationName,
        [FromBody] Request request,
        Caller caller,
        [FromServices] IProviderRepository repository,
        [FromServices] IRepository<Core.Domain.Provider.ProjectResource, ProjectResourceKey> resourceRepository,
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
                AuditActions.ProviderResourceAssignmentCreated, caller,
                data: new Dictionary<string, object?>
                {
                    ["provider"] = providerName,
                    ["location"] = locationName,
                    ["resource"] = request.ResourceName,
                }) with
            {
                TargetType = AuditActions.TargetTypes.ResourceAssignment,
                Reason = AuditActions.DenialReasons.ProviderWriteForbidden,
            }, logger);
            return Problems.ProviderWriteForbidden();
        }

        if (ParseInput(providerName, locationName, request, out var name, out var location, out var resourceName) is { } invalidInput)
            return invalidInput;

        var provider = await repository.FindAsync(name, ct);
        if (provider is null)
            return Problems.NotFound("provider.not_found", "Provider not found.");

        // Load the resource ENTITY by its per-provider natural key (ADR-0044 L2.4). The repository resolves the
        // single active resource OWNED by this provider; a name under another provider (or a NULL-ProviderId
        // legacy row) never matches → 404. Invariant #5 is additionally re-checked in-aggregate by
        // provider.AssignResource (ADR-0044 L2.3).
        var resource = await resourceRepository.FindAsync(new ProjectResourceKey(name, resourceName), ct);
        if (resource is null)
            return Problems.NotFound("provider.resource_not_found", "Project resource not found.");

        if (await CheckAssignableAsync(resource, resourceName, request, assignmentQuery, ct) is { } notAssignable)
            return notAssignable;

        // Cross-aggregate invariant #1 (resource already holds a non-shared location) stays a bool query keyed
        // by the natural key; the aggregate consumes the resolved flag (ADR-0044 L2.5).
        var hasNonSharedLocation = await assignmentQuery.HasNonSharedLocationAsync(new ProjectResourceKey(name, resourceName), ct);

        var specifications = ToSpecifications(request.Specifications);

        // GUID-free public id for the allocation (ADR-0024), minted from the requested resource name.
        // ponytail: no regenerate-retry — r6 makes a global collision ~1/2e9; a hit cleanly rolls back
        // (one UnitOfWork) and the caller re-POSTs to a fresh slug. Add a retry only if ever observed.
        var slug = AllocationSlug.New(resourceName);

        var result = provider.AssignResource(
            location,
            resource,
            (int)request.AllocationUnitType,
            request.AllocationUnitCount,
            request.Description,
            request.AggregationName,
            specifications,
            hasNonSharedLocation,
            slug: slug);

        if (!result.IsSuccess)
            return result.Error.ToProblem();

        // provider is tracked Unchanged (it was loaded, not detached), so the new AssignedLocation that
        // AssignResource just added to one of its (also tracked) Location collections is discovered by
        // DetectChanges rather than attached fresh. AssignedLocation.Id is client-generated
        // (AssignedLocation.cs:80), so EF's NavigationFixer/AttachGraph heuristic treats the already-set key as
        // "existing" and marks the row Modified instead of Added — which SaveChanges then issues as an UPDATE
        // that matches no row. Tell the repository this row is actually new so it can correct its tracked
        // state before save; the repository walks the whole reachable graph (Add, not Entry(x).State=Added), so
        // the still-untracked AssignedLocationValue children get tracked and marked Added along with it.
        repository.MarkAssignedLocationAsAdded(result.Value);

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProviderResourceAssignmentCreated, caller,
            data: new Dictionary<string, object?>
            {
                ["provider"] = name.Value,
                ["location"] = location.Value,
                ["resource"] = resourceName.Value,
            }) with
        {
            TargetType = AuditActions.TargetTypes.ResourceAssignment,
            TargetId = slug,
        }, logger);

        // Location points at the created allocation's own canonical URL (matching the Update/Remove routes),
        // addressed by its GUID-free slug under the provider/location.
        return Results.Created(
            $"v2/Providers/{name.Value}/Locations/{location.Value}/Assignments/{slug}",
            new Response(slug));
    }

    private static IResult? ParseInput(
        string providerName,
        string locationName,
        Request request,
        out ProviderName name,
        out LocationName location,
        out ResourceName resourceName)
    {
        location = default;
        resourceName = default;

        if (!ProviderName.TryCreate(providerName, out name, out var providerNameError))
            return Problems.Validation(ProviderErrorCodes.InvalidName, providerNameError);

        if (!LocationName.TryCreate(locationName, out location, out var locationNameError))
            return Problems.Validation(ProviderErrorCodes.InvalidLocationName, locationNameError);

        if (!ResourceName.TryParse(request.ResourceName, null, out resourceName))
            return Problems.Validation(CommonErrorCodes.Required, "Resource name is required.");

        return null;
    }

    private static async Task<IResult?> CheckAssignableAsync(
        Core.Domain.Provider.ProjectResource resource,
        ResourceName resourceName,
        Request request,
        IResourceAssignmentQuery assignmentQuery,
        CancellationToken ct)
    {
        // Cross-aggregate read-only guard, read off the loaded entity (ADR-0044 L2.5).
        if (resource.OperationMode == EOperationMode.ReadOnly)
            return Problems.Unprocessable(ProviderErrorCodes.ProviderReadOnly, $"Project resource '{resourceName.Value}' is in read-only mode and cannot be modified.");

        if (!await assignmentQuery.AllocationUnitTypeExistsAsync((int)request.AllocationUnitType, ct))
            return Problems.Unprocessable("provider.allocation_unit_type_not_found", $"Allocation unit type '{request.AllocationUnitType}' does not exist.");

        return null;
    }

    private static IReadOnlyCollection<(string Name, string Value)> ToSpecifications(IReadOnlyList<SpecificationItem>? specifications) =>
        specifications is { Count: > 0 }
            ? [.. specifications.Select(s => (s.Name, s.Value))]
            : [];

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapAssignResourceEndpoint()
        {
            app.MapPost("Providers/{providerName}/Locations/{locationName}/Assignments", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.ProviderAdmin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("AssignResourceV2")
            .WithSummary("Assign a project resource to a location")
            .WithDescription("Assigns a project resource to a location under a provider (Admin or the owning provider identity). The resource is addressed by its name (ADR-0024). Body {ResourceName, AllocationUnitType, AllocationUnitCount?, Description?, AggregationName?, Specifications?}. Returns 201 with a Location header to the created allocation (addressed by its slug); 400 on input validation (including an allocation unit type name that is not in GET AllocationUnitTypes) or a domain invariant breach; 422 on an allocation unit type with no catalog entry (None) or a read-only resource; 403 when not allowed; 404 when the provider, location, or resource does not exist. Use the machine-readable `code`, not the message.")
            .WithTags("Provider")
            .Accepts<Request>("application/json")
            .Produces<Response>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }
}