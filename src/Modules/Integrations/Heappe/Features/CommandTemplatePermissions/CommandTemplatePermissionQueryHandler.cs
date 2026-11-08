using Heappe.Contracts.CommandTemplatePermissions.Contract;
using Heappe.Features.CommandTemplatePermissions.Response;
using Heappe.Features.KillSwitch;
using Heappe.HostContracts;
using Heappe.Persistence;
using Heappe.Persistence.Entities;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

using UserOrg.Shared.Abstractions.Interfaces;
using UserOrg.Shared.Errors;

namespace Heappe.Features.CommandTemplatePermissions;

/// <summary>
/// Handles queries for retrieving user permissions related to command templates within the specified project and
/// resource context.
/// </summary>
/// <param name="dbContext">The database context used to access project and command template data. Cannot be null.</param>
/// <param name="userService">A service for retrieving user project allocation and identity information. Cannot be null.</param>
/// <param name="cache">The hybrid cache used to memoize the expensive per-user HEAppE allocation lookup and the composed response.</param>
/// <param name="killSwitchState">Reports which partitions sit on a frozen location, degrading their <c>Enabled</c> flag (ADR-0077 §3).</param>
internal sealed class CommandTemplatePermissionQueryHandler(HeappeIntegrationDbContext dbContext, IUserorgService userService, HybridCache cache, IKillSwitchStateService killSwitchState) : IRequestHandler<CommandTemplatePermissionQuery, Result<UserCommandTemplatePermissionModel, Error>>
{
    private static readonly HybridCacheEntryOptions _cacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5)
    };


    /// <summary>
    /// Handles a query to retrieve the command template permissions for a user within a specified HEAppE context.
    /// </summary>
    /// <param name="request">The query containing the user's identity provider ID and HEAppE identifier for which to retrieve permissions.
    /// Cannot be null.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a model describing the user's
    /// command template permissions for the specified HEAppE context.</returns>
    public async Task<Result<UserCommandTemplatePermissionModel, Error>> Handle(CommandTemplatePermissionQuery request, CancellationToken ct)
    {
        if (request is null)
        {
            return Result<UserCommandTemplatePermissionModel, Error>.Failure(CommonErrors.InvalidArgument(nameof(request)));
        }

        // Anonymous callers (no identity) are not cached: the response is user-specific.
        if (string.IsNullOrEmpty(request.IdpUserId))
        {
            return await BuildPermissionsAsync(request.IdpUserId, request.HeappeIdentifier, ct);
        }

        return await BuildCachedPermissionsAsync(request.IdpUserId, request.HeappeIdentifier, ct);
    }

    private async Task<Result<UserCommandTemplatePermissionModel, Error>> BuildCachedPermissionsAsync(string idpUserId, string heappeIdentifier, CancellationToken ct)
    {
        IList<ProjectAllocationInfoEntry> allocations;
        try
        {
            allocations = await GetCachedAllocationsAsync(idpUserId, heappeIdentifier, ct);
        }
        catch (AllocationsUnavailableException ex)
        {
            return Result<UserCommandTemplatePermissionModel, Error>.Failure(ex.Error, ex.State);
        }

        if (allocations.Count == 0)
        {
            return Result<UserCommandTemplatePermissionModel, Error>.Success(new UserCommandTemplatePermissionModel(heappeIdentifier, []), State.Ok);
        }

        var tags = BuildPermissionTags(allocations);

        var result = await cache.GetOrCreateAsync(
            HeappeCacheKeys.Permissions(idpUserId, heappeIdentifier),
            async token => await ComposeModelAsync(heappeIdentifier, allocations, token),
            _cacheOptions,
            tags: tags,
            cancellationToken: ct);

        return Result<UserCommandTemplatePermissionModel, Error>.Success(result, State.OkWithContent);
    }

    /// <summary>
    /// Builds the tags carried by a cached permission entry: one per project, so a command-template state change
    /// for that project evicts the entry across all users sharing the project, plus the global kill-switch tag,
    /// so enforcing or lifting a kill-switch drops every permission entry in a single eviction (ADR-0077 §4 —
    /// per-project eviction alone would leave a hole during an incident).
    /// </summary>
    /// <param name="allocations">The caller's project allocations backing the entry.</param>
    /// <returns>The cache tags for the entry.</returns>
    internal static string[] BuildPermissionTags(IList<ProjectAllocationInfoEntry> allocations)
        => allocations
            .Select(a => HeappeCacheKeys.PermissionProjectTag(a.Id))
            .Distinct()
            .Append(HeappeCacheKeys.KillSwitchGlobalTag)
            .ToArray();

    private async Task<IList<ProjectAllocationInfoEntry>> GetCachedAllocationsAsync(string idpUserId, string heappeIdentifier, CancellationToken ct)
        => await cache.GetOrCreateAsync(
            HeappeCacheKeys.PermissionAllocations(idpUserId, heappeIdentifier),
            async token =>
            {
                var allocations = await userService.GetUserProjectAllocationsAsync(idpUserId, heappeIdentifier, token);
                if (allocations.IsSuccess)
                {
                    return allocations.Value;
                }

                // NotFound is a valid empty result (cacheable); any other failure is transient and must not be cached.
                return allocations.State == State.NotFound
                    ? (IList<ProjectAllocationInfoEntry>)[]
                    : throw new AllocationsUnavailableException(allocations.Error, allocations.State);
            },
            _cacheOptions,
            tags: [HeappeCacheKeys.PermissionHeappeTag(heappeIdentifier)],
            cancellationToken: ct);

    private async Task<Result<UserCommandTemplatePermissionModel, Error>> BuildPermissionsAsync(string? idpUserId, string heappeIdentifier, CancellationToken ct)
    {
        var projectData = await userService.GetUserProjectAllocationsAsync(idpUserId, heappeIdentifier, ct);

        if (!projectData.IsSuccess)
        {
            return projectData.State == State.NotFound
                ? Result<UserCommandTemplatePermissionModel, Error>.Success(new UserCommandTemplatePermissionModel(heappeIdentifier, []), State.Ok)
                : Result<UserCommandTemplatePermissionModel, Error>.Failure(projectData.Error, projectData.State);
        }

        var result = await ComposeModelAsync(heappeIdentifier, projectData.Value, ct);
        return Result<UserCommandTemplatePermissionModel, Error>.Success(result, State.OkWithContent);
    }

    private async Task<UserCommandTemplatePermissionModel> ComposeModelAsync(string heappeIdentifier, IList<ProjectAllocationInfoEntry> allocations, CancellationToken ct)
    {
        var projectIds = allocations.Select(s => s.Id).ToList();
        var commandTemplatePartitions = await dbContext.Set<ProjectCommandTemplate>().AsNoTracking()
           .Where(w => projectIds.Contains(w.ProjectId))
           .SelectMany(se => se.ProjectCommandTemplatePartitions.Select(s => new CommandTemplatePartitionRow
               (
                   se.ProjectId,
                   se.Id,
                   se.Name,
                   s.State,
                   s.LocationPartition.LocationId,
                   s.ProjectCommandTemplate.ProjectResourceId,
                   s.LocationPartition.AggregationName,
                   s.LocationPartition.Name
               ))).ToListAsync(ct);

        var killedPartitions = await killSwitchState.GetKilledPartitionsAsync(
            commandTemplatePartitions
                .Select(p => new PartitionKillKey(p.LocationId, p.ProjectCommandTemplateId))
                .Distinct()
                .ToList(),
            ct);

        return new UserCommandTemplatePermissionModel(
            heappeIdentifier,
            CommandTemplatePermissionComposer.Compose(commandTemplatePartitions, allocations, killedPartitions));
    }

    public sealed class AllocationsUnavailableException(Error error, State state) : Exception
    {
        public Error Error { get; } = error;

        public State State { get; } = state;
    }
}