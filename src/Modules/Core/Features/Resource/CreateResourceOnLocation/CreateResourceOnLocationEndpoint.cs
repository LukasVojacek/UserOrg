using Core.Features.Resource.Workflow.Steps;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared;
using UserOrg.Shared.Extensions;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Ports;

using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.Features.Resource.CreateResourceOnLocation;

/// <summary>
/// Creates a <see cref="ResourceAggregate"/> under a provider AND allocates it to one or more named locations in
/// one atomic transaction (ADR-0043, ADR-0050). Create+allocate is coordinated by the pure
/// <see cref="IResourceProvisioningService"/> domain service; this use-case resolves the provider (404), resolves
/// every named location (404), gates the whole request on every location's HEAppE instance being reachable (502),
/// then submits the async create-and-allocate workflow and returns 202 Accepted (ADR-0057). Takes over the route
/// the retired create-only <c>POST providers/{providerName}/resources</c> used — a resource with zero locations
/// has no use case. A name collision under the same provider returns 409 Conflict.
/// </summary>
internal static class CreateResourceOnLocationEndpoint
{
    internal sealed class AuditCategory;

    internal sealed record UserSshKey(string LocationName, string AggregationName, long ProjectId, string Username, string? Key);

    internal sealed record SpecificationItem(string Name, string Value);

    internal sealed record Cluster(
        string ClusterName,
        string ScratchPath,
        string ProjectPath);

    internal sealed record LocationAssignment(
        string LocationName,
        EAllocationUnitType AllocationUnitType,
        string? AggregationName,
        long? AllocationUnitCount,
        string? AllocationDescription,
        string PreferredAuthenticationType,
        IReadOnlyList<SpecificationItem>? Specifications
        );

    internal sealed record Request(
        string Name,
        string? Description,
        string PrimaryInvestigatorEmail,
        DateTimeOffset StartDate,
        DateTimeOffset EndDate,

        bool BypassHeappe,
        string? AccountString,
        EAllocationUnitType UnitType,
        bool? MappingOneToOne,
        bool? UseAccountingStringForScheduler,

        List<Cluster>? Clusters,

        List<string>? Usernames,
        string? LicenseUrl,

        List<LocationAssignment> AssignedLocations
       );

    internal sealed record Response(string Name, Guid RequestId);

