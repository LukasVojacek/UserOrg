using Heappe.Contracts.Entries;
using Heappe.Features.CommandTemplateManagement.GetAll;
using Heappe.Features.CommandTemplateManagement.GetAll.Response;
using Heappe.Features.CommandTemplateManagement.Request;
using Heappe.Features.KillSwitch;
using Heappe.HostContracts;
using Heappe.Persistence;
using Heappe.Persistence.Entities;
using Heappe.Persistence.Enums;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

using UserOrg.Shared;
using UserOrg.Shared.Errors;

namespace Heappe.Features.CommandTemplateManagement;

/// <summary>
/// Provides operations for managing command templates associated with project resource allocations, including retrieval
/// and activation state updates.
/// </summary>
/// <param name="dbContext">The database context used to access and persist command template and project resource allocation data. Cannot be
/// null.</param>
/// <param name="userService">The user organization service used to retrieve project resource allocation information. Cannot be null.</param>
/// <param name="killSwitchStateService">Resolves which of a template's partitions are killed by an active,
/// non-whitelisted location kill-switch (ADR-0077 §2/§3).</param>
/// <param name="writeGuard">Rejects Activate/Deactivate against a frozen target Location with 409 before any state is
/// mutated (ADR-0077 §5).</param>
internal class CommandTemplateManagementService(
    HeappeIntegrationDbContext dbContext,
    IUserorgService userService,
    HybridCache cache,
    IKillSwitchStateService killSwitchStateService,
    CommandTemplateWriteGuard writeGuard)
{
    /// <summary>
    /// Retrieves the list of command templates associated with a specific project resource allocation asynchronously.
    /// </summary>
    /// <param name="request">The query containing the project short name and resource name used to identify the project resource allocation.
    /// Cannot be null.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A result containing a list of command template responses if the project resource allocation is found; otherwise,
    /// a result containing an error describing the failure.</returns>
    internal async Task<Result<IList<CommandTemplateResponse>, Error>> GetCommandTemplatesForProjectAsync(GetCommandTemplateQuery request, CancellationToken ct)
    {
        if (request is null)
        {
            return Result<IList<CommandTemplateResponse>, Error>.Failure(CommonErrors.InvalidArgument(nameof(request)));
        }

        var projectAllocation = await userService.GetProjectResourceAllocationAsync(request.projectShortName, request.resourceName, ct);

        if (!projectAllocation.IsSuccess)
        {
            return Result<IList<CommandTemplateResponse>, Error>.Failure(projectAllocation.Error, projectAllocation.State);
        }
        var isExpired = CheckAllocationExpiration(projectAllocation.Value);

        var templates = await dbContext.Set<ProjectCommandTemplate>().AsNoTracking()
            .Include(i => i.ProjectCommandTemplatePartitions)
                .ThenInclude(p => p.LocationPartition)
            .Where(w => w.ProjectId == projectAllocation.Value.Id
                && w.ProjectResourceId == projectAllocation.Value.Resource.Id)
            .ToListAsync(ct);

        var partitionKeys = templates
            .SelectMany(t => t.ProjectCommandTemplatePartitions.Select(p => new PartitionKillKey(p.LocationPartition.LocationId, t.Id)))
            .Distinct()
            .ToList();
        var killedPartitionKeys = await killSwitchStateService.GetKilledPartitionsAsync(partitionKeys, ct);

        var data = templates.Select(t =>
        {
            var killedPartitionIds = t.ProjectCommandTemplatePartitions
                .Where(p => killedPartitionKeys.Contains(new PartitionKillKey(p.LocationPartition.LocationId, t.Id)))
                .Select(p => p.Id)
                .ToHashSet();

            return new CommandTemplateResponse
            (
                t.Id,
                t.Name,
                t.Type.ToResponseType(),
                t.GetCommandTemplateState(killedPartitionIds).ToResponseType(isExpired),
                Frozen: killedPartitionIds.Count > 0
            );
        }).ToList();

        return data is not null
            ? Result<IList<CommandTemplateResponse>, Error>.Success(data, State.OkWithContent)
            : Result<IList<CommandTemplateResponse>, Error>.Failure(Errors.FailedToRetrieveCommandTemplatesForProject(request.projectShortName, request.resourceName), State.NotFound);
    }

    /// <summary>
    /// Updates the activation state of a project command template and its partitions based on the specified request.
    /// </summary>
    /// <param name="request">The request containing the identifiers for the project, resource, and command template to update.</param>
    /// <param name="setActive">true to activate the command template; false to deactivate it. The default is true.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A result indicating success or failure. On success, returns a successful result; on failure, returns an error
    /// describing the reason, such as if the command template is not found or its partitions are not in the expected
    /// state.</returns>
    internal async Task<Result<VoidResult, Error>> UpdateCommandTemplateStateAsync(CommandTemplateActivateRequest request, bool setActive = true, CancellationToken ct = default)
    {
        if (request is null)
        {
            return Result<VoidResult, Error>.Failure(CommonErrors.InvalidArgument(nameof(request)));
        }

        var projectAllocation = await userService.GetProjectResourceAllocationWithLocationsAsync(request.projectShortName, request.resourceName, ct);

        if (!projectAllocation.IsSuccess)
        {
            return Result<VoidResult, Error>.Failure(projectAllocation.Error, projectAllocation.State);
        }

        var projectCommandTemplate = await dbContext.Set<ProjectCommandTemplate>()
            .IgnoreQueryFilters()
            .Include(i => i.ProjectCommandTemplatePartitions)
                .ThenInclude(p => p.LocationPartition)
            .Where(w => w.ProjectId == projectAllocation.Value.Id
                && w.ProjectResourceId == projectAllocation.Value.Resource.Id
                && w.Id == request.Id)
            .FirstOrDefaultAsync(ct);

        if (projectCommandTemplate is null)
        {
            return Result<VoidResult, Error>.Failure(Errors.FailedToRetrieveCommandTemplateForProject(request.Id, request.projectShortName, request.resourceName), State.NotFound);
        }

        // Location-grain write guard (ADR-0077 §5): checked BEFORE any state mutation below, and
        // BEFORE the current-state validation, so a frozen Location always wins with 409 rather
        // than a stale-state 400. The checked set is every partition actually being written — NOT
        // narrowed to the allocation's Resource.Locations, which can legitimately diverge from the
        // partitions (this query uses IgnoreQueryFilters, the allocation does not). Narrowing to
        // the intersection would fail OPEN for a partition whose Location fell out of that set;
        // fall back to the raw id for the message rather than skip the check when a name is missing.
        // Grouped, not ToDictionary'd: a Resource can legitimately list the same LocationId more
        // than once (one entry per aggregation/queue), and a plain ToDictionary throws on the
        // duplicate — which would reject the write with a 500 before the guard below ever ran.
        var resourceLocationNames = projectAllocation.Value.Resource.Locations
            .GroupBy(l => l.LocationId)
            .ToDictionary(g => g.Key, g => g.First().LocationName);
        var locationNamesById = projectCommandTemplate.ProjectCommandTemplatePartitions
            .Where(p => p.LocationPartition is not null)
            .Select(p => p.LocationPartition.LocationId)
            .Distinct()
            .ToDictionary(
                locationId => locationId,
                locationId => resourceLocationNames.GetValueOrDefault(locationId, locationId.ToString()));

        var frozenError = await writeGuard.RejectIfFrozenAsync(locationNamesById, ct);
        if (frozenError is not null)
        {
            return Result<VoidResult, Error>.Failure(frozenError, State.Error);
        }

        var requiredCurrentState = setActive
            ? ECommandTemplateState.Inactive
            : ECommandTemplateState.Active;

        var partitions = projectCommandTemplate.ProjectCommandTemplatePartitions;
        var allPartitionsHasSameState = partitions.All(a => a.State == requiredCurrentState);
        if (!allPartitionsHasSameState)
        {
            var actualStates = partitions
                .GroupBy(p => p.State)
                .Select(g => $"{g.Key}({g.Count()})")
                .ToList();
            return Result<VoidResult, Error>.Failure(
                Errors.CommandTemplatePartitionsInconsistentState(
                    request.Id, request.projectShortName, request.resourceName,
                    requiredCurrentState, actualStates),
                State.Error);
        }

        var newState = setActive
            ? ECommandTemplateState.Active
            : ECommandTemplateState.Inactive;

        foreach (var partition in partitions)
        {
            partition.State = newState;
            dbContext.Entry(partition).State = EntityState.Modified;
        }
        int countChanged = await dbContext.SaveChangesAsync(ct);

        if (countChanged <= 0)
        {
            return Result<VoidResult, Error>.Failure(
                Errors.FailedToUpdateCommandTemplateState(request.Id, request.projectShortName, request.resourceName, newState),
                State.CriticalError);
        }

        await cache.RemoveByTagAsync(HeappeCacheKeys.PermissionProjectTag(projectAllocation.Value.Id), ct);
        await cache.RemoveByTagAsync(CacheTags.TemplatesInfo(request.projectShortName), ct);
        foreach (var location in projectAllocation.Value.Resource.Locations.DistinctBy(l => l.HeappeInstance.Identifier))
        {
            await cache.RemoveByTagAsync(CacheTags.HeappeClusters(location.HeappeInstance.Identifier), ct);
        }

        return Result<VoidResult, Error>.Success(default, State.NoContent);

    }

    /// <summary>
    /// Determines whether the specified project resource allocation has expired based on its start and end dates.
    /// </summary>
    /// <remarks>The allocation is considered expired if the current UTC date is not within both the
    /// allocation's and its resource's start and end date ranges.</remarks>
    /// <param name="projectAllocation">The project resource allocation entry to evaluate. Must not be null. The allocation and its associated resource
    /// must have valid start and end dates.</param>
    /// <returns>true if the allocation is expired; otherwise, false.</returns>
    private static bool CheckAllocationExpiration(ProjectResourceInfoEntry projectAllocation)
    {
        var actualDate = DateTime.UtcNow;
        var isExpired = projectAllocation.StartDate <= actualDate && actualDate <= projectAllocation.EndDate
                            && projectAllocation.Resource.StartDate <= actualDate && actualDate <= projectAllocation.Resource.EndDate
            ? false
            : true;

        return isExpired;
    }
}