using System.Collections.Immutable;

using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Shared;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Domain.Entities;

using Waldur.Contracts.Payloads;
using Waldur.Sync.Diff;

namespace Userorg.Adapter.Waldur.Sync;

/// <summary>
/// One (instance name → upsert ServiceName) mapping the cold rebuild needs (the worker calls
/// <see cref="IShaMapRebuilder.RebuildAsync"/> with the logical instance name, e.g. "EFP", but the DB
/// keys everything by the ServiceName discriminator, e.g. "EFP-Waldur"). Registered per enabled
/// instance via <see cref="WaldurEventConsumerExtensions.AddWaldurShaMapRebuilder"/>.
/// </summary>
internal sealed record WaldurRebuildInstance(string Instance, string ServiceName);

/// <summary>
/// Adapter-side implementation of the startup cold-rebuild port (ADR-0055/D1, plan §4.7): reads the
/// UserOrg DB and reproduces EXACTLY the <c>Waldur.Contracts</c> payloads the producer mapper
/// (<c>WaldurMapper</c>, B03) emits, then hashes each via its own generated
/// <see cref="UserOrg.Shared.Hashing.IIdentityHashed.ComputeIdentityHash"/> and keys it by
/// <see cref="ShaKey"/>. Living adapter-side keeps the DB out of <c>Waldur.Sync</c>.
/// </summary>
/// <remarks>
/// The five CRITICAL round-trip invariants (D1/D13, guarded by the Phase-4.8 round-trip test):
/// <list type="number">
/// <item><b>EfpCore excluded.</b> Every read is positively filtered to the instance's
/// <c>ServiceName</c>. Reconciliation-owned rows (<c>ServiceName == EfpCoreServiceName</c>, ADR-0061)
/// carry a DIFFERENT service name and are therefore never included — no negative filter needed.</item>
/// <item><b>Mapped role read verbatim.</b> A membership's role is read back from the persisted
/// project-scoped <see cref="UserPermission.SystemRoleId"/> resolved through the SAME role list the
/// consumer wrote it with — the mapped Userorg role, not a re-derivation.</item>
/// <item><b>HeappeUri/Identifier verbatim.</b> Read straight out of the <c>HEAPPE_URL</c>/
/// <c>HEAPPE_IDENTIFIER</c> assigned-location values with no re-normalization (B08 stores them
/// canonical/verbatim).</item>
/// <item><b>Config slot never rebuilt</b> (<see cref="ShaKey.StorageConfig"/>, ADR-0055/A4) — this
/// rebuilder never emits it.</item>
/// <item><b>Stored shapes read as-is.</b> Grouping/naming/dates/allocation-unit/truncation already
/// live in the persisted rows (the mapper produced them); they are read back verbatim, never
/// re-truncated or re-normalized.</item>
/// </list>
/// Soft-deleted projects ARE included (their <c>IsRemoved</c> flag is part of the hash, ADR-0055/A4).
/// </remarks>
internal sealed class WaldurShaMapRebuilder : IShaMapRebuilder
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Dictionary<string, string> _serviceNameByInstance;

    public WaldurShaMapRebuilder(IServiceScopeFactory scopeFactory, IEnumerable<WaldurRebuildInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(instances);
        _scopeFactory = scopeFactory;
        _serviceNameByInstance = instances.ToDictionary(i => i.Instance, i => i.ServiceName, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyDictionary<ShaKey, string>> RebuildAsync(string instance, CancellationToken ct)
    {
        if (!_serviceNameByInstance.TryGetValue(instance, out var serviceName))
        {
            throw new InvalidOperationException($"No Waldur rebuild instance registered for '{instance}'.");
        }

        // A cold rebuild reads the DB from a background service (singleton) — own scope, fresh context.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IUserOrgDbContext>();
        var roleListService = scope.ServiceProvider.GetRequiredService<IRoleListService>();
        return await RebuildCoreAsync(context, roleListService, serviceName, ct);
    }

    /// <summary>
    /// The pure DB → payloads → hash reconstruction for one instance's <paramref name="serviceName"/>.
    /// Kept internal so the round-trip guardian test drives it against a shared context directly.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<ShaKey, string>> RebuildCoreAsync(
        IUserOrgDbContext context,
        IRoleListService roleListService,
        string serviceName,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(roleListService);

        var locationTypeNames = await context.LocationTypesQuery.ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var allocationTypeNames = await context.AllocationUnitTypesQuery.ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var rolesResult = await roleListService.ListRoles(ct);
        if (!rolesResult.IsSuccess)
        {
            throw new InvalidOperationException($"Cold rebuild failed to retrieve system roles: {rolesResult.Error}");
        }

        var roleNamesById = rolesResult.Value
            .GroupBy(r => r.Id)
            .ToDictionary(g => g.Key, g => g.First().Name);

        var map = new Dictionary<ShaKey, string>();
        await RebuildProvidersAsync(context, serviceName, locationTypeNames, map, ct);
        await RebuildProjectsAsync(context, serviceName, allocationTypeNames, roleNamesById, map, ct);
        return map;
    }

    private static async Task RebuildProvidersAsync(
        IUserOrgDbContext context,
        string serviceName,
        IReadOnlyDictionary<int, string?> locationTypeNames,
        Dictionary<ShaKey, string> map,
        CancellationToken ct)
    {
        var providers = await context.ProvidersQuery
            .Include(p => p.Country)
            .Where(p => p.ServiceName == serviceName)
            .ToListAsync(ct);

        // Only this instance's locations (EfpCore locations carry a different ServiceName → excluded).
        var locations = await context.LocationsQuery
            .Where(l => l.ServiceName == serviceName)
            .ToListAsync(ct);
        var locationsByProvider = locations
            .GroupBy(l => l.ProviderId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var provider in providers)
        {
            var providerLocations = locationsByProvider.TryGetValue(provider.Id, out var locs) ? locs : [];
            var locationPayloads = providerLocations
                .Select(l => new ProviderLocationPayload(
                    l.Name ?? string.Empty,
                    l.Endpoint ?? string.Empty,
                    // W1: LocationType is resolved case-insensitively downstream, so the DB holds the
                    // canonical casing while the mapper hashed a case-folded value — fold on read-back too.
                    CaseFold(locationTypeNames.GetValueOrDefault(l.LocationTypeId)) ?? string.Empty,
                    l.Description,
                    l.IsShared,
                    l.IsDeleted))
                .ToImmutableList();

            var payload = new ProviderPayload(
                provider.ExternalId ?? string.Empty,
                provider.Name ?? string.Empty,
                // W1: Country is resolved case-insensitively downstream — fold the canonical DB casing
                // back so the hash matches the mapper's folded value (no spurious ProviderUpdated).
                CaseFold(provider.Country?.Name),
                provider.Description,
                locationPayloads);

            map[ShaKey.Provider(payload.ExternalId)] = payload.ComputeIdentityHash();
        }
    }

    private static async Task RebuildProjectsAsync(
        IUserOrgDbContext context,
        string serviceName,
        IReadOnlyDictionary<int, string?> allocationTypeNames,
        IReadOnlyDictionary<int, string> roleNamesById,
        Dictionary<ShaKey, string> map,
        CancellationToken ct)
    {
        // Includes soft-deleted projects (IsRemoved is part of the hash, ADR-0055/A4).
        var projects = await context.ProjectsQuery
            .Where(p => p.ServiceName == serviceName)
            .ToListAsync(ct);

        foreach (var project in projects)
        {
            var resources = await BuildResourcePayloadsAsync(context, project.Id, serviceName, allocationTypeNames, ct);

            var payload = new ProjectPayload(
                project.ExternalId ?? string.Empty,
                project.Name ?? string.Empty,
                project.ShortName.Value,
                project.Description,
                project.StartDate,
                project.EndDate,
                project.IsDeleted,
                resources);

            map[ShaKey.Project(payload.ExternalId)] = payload.ComputeIdentityHash();

            await RebuildMembersAsync(context, project, roleNamesById, map, ct);
        }
    }

    private static async Task<IImmutableList<ResourcePayload>> BuildResourcePayloadsAsync(
        IUserOrgDbContext context,
        UserOrganizationManagement.Domain.ValueObjects.ProjectId projectId,
        string serviceName,
        IReadOnlyDictionary<int, string?> allocationTypeNames,
        CancellationToken ct)
    {
        var resourceIds = await context.AssignedProjectResourceToProjectsQuery
            .Where(a => a.ProjectId == projectId && !a.IsDeleted)
            .Select(a => a.ProjectResourceId)
            .ToListAsync(ct);

        if (resourceIds.Count == 0)
        {
            return [];
        }

        var resources = await context.ProjectResourcesQuery
            .Include(r => r.AssignedLocations).ThenInclude(al => al.Location)
            .Include(r => r.AssignedLocations).ThenInclude(al => al.AssignedLocationsValues)
            .Where(r => resourceIds.Contains(r.Id) && r.ServiceName == serviceName)
            .ToListAsync(ct);

        var payloads = new List<ResourcePayload>();
        foreach (var resource in resources)
        {
            // Exclude EfpCore-owned assigned locations (storage reconciliation, ADR-0061): they carry a
            // different ServiceName and are not part of the project payload/hash.
            foreach (var assigned in resource.AssignedLocations.Where(al => al.ServiceName == serviceName))
            {
                var values = assigned.AssignedLocationsValues
                    .Where(v => v.Name != ApplicationConstants.HeappeUriKey && v.Name != ApplicationConstants.HeappeIdentifierKey)
                    .Select(v => new ResourceAllocationValuePayload(v.Name, v.Value))
                    .ToImmutableList();

                var heappeUri = assigned.AssignedLocationsValues
                    .FirstOrDefault(v => v.Name == ApplicationConstants.HeappeUriKey)?.Value;
                var heappeIdentifier = assigned.AssignedLocationsValues
                    .FirstOrDefault(v => v.Name == ApplicationConstants.HeappeIdentifierKey)?.Value;

                payloads.Add(new ResourcePayload(
                    resource.Name ?? string.Empty,
                    assigned.ExternalId ?? string.Empty,
                    resource.Description,
                    resource.StartDate ?? DateTime.MinValue,
                    resource.EndDate ?? DateTime.MaxValue,
                    assigned.IsDeleted,
                    assigned.Location?.Endpoint ?? string.Empty,
                    assigned.AggregationName ?? string.Empty,
                    allocationTypeNames.GetValueOrDefault(assigned.AllocationUnitTypeId),
                    assigned.AllocationUnitCount,
                    heappeUri,
                    heappeIdentifier,
                    values));
            }
        }

        return payloads.ToImmutableList();
    }

    private static async Task RebuildMembersAsync(
        IUserOrgDbContext context,
        Project project,
        IReadOnlyDictionary<int, string> roleNamesById,
        Dictionary<ShaKey, string> map,
        CancellationToken ct)
    {
        // One membership per NON-deleted assignment (ADR-0055/A4).
        var assignments = await context.AssignedUserToProjectsQuery
            .Include(a => a.User)
            .Where(a => a.ProjectId == project.Id && !a.IsDeleted)
            .ToListAsync(ct);

        foreach (var assignment in assignments)
        {
            var user = assignment.User;
            if (user is null)
            {
                continue;
            }

            var permission = await context.UserPermissionsQuery
                .Include(p => p.User)
                .Include(p => p.Project)
                .FirstOrDefaultAsync(p => p.User!.Id == user.Id && p.Project!.Id == project.Id, ct);

            var roleName = permission?.SystemRoleId is int roleId && roleNamesById.TryGetValue(roleId, out var name)
                ? name
                : RoleInfoConstants.ProjectMemberRoleName;

            var access = await context.UserProjectAccessesQuery
                .FirstOrDefaultAsync(a => a.ProjectId == project.Id && a.UserId == user.Id && !a.IsDeleted, ct);

            var payload = new ProjectUserPayload(
                project.ExternalId ?? string.Empty,
                user.UserName ?? string.Empty,
                roleName,
                access?.HasAccess ?? false);

            map[ShaKey.Member(payload.ProjectExternalId, payload.UserName)] = payload.ComputeIdentityHash();
        }
    }

    /// <summary>
    /// Case-fold a reference-name field the consumer resolves case-insensitively (Country,
    /// LocationType), mirroring the mapper's <c>CaseFold</c> so DB-canonical casing hashes identically
    /// to the mapper's folded value (review W1).
    /// </summary>
#pragma warning disable CA1308 // Deliberate lowercase fold for a byte-stable, case-insensitive hash — not security normalization.
    private static string? CaseFold(string? value) => value?.ToLowerInvariant();
#pragma warning restore CA1308
}