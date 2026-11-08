using System.Collections.ObjectModel;

using Heappe.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Heappe.Features.KillSwitch;

/// <inheritdoc/>
internal sealed class KillSwitchStateService(HeappeIntegrationDbContext dbContext) : IKillSwitchStateService
{
    /// <inheritdoc/>
    public async Task<IReadOnlySet<PartitionKillKey>> GetKilledPartitionsAsync(IReadOnlyCollection<PartitionKillKey> partitionKeys, CancellationToken ct = default)
    {
        if (partitionKeys.Count == 0)
        {
            return ReadOnlySet<PartitionKillKey>.Empty;
        }

        var locationIds = partitionKeys.Select(p => p.LocationId).Distinct().ToList();

        var enforcedLocations = await EnforcedLocationsAsync(locationIds, ct);
        if (enforcedLocations.Count == 0)
        {
            return ReadOnlySet<PartitionKillKey>.Empty;
        }

        var enforcedLocationIds = enforcedLocations.Select(l => l.LocationId).ToHashSet();

        // Only whitelist rows on a frozen Location that still honours its whitelist can change a
        // decision, so scope the second round-trip to those. A Location under lockdown ignores its
        // whitelist rather than deleting it (ADR-0013 §3), so its rows are left out of the read.
        var whitelistHonouringLocationIds = enforcedLocations
            .Where(l => !l.IgnoreWhitelist)
            .Select(l => l.LocationId)
            .ToList();

        var whitelisted = await WhitelistedPartitionsAsync(whitelistHonouringLocationIds, ct);

        return partitionKeys
            .Where(p => enforcedLocationIds.Contains(p.LocationId) && !whitelisted.Contains(p))
            .ToHashSet();
    }

    private async Task<HashSet<PartitionKillKey>> WhitelistedPartitionsAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken ct)
    {
        if (locationIds.Count == 0)
        {
            return [];
        }

        var rows = await dbContext.LocationCommandTemplateWhitelists
            .AsNoTracking()
            .Where(w => locationIds.Contains(w.LocationId))
            .Select(w => new { w.LocationId, w.ProjectCommandTemplateId })
            .ToListAsync(ct);

        return rows
            .Select(w => new PartitionKillKey(w.LocationId, w.ProjectCommandTemplateId))
            .ToHashSet();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlySet<Guid>> GetEnforcedKilledLocationIdsAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken ct = default)
        => locationIds.Count == 0
            ? ReadOnlySet<Guid>.Empty
            : (await EnforcedLocationsAsync(locationIds, ct)).Select(l => l.LocationId).ToHashSet();

    private async Task<IReadOnlyList<EnforcedLocation>> EnforcedLocationsAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken ct)
        => await dbContext.LocationCommandTemplateKillSwitches
            .AsNoTracking()
            .Where(k => k.IsEnforced && locationIds.Contains(k.LocationId))
            .Select(k => new EnforcedLocation(k.LocationId, k.IgnoreWhitelist))
            .ToListAsync(ct);

    /// <summary>A frozen Location and whether it is under lockdown.</summary>
    private sealed record EnforcedLocation(Guid LocationId, bool IgnoreWhitelist);
}