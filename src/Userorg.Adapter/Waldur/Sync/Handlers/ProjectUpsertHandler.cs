using Heappe.Contracts;
using Heappe.Contracts.Connection;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Userorg.Adapter.Waldur.Base;
using Userorg.Adapter.Waldur.Base.Heappe;
using Userorg.Adapter.Waldur.Base.Orchestrators.Extensions;
using Userorg.Adapter.Waldur.Base.Sync;

using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ValueObjects;

using Waldur.Contracts.Events;
using Waldur.Contracts.Payloads;
using Waldur.Sync.Worker;

namespace Userorg.Adapter.Waldur.Sync.Handlers;

/// <summary>
/// Consumer-side upsert for a Waldur project and its embedded marketplace resources (ADR-0057,
/// plan §4.3). Both <see cref="ProjectCreated"/> and <see cref="ProjectUpdated"/> route to the SAME
/// <see cref="UpsertAsync"/> — the Created/Updated distinction is not load-bearing (ADR-0057/A1),
/// which is essential with a lossy in-memory SHA map (a wrong first-run guess against a populated
/// DB must not change behaviour).
/// </summary>
/// <remarks>
/// Ports the behaviour of the old <c>WaldurExportOrchestrator.EnsureProjectResourcesAsync</c> +
/// <c>WaldurEfpExporter.ExportData(ProjectDto)</c> path, without the per-resource manual
/// transactions:
/// <list type="bullet">
/// <item>project upsert keyed <c>(ExternalId, ServiceName)</c> with <c>SyncFrom</c> merge;
/// <see cref="ProjectPayload.IsRemoved"/> → soft delete, memberships left in place;</item>
/// <item>resource upsert keyed <c>(Name, ServiceName)</c> SCOPED to this project (via the
/// <see cref="AssignedProjectResourceToProject"/> join — review B07/2a: a fallback must never match
/// a resource owned by a different project), and assigned-location upsert keyed
/// <c>(ExternalId = resource uuid, ServiceName)</c> SCOPED to the resource;</item>
/// <item>Heappe project ensure per HPC resource via <see cref="IProjectCreationService"/> on BOTH
/// events (ADR-0057/A1) — a missing Heappe instance (no uri/identifier) for an HPC resource throws
/// (retriable), preserving today's rollback-on-enrichment-failure;</item>
/// <item>command-template sync trigger per HPC resource via
/// <see cref="IHeappeSynchronizationService"/> (ADR-0057/A2).</item>
/// </list>
/// Order (ADR-0057/A3): mutate the tracked graph → run the external Heappe ensures/triggers → ONE
/// terminal <see cref="IUserOrgDbContext.SaveChangesAsync"/>. A Heappe throw therefore commits
/// nothing (the DB write has not happened yet), preserving "resource row committed ⇒ Heappe ensure
/// succeeded" without a manual transaction. Storage association (EfpCore rows) is NOT done here —
/// it is deferred to B10's <c>StorageConfigurationChanged</c> reconcile (ADR-0061, sole owner).
/// The service account is LOOKED UP (never ensured — B06/F1); per-instance config that the payload
/// cannot carry (domain-type name, Heappe auth type / PI email) comes from
/// <see cref="IWaldurConsumerOptions"/>.
/// </remarks>
internal sealed class ProjectUpsertHandler(
    IUserOrgDbContext context,
    IWaldurConsumerOptions consumerOptions,
    IProjectCreationService heappeProjectCreationService,
    IHeappeService heappeService,
    IHeappeConnectionFactory heappeConnectionFactory,
    IHeappeSynchronizationService heappeSynchronizationService,
    ILogger<ProjectUpsertHandler> logger)
    : IWaldurEventHandler<ProjectCreated>, IWaldurEventHandler<ProjectUpdated>
{
    public Task<Result<Error>> HandleAsync(ProjectCreated evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return UpsertAsync(evt.Payload, evt.Instance, evt.ServiceName, ct);
    }

    public Task<Result<Error>> HandleAsync(ProjectUpdated evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return UpsertAsync(evt.Payload, evt.Instance, evt.ServiceName, ct);
    }

    private async Task<Result<Error>> UpsertAsync(ProjectPayload payload, string integrationName, string serviceName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var options = consumerOptions.For(serviceName);

        var accountResult = await context.RequireServiceAccountAsync(integrationName, serviceName, ct);
        if (!accountResult.IsSuccess)
        {
            return Result<Error>.Failure(accountResult.Error);
        }

        var waldurUser = accountResult.Value;
        var resourceFailures = new List<Error>();

        var project = MapProject(payload, serviceName, options.ServiceProjectDomainName);
        var dbProject = await context.UpsertProjectAsync(logger, integrationName, project, waldurUser, ct);

        // A removed project is being torn down: soft-delete it (SyncFrom set IsDeleted above), leave
        // memberships in place (ADR-0054/A1), and do NOT re-process its resources. Re-ensuring HEAppE
        // projects + firing command-template syncs for a project being removed is wrong churn — the
        // payload may still carry the resources Waldur returned before removal (review B08 MEDIUM).
        if (!payload.IsRemoved)
        {

            foreach (var resourcePayload in payload.Resources)
            {
                if (string.IsNullOrEmpty(resourcePayload.Name))
                {
                    continue;
                }

                var resourceResult = await UpsertResourceAsync(resourcePayload, dbProject, options, integrationName, serviceName, ct);
                if (!resourceResult.IsSuccess)
                {
                    logger.ResourceSkipped(integrationName, payload.ShortName, resourcePayload.Name, resourceResult.Error.Description);
                    resourceFailures.Add(resourceResult.Error);
                }
            }
        }

        await context.SaveChangesAsync(ct);
        return resourceFailures.Count == 0
            ? Result<Error>.Success()
            : Result<Error>.Failure(Base.Errors.ProjectPartiallyImported(payload.ShortName, resourceFailures));
    }

    private async Task<Result<Error>> UpsertResourceAsync(
        ResourcePayload payload,
        Project dbProject,
        WaldurConsumerInstanceOptions options,
        string integrationName,
        string serviceName,
        CancellationToken ct)
    {
        // ADR-0066: a child span per resource, nesting under the worker's ambient project span via the
        // in-process Activity.Current, so consumer-side resource work is correlated by the same TraceId.
        using var span = WaldurSyncDiagnostics.StartEntitySpan(
            WaldurSyncDiagnostics.ResourceActivityName, integrationName, WaldurSyncDiagnostics.ResourceEntityType,
            payload.ResourceExternalId ?? string.Empty);

        var dbResource = await UpsertProjectResourceScopedAsync(payload, dbProject, serviceName, ct);
        await context.EnsureProjectResourceAssignmentAsync(dbProject, dbResource, ct);

        var locationResult = await context.RequireLocationByEndpointAsync(
            logger, integrationName, payload.Endpoint, serviceName, dbProject.ShortName.ToString(), ct);
        if (!locationResult.IsSuccess)
        {
            return Result<Error>.Failure(locationResult.Error);
        }

        var dbLocation = locationResult.Value;

        var allocationTypeResult = await context.RequireAllocationUnitTypeAsync(
            logger, integrationName, payload.AllocationUnitType, dbProject.ShortName.ToString(), ct);
        if (!allocationTypeResult.IsSuccess)
        {
            return Result<Error>.Failure(allocationTypeResult.Error);
        }

        //TODO if HEAppE side failed -> store state in database.
        var dbAllocationType = allocationTypeResult.Value;
        var dbAssignedLocation = await UpsertAssignedLocationScopedAsync(payload, dbResource, dbLocation, dbAllocationType, serviceName, ct);

        // HPC-ness comes from the resolved location's type (the provider handler set it, B07). Only
        // HPC resources touch Heappe; non-HPC resources carry no uri/identifier and are skipped.
        if (dbLocation.LocationTypeId != (int)ELocationType.HPC)
        {
            return Result<Error>.Success();
        }

        if (string.IsNullOrEmpty(payload.HeappeUri) || string.IsNullOrEmpty(payload.HeappeIdentifier))
        {
            // Today: an HPC resource whose provider lacks a HeappeInstancesMap entry fails and is
            // never persisted (ADR-0057/A1). An expected dependency-missing failure (ADR-0065): return
            // Result.Failure so nothing is acked and the event replays.
            return Result<Error>.Failure(Base.Errors.EnsureProjectFailed(
                dbProject.ShortName.ToString(),
                $"missing Heappe instance (uri/identifier) for HPC resource '{payload.Name}'."));
        }

        var providerId = dbLocation.Provider?.ExternalId ?? string.Empty;
        var heappeResult = await WaldurHeappeProjectInitializer.InitializeIfNeededAsync(
            dbAssignedLocation, providerId, options.WaldurPreferredAuthType, options.WaldurPiEmail, heappeProjectCreationService, heappeService, heappeConnectionFactory, ct);
        if (!heappeResult.IsSuccess)
        {
            return Result<Error>.Failure(heappeResult.Error);
        }

        // Command-template sync trigger (ADR-0057/A2) — replaces the old orchestrator's
        // WorkItem(projectShortName, resourceName) enqueue onto the Heappe background-sync channel.
        await heappeSynchronizationService.TriggerSynchronizationForProjectResource(
            dbProject.ShortName, dbResource.Name!, ct);

        return Result<Error>.Success();
    }

    /// <summary>
    /// Upserts the <see cref="ProjectResource"/> keyed <c>(Name, ServiceName)</c> SCOPED to
    /// <paramref name="dbProject"/> via the join table (review B07/2a): a resource with the same name
    /// owned by a different project must not be matched-and-mutated here.
    /// </summary>
    private async Task<ProjectResource> UpsertProjectResourceScopedAsync(
        ResourcePayload payload,
        Project dbProject,
        string serviceName,
        CancellationToken ct)
    {
        var existing = await context.AssignedProjectResourceToProjectsQuery
            .Where(apr => apr.ProjectId == dbProject.Id
                && apr.ProjectResource!.Name == payload.Name
                && apr.ProjectResource.ServiceName == serviceName)
            .Select(apr => apr.ProjectResource!)
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            var toInsert = MapResource(payload, serviceName);
            context.Add(toInsert);
            return toInsert;
        }

        existing.SyncFrom(MapResource(payload, serviceName));
        return existing;
    }

    /// <summary>
    /// Upserts the <see cref="AssignedLocation"/> keyed <c>(ExternalId = resource uuid, ServiceName)</c>
    /// SCOPED to <paramref name="dbResource"/> (review B07/2a). Reconciliation-owned rows
    /// (<c>EfpCoreServiceName</c>) are a different service name and are never matched here.
    /// </summary>
    private async Task<AssignedLocation> UpsertAssignedLocationScopedAsync(
        ResourcePayload payload,
        ProjectResource dbResource,
        Location dbLocation,
        AllocationUnitType dbAllocationType,
        string serviceName,
        CancellationToken ct)
    {
        var source = MapAssignedLocation(payload, serviceName);

        AssignedLocation? existing = null;
        if (!string.IsNullOrEmpty(source.ExternalId))
        {
            existing = await context.AssignedLocationsQuery
                .Include(al => al.AssignedLocationsValues)
                .FirstOrDefaultAsync(
                    al => al.ProjectResourceId == dbResource.Id
                        && al.ExternalId == source.ExternalId
                        && al.ServiceName == serviceName,
                    ct);
        }

        if (existing is null)
        {
            var toInsert = new AssignedLocation
            {
                Id = source.Id,
                ProjectResourceId = dbResource.Id,
                ProjectResource = dbResource,
                LocationId = dbLocation.Id,
                Location = dbLocation,
                AllocationUnitTypeId = dbAllocationType.Id,
                AllocationUnitType = dbAllocationType,
                AllocationUnitCount = source.AllocationUnitCount,
                Description = source.Description,
                AggregationName = source.AggregationName,
                CreatedAt = source.CreatedAt,
                ModifiedAt = source.ModifiedAt,
                IsDeleted = source.IsDeleted,
                Mode = source.Mode,
                ServiceName = source.ServiceName,
                ExternalId = source.ExternalId,
            };

            foreach (var value in source.AssignedLocationsValues)
            {
                value.AssignedLocation = toInsert;
                toInsert.AssignedLocationsValues.Add(value);
            }

            context.Add(toInsert);
            return toInsert;
        }

        existing.SyncFrom(source, dbAllocationType, out var newValues, out var removedValues);
        existing.LocationId = dbLocation.Id;
        existing.Location = dbLocation;
        existing.ProjectResourceId = dbResource.Id;
        existing.ProjectResource = dbResource;
        existing.AllocationUnitTypeId = dbAllocationType.Id;
        existing.AllocationUnitType = dbAllocationType;

        // Add newly-seen values to the in-memory collection too, so the Heappe initializer sees the
        // complete graph (e.g. a first-time HEAPPE_URL/IDENTIFIER value) before the terminal save.
        foreach (var value in newValues)
        {
            existing.AssignedLocationsValues.Add(value);
        }

        context.RemoveRange(removedValues);
        return existing;
    }

    private static Project MapProject(ProjectPayload payload, string serviceName, string domainTypeName) =>
        new()
        {
            Id = ProjectId.Create(Guid.CreateVersion7()),
            Name = payload.Name,
            ShortName = ProjectShortName.Create(payload.ShortName),
            Description = payload.Description,
            StartDate = payload.StartDate,
            EndDate = payload.EndDate,
            IsDeleted = payload.IsRemoved,
            Mode = EOperationMode.ReadOnly,
            ProjectDomainType = new ProjectDomainType { Name = domainTypeName },
            ServiceName = serviceName,
            ExternalId = payload.ExternalId,
            CreatedAt = payload.CreatedAt ?? DateTime.UtcNow,
        };

    private static ProjectResource MapResource(ResourcePayload payload, string serviceName) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Name = payload.Name,
            Description = payload.Description,
            StartDate = payload.StartDate,
            EndDate = payload.EndDate,
            IsDeleted = payload.IsDeleted,
            Mode = EOperationMode.ReadOnly,
            ServiceName = serviceName,
            ExternalId = null,
            CreatedAt = payload.CreatedAt ?? DateTime.UtcNow,
            ModifiedAt = payload.ModifiedAt,
        };

    private static AssignedLocation MapAssignedLocation(ResourcePayload payload, string serviceName)
    {
        var values = payload.Values
            .Select(v => new AssignedLocationValue { Id = Guid.CreateVersion7(), Name = v.Name, Value = v.Value })
            .ToList();

        if (!string.IsNullOrEmpty(payload.HeappeIdentifier))
        {
            values.Add(new AssignedLocationValue { Id = Guid.CreateVersion7(), Name = ApplicationConstants.HeappeIdentifierKey, Value = payload.HeappeIdentifier });
        }

        if (!string.IsNullOrEmpty(payload.HeappeUri))
        {
            values.Add(new AssignedLocationValue { Id = Guid.CreateVersion7(), Name = ApplicationConstants.HeappeUriKey, Value = payload.HeappeUri });
        }

        var assignedLocation = new AssignedLocation
        {
            Id = Guid.CreateVersion7(),
            AggregationName = payload.AggregationName,
            AllocationUnitCount = payload.AllocationUnitCount,
            Description = null,
            CreatedAt = payload.CreatedAt ?? DateTime.UtcNow,
            ModifiedAt = payload.ModifiedAt,
            IsDeleted = payload.IsDeleted,
            Mode = EOperationMode.ReadOnly,
            ServiceName = serviceName,
            ExternalId = payload.ResourceExternalId,
        };

        foreach (var value in values)
        {
            assignedLocation.AssignedLocationsValues.Add(value);
        }

        return assignedLocation;
    }
}