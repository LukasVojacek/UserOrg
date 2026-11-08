using Core.Features.Resource.Workflow.Steps;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;

using static Core.Features.Resource.CreateResourceOnLocation.CreateResourceOnLocationEndpoint;

namespace Core.Features.Resource.Workflow.Handlers;

/// <summary>
/// Workflow step handler for <see cref="InitializeProjectResourceStep"/>. Creates and allocates the resource in
/// memory (ADR-0043) and persists it in a single transaction; throws RetryableException on any domain error,
/// which the engine catches and retries as part of the durable workflow.
/// </summary>
internal sealed class InitializeProjectResourceHandler : IStepHandler<InitializeProjectResourceStep>
{
    private readonly IResourceProvisioningService _provisioning;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRepository<ProjectResource, ProjectResourceKey> _resourceRepository;
    private readonly IProviderRepository _providerRepository;
    private readonly ILogger<InitializeProjectResourceHandler> _logger;

    public InitializeProjectResourceHandler(
       IResourceProvisioningService provisioning,
       IUnitOfWork unitOfWork,
       IRepository<ProjectResource, ProjectResourceKey> resourceRepository,
       IProviderRepository providerRepository,
       ILogger<InitializeProjectResourceHandler> logger)
    {
        _provisioning = provisioning;
        _unitOfWork = unitOfWork;
        _resourceRepository = resourceRepository;
        _providerRepository = providerRepository;
        _logger = logger;
    }

    public async Task<StepResult> HandleAsync(InitializeProjectResourceStep request, CancellationToken ct)
    {
        // T-055-04: the payload carries only the provider's natural key (ADR-0029), so the aggregate can only
        // come from THIS handler's own scope — the endpoint's scope, and the DbContext the endpoint loaded from,
        // are long gone by the time the worker runs. A provider that no longer exists by then is a permanent
        // failure, not something a retry can fix.
        var provider = await _providerRepository.FindAsync(request.ProviderName, ct)
            ?? throw new PermanentException("PROVIDER_NOT_FOUND", $"Provider {request.ProviderName} was not found.");

        // GUID-free public id for the allocation (ADR-0024), minted from the requested resource name.
        ResourceName resourceName = ResourceName.Create(request.Data.Name);
        Email piEmail = Email.Create(request.Data.PrimaryInvestigatorEmail);

        // T-055-02: RequestWorker records step completion through a SEPARATE WorkflowDbContext from the one
        // this handler commits with (RequestWorker.cs:157-172), so the two commits are not atomic. A crash
        // between them redelivers this step (ADR-0100 recovery class 1) and it runs again over a provider
        // that is already provisioned. The natural key (provider, resource name) is the same one the unique
        // index IX_ProjectResource_ProviderId_Name guards, so a hit here means "already done" — return
        // success without provisioning or saving again.
        var key = new ProjectResourceKey(provider.Name, resourceName);
        if (await _resourceRepository.FindAsync(key, ct) is not null)
        {
            return new StepResult(null);
        }

        var assignments = new List<LocationAssignmentSpec>(
            request.Data.AssignedLocations.Select(
                l => new LocationAssignmentSpec(
                    LocationName.Create(l.LocationName),
                    new ResourceAllocationSpec(
                        (int)l.AllocationUnitType,
                        l.AllocationUnitCount,
                        l.AllocationDescription,
                        l.AggregationName,
                        ToSpecifications(l.Specifications),
                        AllocationSlug.New(resourceName)
                    )
                )
            ));

        // Snapshot of the allocation ids the reloaded (tracked) provider already carries, taken before
        // provisioning mutates it, so the new AssignedLocation(s) provisioning creates can be told apart from
        // the ones that already existed.
        var existingAssignedLocationIds = provider.Locations
            .SelectMany(l => l.AssignedLocations)
            .Select(a => a.Id)
            .ToHashSet();

        var result = _provisioning.Provision(
            provider,
            new NewResourceSpec(resourceName, request.Data.StartDate, request.Data.EndDate, request.Data.Description, piEmail, request.Data.LicenseUrl),
            request.CreatedBy!.Value,
            assignments);
        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to provision resource {ResourceName}: {Error}", resourceName, result.Error);
            throw new RetryableException("FAILED_PROVISIONING", $"Failed to provision resource for provider {provider.Name}: {result.Error.Description}");
        }

        // Set the state before persisting.
        result.Value!.UpdateState(EProjectResourceState.InitializeProjectResourceStep);

        // provider is tracked Unchanged (it was loaded, not detached), so the new AssignedLocation(s) that
        // Provision just added to one of its (also tracked) Location collections are discovered by
        // DetectChanges rather than attached fresh. AssignedLocation.Id is client-generated
        // (AssignedLocation.cs:80), so EF's NavigationFixer/AttachGraph heuristic treats the already-set key as
        // "existing" and marks the row Modified instead of Added — which SaveChanges then issues as an UPDATE
        // that matches no row. Tell the repository which rows are actually new so it can correct their tracked
        // state before save; the repository walks the whole reachable graph (Add, not Entry(x).State=Added), so
        // the still-untracked AssignedLocationValue children get tracked and marked Added along with it.
        foreach (var assignedLocation in provider.Locations.SelectMany(l => l.AssignedLocations))
        {
            if (!existingAssignedLocationIds.Contains(assignedLocation.Id))
            {
                _providerRepository.MarkAssignedLocationAsAdded(assignedLocation);
            }
        }

        // One transaction: the new ProjectResource + the mutated provider's AssignedLocation (via the
        // _resource navigation) persist together or not at all.
        await _resourceRepository.AddAsync(result.Value, ct);

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // T-055-02: the pre-check above and this save are two separate round trips, so a competing
            // delivery can commit the same (provider, resource name) row in between — the pre-check saw "no
            // row" and this save still hits IX_ProjectResource_ProviderId_Name. Do not pattern-match the
            // exception: SQLite reports it via column names, SQL Server via the index name, and neither shape
            // is provider-neutral. Instead, re-read the same natural key: a hit means the redelivered write
            // is now redundant (idempotent success); anything else means the failure is unrelated to the
            // duplicate-delivery race and must propagate so it is not reported as a completed step.
            if (await _resourceRepository.FindAsync(key, ct) is not null)
            {
                return new StepResult(null);
            }

            throw;
        }

        return new StepResult(null);
    }

    private static IReadOnlyCollection<(string Name, string Value)> ToSpecifications(IReadOnlyList<SpecificationItem>? specifications) =>
       specifications is { Count: > 0 }
           ? [.. specifications.Select(s => (s.Name, s.Value))]
           : [];
}