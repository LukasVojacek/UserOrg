using Heappe.Persistence;

using Lukdrasil.Error;

using Microsoft.EntityFrameworkCore;

namespace Heappe.Features.KillSwitch;

/// <summary>
/// Rejects a write (Activate/Deactivate/Create) targeting a frozen Location, before any stored
/// state is mutated (ADR-0077 §5). Location-grain only: consumes
/// <see cref="IKillSwitchStateService.GetEnforcedKilledLocationIdsAsync"/>, which ignores the
/// whitelist by design — whitelisting governs runnability (the read paths), not writability.
/// </summary>
internal sealed class CommandTemplateWriteGuard(IKillSwitchStateService killSwitchState, HeappeIntegrationDbContext dbContext)
{
    /// <summary>
    /// Checks whether any of <paramref name="locationNamesById"/> currently carries an enforced
    /// kill-switch and, if so, returns the rejection error for the first one found.
    /// </summary>
    /// <param name="locationNamesById">The candidate Locations, keyed by id, with their display name
    /// for the error message.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A 409-mapped <see cref="Error"/> (<see cref="KillSwitchErrorCodes.LocationFrozen"/>),
    /// or <see langword="null"/> when none of the candidates are frozen.</returns>
    internal async Task<Error?> RejectIfFrozenAsync(
        IReadOnlyDictionary<Guid, string> locationNamesById,
        CancellationToken ct)
    {
        if (locationNamesById.Count == 0)
        {
            return null;
        }

        var frozenLocationIds = await killSwitchState.GetEnforcedKilledLocationIdsAsync(locationNamesById.Keys.ToList(), ct);
        if (frozenLocationIds.Count == 0)
        {
            return null;
        }

        // Re-read is-enforced + enforced-at for the message. A lift can land between B02's read
        // above and this one (B09 lifts by flipping IsEnforced to false, not deleting the row), so
        // a vanished row or one that is no longer enforced means the write may proceed — never a
        // 500 from an empty re-read, and never a stale EnforcedAtUtc turned into a false 409.
        foreach (var frozenLocationId in locationNamesById.Keys.Where(frozenLocationIds.Contains))
        {
            var killSwitch = await dbContext.LocationCommandTemplateKillSwitches
                .AsNoTracking()
                .Where(k => k.LocationId == frozenLocationId)
                .Select(k => new { k.IsEnforced, k.EnforcedAtUtc })
                .FirstOrDefaultAsync(ct);

            if (killSwitch is { IsEnforced: true })
            {
                return Errors.LocationFrozenByKillSwitch(locationNamesById[frozenLocationId], killSwitch.EnforcedAtUtc);
            }
        }

        return null;
    }
}