    internal static async Task<IResult> HandleAsync(
        [FromRoute] string providerName,
        [FromBody] Request request,
        Caller caller,
        [FromServices] IProviderRepository providerRepository,
        [FromServices] IResourceQuery resources,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] IRequestApplicationService requestApplicationService,
        [FromServices] IUserDirectory userDirectory,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        CancellationToken ct)
    {
        if (!ProviderName.TryCreate(providerName, out var provider, out var providerNameError))
            return Problems.Validation(ProviderErrorCodes.InvalidName, providerNameError);

        if (!ResourceName.TryParse(request.Name, null, out var name))
            return Problems.Validation("resource.invalid_name", "Resource name is invalid.");

        if (ValidateBody(request, out var parsedLocations) is { } invalidBody)
            return invalidBody;

        if (!ProviderWriteAuthorization.Authorize(caller, provider, roleOptions.Value))
        {
            await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.ResourceCreationRequested, caller, data: new Dictionary<string, object?> { ["provider"] = provider.Value }) with
            {
                TargetType = AuditActions.TargetTypes.Resource,
                TargetId = name.Value,
                Reason = AuditActions.DenialReasons.ProviderWriteForbidden,
            }, logger);
            return Problems.ProviderWriteForbidden();
        }

        if (ValidateWorkflowInput(request) is { } invalidWorkflowInput)
            return invalidWorkflowInput;

        if (await userDirectory.FindUserIdAsync(caller.IdpId, ct) is not { } createdBy)
            return Problems.CallerNotProvisioned();

        return await ResolveAndSubmitAsync(provider, name, request, parsedLocations, providerRepository, resources,
            requestApplicationService, caller, createdBy, auditLogger, logger, ct);
    }

    private static IResult? ValidateBody(Request request, out List<(LocationName LocationName, LocationAssignment Item)> parsedLocations)
    {
        parsedLocations = [];

        if (ValidateScalars(request) is { } invalidScalar)
            return invalidScalar;

        // The real allocations the create-and-allocate workflow submits live in AssignedLocations — that is the
        // list the "at least one assignment" rule guards (the bug validated Clusters here instead, so a request
        // with clusters but zero assignments slipped through).
        if (request.AssignedLocations is not { Count: > 0 })
            return Problems.Validation("resource.locations_required", "At least one location assignment is required.");

        // Clusters is a distinct input (per-cluster scratch/project paths); validate its emptiness separately
        // with its own code rather than conflating it with the assignment rule.
        if (!request.BypassHeappe && request.Clusters is not { Count: > 0 })
            return Problems.Validation("resource.clusters_required", "At least one cluster is required.");

        return ParseAssignments(request.AssignedLocations, out parsedLocations);
    }

    private static IResult? ValidateScalars(Request request)
    {
        if (request.Description is { Length: > 150 })
            return Problems.Validation(CommonErrorCodes.InvalidFormat, "Description must not exceed 150 characters.");

        // Legacy parity (mirrors CreateResourceV2): the primary investigator email is required, a valid
        // address, max 100 chars.
        if (string.IsNullOrWhiteSpace(request.PrimaryInvestigatorEmail)
            || request.PrimaryInvestigatorEmail.Length > 100
            || !Email.TryParse(request.PrimaryInvestigatorEmail, null, out _))
            return Problems.Validation(CommonErrorCodes.InvalidFormat, "A valid primary investigator email (max 100 characters) is required.");

        if (request.EndDate <= request.StartDate)
            return Problems.Validation(CommonErrorCodes.InvalidFormat, "EndDate must be after StartDate.");

        return null;
    }

    public static IResult? ParseAssignments(
        List<LocationAssignment> assignments, out List<(LocationName LocationName, LocationAssignment Item)> parsedLocations)
    {
        parsedLocations = new List<(LocationName LocationName, LocationAssignment Item)>(assignments.Count);
        foreach (var item in assignments)
        {
            if (!LocationName.TryCreate(item.LocationName, out var locationName, out var locationNameError))
                return Problems.Validation(ProviderErrorCodes.InvalidLocationName, locationNameError);

            parsedLocations.Add((locationName, item));
        }

        // The same location may legitimately appear twice under different aggregations; the same location AND
        // the same aggregation twice is a malformed request, caught before any DB/domain call (ADR-0050 §3).
        var duplicate = parsedLocations
            .GroupBy(p => (p.LocationName, p.Item.AggregationName))
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            return Problems.Validation(
                "resource.duplicate_location_assignment",
                $"Location '{duplicate.Key.LocationName.Value}' is assigned twice with the same aggregation.");

        return null;
    }

    private static IResult? ValidateWorkflowInput(Request request)
    {
        if (request.BypassHeappe)
            return null;

        if (request.UnitType == EAllocationUnitType.None)
            return Problems.Validation(CommonErrorCodes.InvalidFormat, "AllocationUnitType is required.");

        foreach (var location in request.Clusters ?? Enumerable.Empty<Cluster>())
        {
            if (!LocationName.TryCreate(location.ClusterName, out _, out var clusterLocationError))
                return Problems.Validation(ProviderErrorCodes.InvalidLocationName, clusterLocationError);
            if (string.IsNullOrWhiteSpace(location.ScratchPath))
                return Problems.Validation(CommonErrorCodes.InvalidFormat, "ScratchPath is required.");
            if (string.IsNullOrWhiteSpace(location.ProjectPath))
                return Problems.Validation(CommonErrorCodes.InvalidFormat, "ProjectPath is required.");
        }

        if (request.Usernames is null || request.Usernames.Count == 0)
            return Problems.Validation(CommonErrorCodes.InvalidFormat, "Usernames are required.");

        foreach (var username in request.Usernames)
        {
            bool isLowerCase = string.Equals(username, username?.ToLowerInvariant(), StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(username) || !isLowerCase)
                return Problems.Validation(CommonErrorCodes.InvalidFormat, "Username is required and must be all lowercase.");
        }

        return null;
    }

    private static async Task<IResult> ResolveAndSubmitAsync(
        ProviderName provider,
        ResourceName name,
        Request request,
        List<(LocationName LocationName, LocationAssignment Item)> parsedLocations,
        IProviderRepository providerRepository,
        IResourceQuery resources,
        IRequestApplicationService requestApplicationService,
        Caller caller,
        UserId createdBy,
        IAuditLogger<AuditCategory> auditLogger,
        ILogger<AuditCategory> logger,
        CancellationToken ct)
    {
        // Resolve the owning provider — the provisioning service operates on the loaded aggregate (the surrogate
        // never surfaces). A missing/deactivated provider → 404 (existence never leaks, ADR-0018).
        var owningProvider = await providerRepository.FindAsync(provider, ct);
        if (owningProvider is null)
            return Problems.NotFound("provider.not_found", "Provider not found.");

        // Per-provider uniqueness pre-check. The filtered unique index is the concurrency backstop.
        if (await resources.IsResourceNameTakenAsync(provider, name, ct))
            return Problems.Conflict("resource.name_already_exists", $"A resource named '{name.Value}' already exists for this provider.");

        if (ResolveLocations(owningProvider, provider, request, parsedLocations, out var resolvedLocations) is { } missingLocation)
            return missingLocation;

        StepRequest initialStep = request.BypassHeappe
            ? new InitializeProjectResourceStep(
                Guid.CreateVersion7(),
                owningProvider.Name,
                request,
                resolvedLocations,
                createdBy)
            : new CheckHeappeStep(
                Guid.CreateVersion7(),
                owningProvider.Name,
                request,
                resolvedLocations,
                createdBy);
        var response = await requestApplicationService.SubmitAsync(initialStep,
            ct: ct);

        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ResourceCreationRequested, caller,
            data: new Dictionary<string, object?>
            {
                ["provider"] = provider.Value,
                ["request_id"] = response.RequestId,
            }) with
        {
            TargetType = AuditActions.TargetTypes.Resource,
            TargetId = name.Value,
        }, logger);

        // Async submit (ADR-0057): SubmitAsync returns as soon as the request is persisted and enqueued —
        // it never yields a terminal status synchronously. The contract is 202 Accepted whose Location points
        // at the status poll route (not the resource, which does not exist yet), carrying the flow RequestId.
        return TypedResults.AcceptedAtRoute(
            new Response(name.Value, response.RequestId),
            "GetResourceRequestStatusV2",
            new { requestId = response.RequestId });
    }

    internal static IResult? ResolveLocations(
        Core.Domain.Provider.Provider owningProvider,
        ProviderName provider,
        Request request,
        List<(LocationName LocationName, LocationAssignment Item)> parsedLocations,
        out List<ResolvedLocationRef> resolvedLocations)
    {
        // Resolve every requested location up front — first missing name fails fast with a 404 naming it
        // specifically. LocationNames are chosen from an existing listing endpoint, so a miss here is a rare
        // caller mistake, not something worth batching into one report (ADR-0050 §4).
        //
        // T-055-04: what leaves this scope is a (LocationName, Endpoint) ref, never the Location entity or the
        // provider aggregate that owns it — owningProvider was resolved for the 404/409 checks above and stays
        // behind with the HTTP request's DbContext (ADR-0029). One ref PER ASSIGNMENT, not per distinct
        // location: the same location may legitimately be assigned twice under different aggregations
        // (ADR-0050 §3) and the liveness gate reports per resolved location.
        resolvedLocations = new List<ResolvedLocationRef>(parsedLocations.Count);
        foreach (var (locationName, _) in parsedLocations)
        {
            var location = owningProvider.Locations.FirstOrDefault(l => !l.IsDeleted && l.Name.Equals(locationName));
            if (location is null)
                return Problems.NotFound(
                    ProviderErrorCodes.LocationNotFound,
                    $"Location '{locationName.Value}' not found for provider '{provider.Value}'.");

            var heappeUrl = FirstSpecification(request, locationName, ApplicationConstants.HeappeUriKey);
            var heappeIdentifier = FirstSpecification(request, locationName, ApplicationConstants.HeappeIdentifierKey);

            resolvedLocations.Add(new ResolvedLocationRef(location.Name.Value, location.LocationType, location.Endpoint, heappeUrl, heappeIdentifier));
        }

        return null;
    }

    private static string? FirstSpecification(Request request, LocationName locationName, string specificationName) => request.AssignedLocations
        .Where(a => string.Equals(a.LocationName, locationName.Value, StringComparison.OrdinalIgnoreCase))
        .Select(a => a.Specifications?.FirstOrDefault(f => f.Name == specificationName)?.Value)
        .FirstOrDefault();

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapCreateResourceOnLocationEndpoint()
        {
            app.MapPost("Providers/{providerName}/Resources", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("CreateResourceV2")
            .WithSummary("Create a provider's resource and allocate it to one or more locations")
            .WithDescription("Creates a resource under a provider and allocates it to every named location in one atomic transaction (ADR-0043/ADR-0050; Admin or the owning provider identity). The resource belongs to that provider and its name is unique per provider (ADR-0026). Body {Name, Description?, PrimaryInvestigatorEmail, StartDate, EndDate, Locations: [{LocationName, AllocationUnitTypeId, AllocationUnitCount?, AllocationDescription?, AggregationName?, Specifications?}]} — at least one location is required. All-or-nothing: any failing location aborts the whole request. Every named location's HEAppE instance is health-probed first; if any is unreachable, none of them are reported individually — the whole set of unreachable ones is returned together. Submits the create-and-allocate workflow asynchronously (ADR-0057) and returns 202 Accepted with a Location header to the request-status poll route and the flow RequestId in the body; 400 on input validation (incl. duplicate location+aggregation pairs) or a domain invariant breach (incl. requesting a second non-shared location for the same resource); 403 when not allowed; 404 when the provider or a named location does not exist; 409 when the name already exists for the provider; 422 on an unknown allocation unit type; 502 when one or more named locations' HEAppE instance is unreachable. Use the machine-readable `code`, not the message.")
            .WithTags("Resource")
            .WithGroupName(Core.Composition.ApiAudience.ProviderAdmin)
            .Accepts<Request>("application/json")
            .Produces<Response>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway);
    }
}