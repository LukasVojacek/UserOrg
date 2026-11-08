using System.Threading.Channels;

using Heappe.Contracts.Entries;
using Heappe.Features.BackgroundSync;
using Heappe.Features.KillSwitch.Contract;
using Heappe.HostContracts;
using Heappe.Persistence;
using Heappe.Persistence.Entities;
using Heappe.Persistence.Enums;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

using UserOrg.Shared;

namespace Heappe.Features.KillSwitch;

/// <summary>
/// Serves the admin write/command side of the Location kill-switch: listing, inspecting, enforcing and lifting,
/// per Location and provider-wide (ADR-0077, ADR-0080, ADR-0082).
/// </summary>
/// <remarks>
/// The caller is read from claims at the endpoint and handed in as a <see cref="CallerInfo"/>; provider scoping
/// is enforced here. Only this surface returns <c>reason</c>/<c>enforcedAt</c> — they may hint at an unpatched
/// vulnerability, so no other read path exposes them.
/// </remarks>
internal sealed class KillSwitchAdminHandler(
    HeappeIntegrationDbContext dbContext,
    IUserorgService userorgService,
    IKillSwitchStateService killSwitchStateService,
    HybridCache cache,
    Channel<WorkItem> resyncChannel,
    ILogger<KillSwitchAdminHandler> logger)
{
    private const int _minimumReasonLength = 10;
    private const string _frozenState = "Frozen";

    /// <summary>
    /// Lists the Locations the caller may manage with their current kill-switch state.
    /// </summary>
    /// <param name="caller">The caller's claim state, as read at the endpoint boundary.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>200 with the Locations, or 403 when the caller manages no provider.</returns>
    public async Task<IResult> ListAsync(CallerInfo caller, CancellationToken ct)
    {
        var locations = await GetManageableLocationsAsync(caller, ct);
        if (locations.Failure is not null)
        {
            return locations.Failure;
        }

        var locationIds = locations.Value.Select(l => l.LocationId).ToList();
        var killSwitches = await dbContext.LocationCommandTemplateKillSwitches
            .AsNoTracking()
            .Where(k => locationIds.Contains(k.LocationId))
            .ToDictionaryAsync(k => k.LocationId, ct);

        var states = locations.Value
            .Select(location => ToLocationState(location, killSwitches.GetValueOrDefault(location.LocationId)))
            .ToList();

        return Results.Ok<IReadOnlyList<KillSwitchLocationState>>(states);
    }

    /// <summary>
    /// Reports one Location's kill-switch state and the stored/effective state of every command template on it.
    /// </summary>
    /// <param name="caller">The caller's claim state, as read at the endpoint boundary.</param>
    /// <param name="providerName">The name of the provider owning the Location.</param>
    /// <param name="locationName">The Location's name.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>200 with the detail, 403 when the Location belongs to another provider, or 409 when the name is ambiguous or unknown.</returns>
    public async Task<IResult> GetLocationAsync(CallerInfo caller, string providerName, string locationName, CancellationToken ct)
    {
        var target = await ResolveManageableLocationAsync(caller, providerName, locationName, ct);
        if (target.Failure is not null)
        {
            return target.Failure;
        }

        var location = target.Value;
        var killSwitch = await dbContext.LocationCommandTemplateKillSwitches
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.LocationId == location.LocationId, ct);

        var templateRows = await LoadTemplateRowsAsync([location.LocationId], ct);
        var templates = await ComposeTemplateStatesAsync(location.LocationId, templateRows, ct);

        return Results.Ok(new LocationKillSwitchDetail(
            location.ProviderName,
            location.LocationName,
            killSwitch?.IsEnforced ?? false,
            killSwitch?.Reason,
            killSwitch?.EnforcedAtUtc,
            killSwitch?.IgnoreWhitelist ?? false,
            templates));
    }

    /// <summary>
    /// REPLACES one Location's standing whitelist — the templates that keep running while the Location is
    /// frozen (ADR-0086 §1).
    /// </summary>
    /// <remarks>
    /// Independent of the kill-switch's state: the whitelist is configuration prepared ahead of an incident, so
    /// this works whether or not the Location is enforced. Replace semantics — an empty list clears it. Every
    /// item must resolve to a template on THIS Location or the whole request is refused before a row is written.
    /// </remarks>
    /// <param name="caller">The caller's claim state, as read at the endpoint boundary.</param>
    /// <param name="providerName">The name of the provider owning the Location.</param>
    /// <param name="locationName">The Location's name.</param>
    /// <param name="request">The whitelist to store in place of the current one.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>200 with the Location's detail, 400 on an unknown item, 403 when the Location belongs to another provider, or 409 when the name is ambiguous or unknown.</returns>
    public async Task<IResult> SetLocationWhitelistAsync(
        CallerInfo caller,
        string providerName,
        string locationName,
        SetLocationWhitelistRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var target = await ResolveManageableLocationAsync(caller, providerName, locationName, ct);
        if (target.Failure is not null)
        {
            return target.Failure;
        }

        var location = target.Value;
        var actingUserId = await ResolveActingUserIdAsync(caller, ct);
        if (actingUserId.Failure is not null)
        {
            return actingUserId.Failure;
        }

        var whitelistedTemplateIds = await ResolveWhitelistAsync(location.LocationId, request.Items, ct);
        if (whitelistedTemplateIds.Failure is not null)
        {
            return whitelistedTemplateIds.Failure;
        }

        await ReplaceWhitelistAsync(location.LocationId, whitelistedTemplateIds.Value, actingUserId.Value, ct);
        await dbContext.SaveChangesAsync(ct);

        // UNCONDITIONALLY, even when the Location is not enforced (ADR-0086 Consequences): conditioning this on
        // IsEnforced buys a race in which a freeze starting between the check and the write leaves a template
        // removed from the whitelist launchable for the cache's full TTL (ADR-0084).
        await EvictPermissionCacheAsync(ct);

        return await GetLocationAsync(caller, providerName, locationName, ct);
    }

    /// <summary>
    /// Enforces or lifts the kill-switch on ONE Location.
    /// </summary>
    /// <param name="caller">The caller's claim state, as read at the endpoint boundary.</param>
    /// <param name="providerName">The name of the provider owning the Location.</param>
    /// <param name="locationName">The Location's name.</param>
    /// <param name="request">What to apply: enforce (with reason, optionally under lockdown) or lift. The whitelist itself is written elsewhere (ADR-0086 §2).</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>200 with the outcome, 400 on a missing reason or lockdown without a freeze, 403 when the Location belongs to another provider, or 409 when the name is ambiguous or unknown.</returns>
    public async Task<IResult> SetLocationAsync(
        CallerInfo caller,
        string providerName,
        string locationName,
        SetLocationKillSwitchRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ValidateRequest(request.IsEnforced, request.Reason, request.IgnoreWhitelist) is IResult invalid)
        {
            return invalid;
        }

        var target = await ResolveManageableLocationAsync(caller, providerName, locationName, ct);
        if (target.Failure is not null)
        {
            return target.Failure;
        }

        var location = target.Value;
        var actingUserId = await ResolveActingUserIdAsync(caller, ct);
        if (actingUserId.Failure is not null)
        {
            return actingUserId.Failure;
        }

        if (!request.IsEnforced)
        {
            var unqueued = await LiftAsync([location.LocationId], actingUserId.Value, ct);
            return Results.Ok(new KillSwitchLocationUpdateResult(
                location.ProviderName, location.LocationName, IsEnforced: false, [], unqueued));
        }

        await UpsertKillSwitchAsync([location.LocationId], request.Reason!, request.IgnoreWhitelist, actingUserId.Value, ct);
        await dbContext.SaveChangesAsync(ct);
        await EvictPermissionCacheAsync(ct);

        var stillAllowed = await ComposeStillAllowedAsync(location.LocationId, request.IgnoreWhitelist, ct);

        return Results.Ok(new KillSwitchLocationUpdateResult(
            location.ProviderName, location.LocationName, IsEnforced: true, stillAllowed, []));
    }

    /// <summary>
    /// Enforces or lifts the kill-switch across ALL of a provider's current Locations (ADR-0082).
    /// </summary>
    /// <remarks>
    /// Enforce NEVER touches the whitelist (ADR-0086 revises ADR-0082 §2a): the whitelist is standing
    /// configuration prepared ahead of an incident, so "freeze everything now, exceptions included" is
    /// <c>IgnoreWhitelist</c>, fanned out to every Location, rather than a delete that cannot be undone.
    /// </remarks>
    /// <param name="caller">The caller's claim state, as read at the endpoint boundary.</param>
    /// <param name="providerName">The provider's name.</param>
    /// <param name="request">What to apply: enforce (with reason, optionally under lockdown) or lift-all. Carries no whitelist by design.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>200 with the outcome, 400 on a missing reason or lockdown without a freeze, 403 for another provider, or 409 when the provider name is ambiguous or unknown.</returns>
    public async Task<IResult> SetProviderAsync(
        CallerInfo caller,
        string providerName,
        SetProviderKillSwitchRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ValidateRequest(request.IsEnforced, request.Reason, request.IgnoreWhitelist) is IResult invalid)
        {
            return invalid;
        }

        var providerId = await ResolveManageableProviderIdAsync(caller, providerName, ct);
        if (providerId.Failure is not null)
        {
            return providerId.Failure;
        }

        var locationIds = await userorgService.GetLocationIdsForProviderAsync(providerId.Value, ct);
        if (!locationIds.IsSuccess)
        {
            return Results.Problem(
                $"Could not enumerate the locations of provider '{providerName}'.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var actingUserId = await ResolveActingUserIdAsync(caller, ct);
        if (actingUserId.Failure is not null)
        {
            return actingUserId.Failure;
        }

        var targets = locationIds.Value.ToList();
        if (!request.IsEnforced)
        {
            var unqueued = await LiftAsync(targets, actingUserId.Value, ct);
            return Results.Ok(new KillSwitchProviderUpdateResult(
                providerName, IsEnforced: false, targets.Count, StillAllowedCount: 0, unqueued));
        }

        await UpsertKillSwitchAsync(targets, request.Reason!, request.IgnoreWhitelist, actingUserId.Value, ct);
        await dbContext.SaveChangesAsync(ct);
        await EvictPermissionCacheAsync(ct);

        var stillAllowedCount = await CountStillAllowedAsync(targets, request.IgnoreWhitelist, ct);

        return Results.Ok(new KillSwitchProviderUpdateResult(
            providerName, IsEnforced: true, targets.Count, stillAllowedCount, []));
    }

    /// <summary>
    /// Platform admin FIRST, ownership second: a platform admin carries no <c>provider_id</c> claim, so checking
    /// ownership first would 403 them (ADR-0080 §3). A provider admin whose claim is missing is refused.
    /// </summary>
    private static bool CanManage(CallerInfo caller, Guid providerId)
        => caller.IsPlatformAdmin || (caller.ProviderId is Guid callerProviderId && callerProviderId == providerId);

    /// <summary>
    /// Validates the parts of an enforce/lift request that need no lookup, so a malformed request is refused
    /// before anything is resolved or written.
    /// </summary>
    private static IResult? ValidateRequest(bool isEnforced, string? reason, bool ignoreWhitelist) => (isEnforced, ignoreWhitelist) switch
    {
        (true, _) when reason is null || reason.Trim().Length < _minimumReasonLength => Results.Problem(
            $"A reason of at least {_minimumReasonLength} characters is required when enforcing a kill-switch.",
            statusCode: StatusCodes.Status400BadRequest),

        // Nothing to ignore when nothing is frozen (ADR-0086 §3). Refusing rather than silently dropping the
        // flag keeps "lift" from ever persisting a lockdown that the next enforce could revive.
        (false, true) => Results.Problem(
            "IgnoreWhitelist can only be set while enforcing a kill-switch; there is nothing to ignore when lifting one.",
            statusCode: StatusCodes.Status400BadRequest),

        _ => null
    };

    private static IResult AmbiguousProvider(string providerName)
        => Results.Problem(
            $"Provider '{providerName}' is unknown or matches more than one provider; refusing to guess.",
            statusCode: StatusCodes.Status409Conflict);

    private static IResult ForeignProvider(string providerName)
        => Results.Problem(
            $"You may not manage kill-switches for provider '{providerName}'.",
            statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// Resolves a provider name to the id the caller may manage, or the failure to return: 409 when the name is
    /// unknown or ambiguous, 403 when the provider is not the caller's.
    /// </summary>
    private async Task<Outcome<Guid>> ResolveManageableProviderIdAsync(CallerInfo caller, string providerName, CancellationToken ct)
    {
        var providerId = await userorgService.ResolveProviderIdByNameAsync(providerName, ct);
        if (!providerId.IsSuccess)
        {
            return Outcome<Guid>.Failed(AmbiguousProvider(providerName));
        }

        return CanManage(caller, providerId.Value)
            ? Outcome<Guid>.Succeeded(providerId.Value)
            : Outcome<Guid>.Failed(ForeignProvider(providerName));
    }

    private async Task<Outcome<IList<ProviderLocationEntry>>> GetManageableLocationsAsync(CallerInfo caller, CancellationToken ct)
    {
        var locations = await userorgService.GetProviderLocationsAsync(ct);
        if (!locations.IsSuccess)
        {
            return Outcome<IList<ProviderLocationEntry>>.Failed(Results.Problem(
                "Could not retrieve the provider locations.",
                statusCode: StatusCodes.Status500InternalServerError));
        }

        if (caller.IsPlatformAdmin)
        {
            return Outcome<IList<ProviderLocationEntry>>.Succeeded(locations.Value);
        }

        if (caller.ProviderId is not Guid callerProviderId)
        {
            return Outcome<IList<ProviderLocationEntry>>.Failed(Results.Problem(
                "You may not manage kill-switches: no provider is associated with your account.",
                statusCode: StatusCodes.Status403Forbidden));
        }

        return Outcome<IList<ProviderLocationEntry>>.Succeeded(
            locations.Value.Where(l => l.ProviderId == callerProviderId).ToList());
    }

    /// <summary>
    /// Resolves the Location the caller may manage, scoping to the provider BEFORE the Location name is looked
    /// up: a caller refused the provider is refused identically whether or not the name exists.
    /// </summary>
    /// <remarks>
    /// The order is the security property, not a style choice. Resolving the name first would answer 409 for an
    /// unknown Location and 403 for a known one, and since <c>GetProviderLocationsAsync</c> enumerates non-public
    /// Locations, that split is an existence oracle over Locations the caller cannot otherwise enumerate. A
    /// foreign provider therefore yields <c>ForeignProvider</c> before any name is resolved; only within a
    /// provider the caller may manage does the 409 stay diagnostic.
    /// </remarks>
    private async Task<Outcome<ProviderLocationEntry>> ResolveManageableLocationAsync(
        CallerInfo caller,
        string providerName,
        string locationName,
        CancellationToken ct)
    {
        var providerId = await ResolveManageableProviderIdAsync(caller, providerName, ct);
        if (providerId.Failure is not null)
        {
            return Outcome<ProviderLocationEntry>.Failed(providerId.Failure);
        }

        var locations = await userorgService.GetProviderLocationsAsync(ct);
        if (!locations.IsSuccess)
        {
            return Outcome<ProviderLocationEntry>.Failed(Results.Problem(
                "Could not retrieve the provider locations.",
                statusCode: StatusCodes.Status500InternalServerError));
        }

        var matches = locations.Value
            .Where(l => l.ProviderId == providerId.Value
                && string.Equals(l.LocationName, locationName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count == 1
            ? Outcome<ProviderLocationEntry>.Succeeded(matches[0])
            : Outcome<ProviderLocationEntry>.Failed(Results.Problem(
                $"Location '{locationName}' of provider '{providerName}' is unknown or matches more than one location; refusing to guess.",
                statusCode: StatusCodes.Status409Conflict));
    }

    private async Task<Outcome<Guid>> ResolveActingUserIdAsync(CallerInfo caller, CancellationToken ct)
    {
        if (caller.IdentityProviderUserId is not string identityProviderUserId)
        {
            return Outcome<Guid>.Failed(Results.Unauthorized());
        }

        var userId = await userorgService.ResolveUserIdByIdentityProviderIdAsync(identityProviderUserId, ct);
        return userId.IsSuccess
            ? Outcome<Guid>.Succeeded(userId.Value)
            : Outcome<Guid>.Failed(Results.Problem(
                "Could not resolve the calling user.",
                statusCode: StatusCodes.Status500InternalServerError));
    }

    /// <summary>
    /// Resolves the whitelist to internal template ids in ONE Core round trip, refusing the WHOLE request if any
    /// item is unknown so a typo can never silently leave a template frozen (or exempt).
    /// </summary>
    /// <remarks>
    /// The batch resolver reports partial success — an unknown, ambiguous or non-owned pair is simply ABSENT
    /// from its result, and it already enforces that the resource belongs to the project. So an absent pair IS
    /// the unknown-item case, and the diff below is the only thing standing between a typo and a silently
    /// partially-applied whitelist. Ownership is deliberately not re-checked here.
    /// </remarks>
    private async Task<Outcome<List<Guid>>> ResolveWhitelistAsync(
        Guid locationId,
        IReadOnlyList<KillSwitchWhitelistItem> whitelist,
        CancellationToken ct)
    {
        if (whitelist.Count == 0)
        {
            return Outcome<List<Guid>>.Succeeded([]);
        }

        // Distinct pairs: several templates commonly share one (project, resource), and many items may even name
        // the same pair, so asking Core once per item is the N+1 this batch exists to remove.
        var resolved = await userorgService.ResolveProjectResourceIdsAsync(
            [.. whitelist
                .Select(i => new ProjectResourceNamePair(i.ProjectShortName, i.ResourceName))
                .DistinctBy(p => PairKey(p.ProjectShortName, p.ResourceName), StringComparer.OrdinalIgnoreCase)],
            ct);

        if (!resolved.IsSuccess)
        {
            // A whole-batch failure is a Core outage, not a bad request: the items may well be valid.
            return Outcome<List<Guid>>.Failed(Results.Problem(
                "Could not resolve the whitelist's (project, resource) pairs.",
                statusCode: StatusCodes.Status500InternalServerError));
        }

        var idsByPair = new Dictionary<string, ProjectResourceIdEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in resolved.Value)
        {
            // TryAdd, not Add: entries carry CORE's spelling of both names, so two requested spellings of one
            // pair can come back as two entries that key identically. They resolve to the same ids either way.
            idsByPair.TryAdd(PairKey(entry.ProjectShortName, entry.ResourceName), new ProjectResourceIdEntry(entry.ProjectId, entry.ProjectResourceId));
        }

        var templatesOnLocation = await LoadTemplateRowsAsync([locationId], ct);
        var templateIds = new List<Guid>();

        foreach (var item in whitelist)
        {
            if (!idsByPair.TryGetValue(PairKey(item.ProjectShortName, item.ResourceName), out var ids))
            {
                return Outcome<List<Guid>>.Failed(UnknownWhitelistItem(item));
            }

            var match = templatesOnLocation.Find(t => t.ProjectId == ids.ProjectId
                && t.ProjectResourceId == ids.ProjectResourceId
                && string.Equals(t.TemplateName, item.TemplateName, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                return Outcome<List<Guid>>.Failed(UnknownWhitelistItem(item));
            }

            templateIds.Add(match.TemplateId);
        }

        return Outcome<List<Guid>>.Succeeded(templateIds.Distinct().ToList());
    }

    /// <summary>
    /// Keys a (project, resource) name pair for case-insensitive lookup. The separator is a control character
    /// no name may contain, so <c>("ab", "c")</c> cannot collide with <c>("a", "bc")</c>.
    /// </summary>
    private static string PairKey(string projectShortName, string resourceName)
        => $"{projectShortName}\u001F{resourceName}";

    private static IResult UnknownWhitelistItem(KillSwitchWhitelistItem item)
        => Results.Problem(
            $"Command template '{item.TemplateName}' of '{item.ProjectShortName}'/'{item.ResourceName}' does not exist on this location; no changes were applied.",
            statusCode: StatusCodes.Status400BadRequest);

    private async Task UpsertKillSwitchAsync(IReadOnlyCollection<Guid> locationIds, string reason, bool ignoreWhitelist, Guid actingUserId, CancellationToken ct)
    {
        var existing = await dbContext.LocationCommandTemplateKillSwitches
            .Where(k => locationIds.Contains(k.LocationId))
            .ToDictionaryAsync(k => k.LocationId, ct);

        var enforcedAtUtc = DateTime.UtcNow;
        foreach (var locationId in locationIds)
        {
            if (existing.TryGetValue(locationId, out var killSwitch))
            {
                killSwitch.IsEnforced = true;
                killSwitch.IgnoreWhitelist = ignoreWhitelist;
                killSwitch.Reason = reason;
                killSwitch.EnforcedAtUtc = enforcedAtUtc;
                killSwitch.EnforcedByUserId = actingUserId;
                killSwitch.LiftedAtUtc = null;
                killSwitch.LiftedByUserId = null;
                continue;
            }

            dbContext.LocationCommandTemplateKillSwitches.Add(new LocationCommandTemplateKillSwitch
            {
                LocationId = locationId,
                IsEnforced = true,
                IgnoreWhitelist = ignoreWhitelist,
                Reason = reason,
                EnforcedAtUtc = enforcedAtUtc,
                EnforcedByUserId = actingUserId
            });
        }
    }

    /// <summary>
    /// Replaces a Location's whitelist rows with <paramref name="templateIds"/>, stamping who added each
    /// exception and when (ADR-0086 §1).
    /// </summary>
    /// <remarks>Tracked delete-then-insert rather than <c>ExecuteDelete</c>: the module's unit tests run on the
    /// EF InMemory provider, which cannot execute bulk operations, so a bulk call here would silently stop this
    /// path from being exercised at all.</remarks>
    private async Task ReplaceWhitelistAsync(Guid locationId, IReadOnlyCollection<Guid> templateIds, Guid actingUserId, CancellationToken ct)
    {
        var existing = await dbContext.LocationCommandTemplateWhitelists
            .Where(w => w.LocationId == locationId)
            .ToListAsync(ct);

        dbContext.LocationCommandTemplateWhitelists.RemoveRange(existing);

        var addedAtUtc = DateTime.UtcNow;
        foreach (var templateId in templateIds)
        {
            dbContext.LocationCommandTemplateWhitelists.Add(new LocationCommandTemplateWhitelist
            {
                LocationId = locationId,
                ProjectCommandTemplateId = templateId,
                AddedByUserId = actingUserId,
                AddedAtUtc = addedAtUtc
            });
        }
    }

    /// <summary>
    /// Lifting leaves the whitelist STANDING (ADR-0086, revising ADR-0077 §7): it is configuration the
    /// administrator prepared ahead of the next incident, not a parameter of the freeze that just ended.
    /// <c>IgnoreWhitelist</c> is always reset, so a lockdown can never revive on the next enforce.
    /// </summary>
    private async Task<IReadOnlyList<KillSwitchResyncItem>> LiftAsync(IReadOnlyCollection<Guid> locationIds, Guid actingUserId, CancellationToken ct)
    {
        var killSwitches = await dbContext.LocationCommandTemplateKillSwitches
            .Where(k => locationIds.Contains(k.LocationId))
            .ToListAsync(ct);

        var liftedAtUtc = DateTime.UtcNow;
        foreach (var killSwitch in killSwitches)
        {
            killSwitch.IsEnforced = false;
            killSwitch.IgnoreWhitelist = false;
            killSwitch.LiftedAtUtc = liftedAtUtc;
            killSwitch.LiftedByUserId = actingUserId;
        }

        await dbContext.SaveChangesAsync(ct);
        await EvictPermissionCacheAsync(ct);

        return await EnqueueResyncAsync(locationIds, ct);
    }

    private async Task<IReadOnlyList<KillSwitchResyncItem>> EnqueueResyncAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken ct)
    {
        var templateRows = await LoadTemplateRowsAsync(locationIds, ct);
        var projectResourceIds = templateRows
            .Select(r => new ProjectResourceIdEntry(r.ProjectId, r.ProjectResourceId))
            .Distinct()
            .ToList();

        if (projectResourceIds.Count == 0)
        {
            return [];
        }

        var names = await userorgService.ResolveProjectResourceNamesAsync(projectResourceIds, ct);
        if (!names.IsSuccess)
        {
            logger.LogWarning("Kill-switch lift could not resolve the (project, resource) names to resync for {LocationCount} location(s); no resync was queued", locationIds.Count);
            return [];
        }

        var unqueued = new List<KillSwitchResyncItem>();
        foreach (var name in names.Value)
        {
            // TryWrite, never WriteAsync: the channel is bounded with FullMode.Wait, so a blocking write would
            // stall the lift behind the sync backlog — exactly when the admin needs the freeze gone.
            if (!resyncChannel.Writer.TryWrite(new WorkItem(name.ProjectShortName, name.ResourceName)))
            {
                logger.LogWarning("Resync queue is full: {ProjectShortName}/{ResourceName} was not queued after a kill-switch lift and must be re-run", name.ProjectShortName, name.ResourceName);
                unqueued.Add(new KillSwitchResyncItem(name.ProjectShortName, name.ResourceName));
            }
        }

        return unqueued;
    }

    private async Task EvictPermissionCacheAsync(CancellationToken ct)
        => await cache.RemoveByTagAsync(HeappeCacheKeys.KillSwitchGlobalTag, ct);

    private static KillSwitchLocationState ToLocationState(ProviderLocationEntry location, LocationCommandTemplateKillSwitch? killSwitch)
        => new(
            location.ProviderName,
            location.LocationName,
            killSwitch?.IsEnforced ?? false,
            killSwitch?.Reason,
            killSwitch?.EnforcedAtUtc,
            killSwitch?.IgnoreWhitelist ?? false);

    /// <summary>
    /// Names the whitelist that is in force AFTER a request: what an operator can still launch. Empty under
    /// lockdown, where the rows survive but exempt nothing (ADR-0086 §4).
    /// </summary>
    private async Task<IReadOnlyList<KillSwitchWhitelistItem>> ComposeStillAllowedAsync(Guid locationId, bool ignoreWhitelist, CancellationToken ct)
    {
        if (ignoreWhitelist)
        {
            return [];
        }

        var whitelistedTemplateIds = await LoadWhitelistedTemplateIdsAsync(locationId, ct);
        if (whitelistedTemplateIds.Count == 0)
        {
            return [];
        }

        var whitelistedRows = (await LoadTemplateRowsAsync([locationId], ct))
            .Where(r => whitelistedTemplateIds.Contains(r.TemplateId))
            .DistinctBy(r => r.TemplateId)
            .ToList();

        var namesByIds = await ResolveNamesByIdsAsync(
            [.. whitelistedRows.Select(r => new ProjectResourceIdEntry(r.ProjectId, r.ProjectResourceId)).Distinct()],
            ct);

        // An unresolvable pair is DROPPED, not named blank: this list is what an operator may still launch, so a
        // half-named entry would be an instruction nobody can act on.
        return
        [
            .. whitelistedRows
                .Select(row => (Row: row, Name: namesByIds.GetValueOrDefault(new ProjectResourceIdEntry(row.ProjectId, row.ProjectResourceId))))
                .Where(x => x.Name is not null)
                .Select(x => new KillSwitchWhitelistItem(x.Name!.ProjectShortName, x.Name.ResourceName, x.Row.TemplateName))
        ];
    }

    /// <summary>
    /// Reads the ids of the templates whitelisted on a Location.
    /// </summary>
    private async Task<List<Guid>> LoadWhitelistedTemplateIdsAsync(Guid locationId, CancellationToken ct)
        => await dbContext.LocationCommandTemplateWhitelists
            .AsNoTracking()
            .Where(w => w.LocationId == locationId)
            .Select(w => w.ProjectCommandTemplateId)
            .ToListAsync(ct);

    /// <summary>
    /// Resolves id pairs to the names Core knows them by, keyed for lookup.
    /// </summary>
    /// <remarks>A failed batch degrades to an EMPTY map rather than failing the request: the names are display
    /// data hung off state that is already correct and already written, so a Core hiccup must not turn a
    /// successful freeze into an error the admin might retry.</remarks>
    private async Task<Dictionary<ProjectResourceIdEntry, ProjectResourceNameEntry>> ResolveNamesByIdsAsync(
        IReadOnlyCollection<ProjectResourceIdEntry> projectResourceIds,
        CancellationToken ct)
    {
        var names = await userorgService.ResolveProjectResourceNamesAsync(projectResourceIds, ct);
        if (!names.IsSuccess)
        {
            logger.LogWarning("Could not resolve the (project, resource) names of {PairCount} pair(s); they will be reported without names", projectResourceIds.Count);
            return [];
        }

        return names.Value.ToDictionary(n => new ProjectResourceIdEntry(n.ProjectId, n.ProjectResourceId));
    }

    /// <summary>
    /// Counts the whitelist exceptions still in force across the Locations THIS action already authorized.
    /// </summary>
    /// <remarks>
    /// Scoped to <paramref name="locationIds"/>, never a table-wide <c>COUNT(*)</c>: the provider path is
    /// reachable by a provider administrator, so a global count would report how many exceptions OTHER providers
    /// have — the one way ADR-0086's structural provider isolation can be broken. Pinned by a test.
    /// </remarks>
    private async Task<int> CountStillAllowedAsync(IReadOnlyCollection<Guid> locationIds, bool ignoreWhitelist, CancellationToken ct)
        => ignoreWhitelist
            ? 0
            : await dbContext.LocationCommandTemplateWhitelists
                .AsNoTracking()
                .Where(w => locationIds.Contains(w.LocationId))
                .CountAsync(ct);

    private async Task<IReadOnlyList<KillSwitchTemplateState>> ComposeTemplateStatesAsync(
        Guid locationId,
        List<TemplateRow> templateRows,
        CancellationToken ct)
    {
        if (templateRows.Count == 0)
        {
            return [];
        }

        var killed = await killSwitchStateService.GetKilledPartitionsAsync(
            templateRows.Select(r => new PartitionKillKey(r.LocationId, r.TemplateId)).Distinct().ToList(),
            ct);

        var whitelistedTemplateIds = await LoadWhitelistedTemplateIdsAsync(locationId, ct);

        var namesByIds = await ResolveNamesByIdsAsync(
            [.. templateRows.Select(r => new ProjectResourceIdEntry(r.ProjectId, r.ProjectResourceId)).Distinct()],
            ct);

        return templateRows
            .GroupBy(r => r.TemplateId)
            .Select(templatePartitions =>
            {
                var row = templatePartitions.First();
                var storedState = RollUpState(templatePartitions.Select(p => p.State).ToList());
                var isKilled = killed.Contains(new PartitionKillKey(row.LocationId, row.TemplateId));
                var name = namesByIds.GetValueOrDefault(new ProjectResourceIdEntry(row.ProjectId, row.ProjectResourceId));

                return new KillSwitchTemplateState(
                    name?.ProjectShortName ?? string.Empty,
                    name?.ResourceName ?? string.Empty,
                    row.TemplateName,
                    storedState.ToString(),
                    isKilled ? _frozenState : storedState.ToString(),
                    whitelistedTemplateIds.Contains(row.TemplateId));
            })
            .ToList();
    }

    /// <summary>
    /// Rolls a template's per-aggregation partitions on ONE location up to a single stored state. Distinct from
    /// <c>ProjectCommandTemplate.State</c>, which rolls up across every location the template lives on.
    /// </summary>
    private static ECommandTemplateState RollUpState(List<ECommandTemplateState> states) => states switch
    {
        _ when states.TrueForAll(s => s == ECommandTemplateState.Active) => ECommandTemplateState.Active,
        _ when states.TrueForAll(s => s == ECommandTemplateState.Inactive) => ECommandTemplateState.Inactive,
        _ => ECommandTemplateState.Pending
    };

    /// <summary>
    /// Loads one row per <c>(location, template, aggregation partition)</c>. The partition's link to its template
    /// is an EF shadow FK, so it is read through the navigation rather than a CLR property.
    /// </summary>
    private async Task<List<TemplateRow>> LoadTemplateRowsAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken ct)
        => await dbContext.Set<ProjectCommandTemplatePartition>()
            .AsNoTracking()
            .Where(p => locationIds.Contains(p.LocationPartition.LocationId))
            .Select(p => new TemplateRow(
                p.LocationPartition.LocationId,
                p.ProjectCommandTemplate.Id,
                p.ProjectCommandTemplate.Name,
                p.ProjectCommandTemplate.ProjectId,
                p.ProjectCommandTemplate.ProjectResourceId,
                p.State))
            .ToListAsync(ct);

    private sealed record TemplateRow(
        Guid LocationId,
        Guid TemplateId,
        string TemplateName,
        Guid ProjectId,
        Guid ProjectResourceId,
        ECommandTemplateState State);

    /// <summary>
    /// Either a value or the HTTP failure to return, so each step can short-circuit without throwing.
    /// </summary>
    private sealed class Outcome<T>
    {
        private Outcome(T value, IResult? failure)
        {
            Value = value;
            Failure = failure;
        }

        internal T Value { get; }

        internal IResult? Failure { get; }

        internal static Outcome<T> Succeeded(T value) => new(value, null);

        internal static Outcome<T> Failed(IResult failure) => new(default!, failure);
    }
}