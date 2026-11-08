using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Heappe.Contracts.Entries;
using Heappe.Features.BackgroundSync;
using Heappe.Features.KillSwitch;
using Heappe.Features.KillSwitch.Contract;
using Heappe.Persistence;
using Heappe.Persistence.Entities;
using Heappe.Persistence.Enums;
using Heappe.HostContracts;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using UserOrg.Shared;

using Xunit;

namespace Heappe.UnitTests.Features.KillSwitch;

/// <summary>
/// Pins the admin write/command side: scoping precedence (ADR-0080 §3), fail-closed name resolution (409),
/// atomic whitelist validation (400), global cache eviction (ADR-0077 §4), the standing whitelist and its
/// lockdown flag (ADR-0086) and the provider-wide fan-out (ADR-0082).
/// </summary>
public sealed class KillSwitchAdminHandlerTests
{
    private static readonly Guid _alphaProviderId = Guid.NewGuid();
    private static readonly Guid _betaProviderId = Guid.NewGuid();
    private static readonly Guid _alphaLocationA = Guid.NewGuid();
    private static readonly Guid _alphaLocationB = Guid.NewGuid();
    private static readonly Guid _betaLocation = Guid.NewGuid();
    private static readonly Guid _duplicateLocationOne = Guid.NewGuid();
    private static readonly Guid _duplicateLocationTwo = Guid.NewGuid();

    private static readonly Guid _projectOne = Guid.NewGuid();
    private static readonly Guid _resourceOne = Guid.NewGuid();
    private static readonly Guid _resourceTwo = Guid.NewGuid();
    private static readonly Guid _callerUserId = Guid.NewGuid();

    private const string _reason = "Suspected RCE in the job submission path";

    private static CallerInfo ProviderAdmin(Guid providerId) => new("idp-provider-admin", providerId, IsPlatformAdmin: false);
    private static CallerInfo PlatformAdmin() => new("idp-platform-admin", null, IsPlatformAdmin: true);
    private static CallerInfo ProviderAdminWithoutClaim() => new("idp-broken-admin", null, IsPlatformAdmin: false);

    /// <summary>
    /// Reads a <see cref="CallerInfo"/> the way the endpoint boundary does, from the claim set the Gateway
    /// mints for an interactive human ProviderAdmin (D5): <c>provider_name</c> carries the provider they
    /// administer and <c>provider_id</c> is emitted empty. Built from claims rather than from the record,
    /// unlike the three factories above, because the empty <c>provider_id</c> is exactly the part under test.
    /// </summary>
    private static CallerInfo CallerFromGatewayMintedClaims(string administeredProvider)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim("sub", "idp-human-provider-admin"),
                    new Claim(ClaimTypes.Role, RoleInfoConstants.ProviderAdministratorRoleName),
                    new Claim(EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.JwtBearerToken)),
                    new Claim(AuthConstants.ProviderNameClaimType, administeredProvider),
                    new Claim(AuthConstants.ProviderIdClaimType, string.Empty)
                ],
                "GatewayJwt"))
        };

        return context.GetCallerInfo();
    }

    [Fact(DisplayName = "#3 Provider admin acting on a Location of a DIFFERENT provider is forbidden")]
    public async Task Provider_admin_on_foreign_location_is_forbidden()
    {
        var world = new World();

        var result = await world.Handler.SetLocationAsync(
            ProviderAdmin(_alphaProviderId), "beta", "loc-c", Enforce(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
        Assert.Empty(world.DbContext.LocationCommandTemplateKillSwitches);
    }

    [Fact(DisplayName = "#3 Provider admin reading a Location of a DIFFERENT provider is forbidden")]
    public async Task Provider_admin_reading_foreign_location_is_forbidden()
    {
        var world = new World();

        var result = await world.Handler.GetLocationAsync(
            ProviderAdmin(_alphaProviderId), "beta", "loc-c", CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
    }

    [Fact(DisplayName = "#3 Provider admin probing a FOREIGN provider cannot tell an existing Location from a missing one")]
    public async Task Foreign_provider_answers_the_same_whether_or_not_the_location_exists()
    {
        var world = new World();
        var alphaAdmin = ProviderAdmin(_alphaProviderId);

        var readExisting = await world.Handler.GetLocationAsync(alphaAdmin, "beta", "loc-c", CancellationToken.None);
        var readMissing = await world.Handler.GetLocationAsync(alphaAdmin, "beta", "ghost", CancellationToken.None);
        var writeExisting = await world.Handler.SetLocationAsync(alphaAdmin, "beta", "loc-c", Enforce(), CancellationToken.None);
        var writeMissing = await world.Handler.SetLocationAsync(alphaAdmin, "beta", "ghost", Enforce(), CancellationToken.None);

        // Scoping must precede name resolution: GetProviderLocationsAsync enumerates NON-public Locations
        // too, so answering 403-when-it-exists / 409-when-it-does-not would turn these routes into an
        // existence oracle over Locations this caller cannot otherwise enumerate.
        Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(readExisting));
        Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(readMissing));
        Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(writeExisting));
        Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(writeMissing));
        Assert.Empty(world.DbContext.LocationCommandTemplateKillSwitches);
    }

    [Fact(DisplayName = "#3 Provider admin without a provider_id claim is forbidden (fail-closed)")]
    public async Task Provider_admin_without_provider_claim_is_forbidden()
    {
        var world = new World();

        var result = await world.Handler.SetLocationAsync(
            ProviderAdminWithoutClaim(), "alpha", "loc-a", Enforce(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
    }

    /// <summary>
    /// Pins the parse, not a privilege boundary: the Gateway's <c>provider_name</c> mint (D5) is not a second
    /// source of kill-switch authority, because authority is read from <c>provider_id</c> and the empty string
    /// the mint emits cannot become a Guid. It does <b>not</b> show that a human ProviderAdmin lacks
    /// kill-switch authority — these routes are hosted in the Core API, whose <c>GatewayJwt</c>
    /// <c>OnTokenValidated</c> re-derives and injects <c>provider_id</c> server-side for every
    /// ProviderAdministrator principal, deliberately and per ADR-0080, so in the deployed pipeline such a
    /// caller does hold authority over the provider they administer.
    /// </summary>
    [Fact(DisplayName = "#3 The Gateway-minted provider_name (D5) is not itself a source of kill-switch authority")]
    public async Task Gateway_minted_claims_alone_carry_no_provider_authority()
    {
        var world = new World();

        var result = await world.Handler.SetLocationAsync(
            CallerFromGatewayMintedClaims("alpha"), "alpha", "loc-a", Enforce(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
        Assert.Empty(world.DbContext.LocationCommandTemplateKillSwitches);
    }

    [Fact(DisplayName = "#3b Platform admin (no provider_id claim) may enforce on ANY location")]
    public async Task Platform_admin_may_enforce_on_any_location()
    {
        var world = new World();

        var result = await world.Handler.SetLocationAsync(
            PlatformAdmin(), "beta", "loc-c", Enforce(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusCodeOf(result));
        Assert.True(world.DbContext.LocationCommandTemplateKillSwitches.Single(k => k.LocationId == _betaLocation).IsEnforced);
    }

    [Fact(DisplayName = "#3b Platform admin (no provider_id claim) may act provider-wide on ANY provider")]
    public async Task Platform_admin_may_act_provider_wide_on_any_provider()
    {
        var world = new World();

        var result = await world.Handler.SetProviderAsync(
            PlatformAdmin(), "beta", new SetProviderKillSwitchRequest(IsEnforced: true, _reason), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusCodeOf(result));
        Assert.True(world.DbContext.LocationCommandTemplateKillSwitches.Single(k => k.LocationId == _betaLocation).IsEnforced);
    }

    [Fact(DisplayName = "#3b Platform admin (no provider_id claim) may read any location")]
    public async Task Platform_admin_may_read_any_location()
    {
        var world = new World();

        var result = await world.Handler.GetLocationAsync(PlatformAdmin(), "beta", "loc-c", CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusCodeOf(result));
    }

    [Fact(DisplayName = "GET list returns only the provider admin's own locations with their kill-switch state")]
    public async Task List_is_scoped_to_the_callers_provider()
    {
        var world = new World();
        await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Enforce(), CancellationToken.None);

        var result = await world.Handler.ListAsync(ProviderAdmin(_alphaProviderId), CancellationToken.None);

        var states = ValueOf<IReadOnlyList<KillSwitchLocationState>>(result);
        Assert.All(states, s => Assert.Equal("alpha", s.ProviderName));
        Assert.Equal(["dup", "dup", "loc-a", "loc-b"], states.Select(s => s.LocationName).Order());
        var locA = states.Single(s => s.LocationName == "loc-a");
        Assert.True(locA.IsEnforced);
        Assert.Equal(_reason, locA.Reason);
        Assert.NotNull(locA.EnforcedAtUtc);
    }

    [Fact(DisplayName = "GET list returns every provider's locations for a platform admin")]
    public async Task List_returns_everything_for_platform_admin()
    {
        var world = new World();

        var result = await world.Handler.ListAsync(PlatformAdmin(), CancellationToken.None);

        var states = ValueOf<IReadOnlyList<KillSwitchLocationState>>(result);
        Assert.Contains(states, s => s.ProviderName == "beta");
        Assert.Contains(states, s => s.ProviderName == "alpha");
    }

    [Fact(DisplayName = "GET list is forbidden for a provider admin without a provider_id claim")]
    public async Task List_is_forbidden_without_provider_claim()
    {
        var world = new World();

        var result = await world.Handler.ListAsync(ProviderAdminWithoutClaim(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
    }

    [Fact(DisplayName = "GET detail reports storedState, effectiveState and whitelisted per template")]
    public async Task Detail_reports_stored_and_effective_state()
    {
        var world = new World();
        await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist(KeptAlive()), CancellationToken.None);
        await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Enforce(), CancellationToken.None);

        var result = await world.Handler.GetLocationAsync(PlatformAdmin(), "alpha", "loc-a", CancellationToken.None);

        var detail = ValueOf<LocationKillSwitchDetail>(result);
        Assert.True(detail.IsEnforced);
        Assert.False(detail.IgnoreWhitelist);
        Assert.Equal(_reason, detail.Reason);

        var frozen = detail.Templates.Single(t => t.TemplateName == "frozen-template");
        Assert.Equal("proj-1", frozen.ProjectShortName);
        Assert.Equal("res-1", frozen.ResourceName);
        Assert.Equal(nameof(ECommandTemplateState.Active), frozen.StoredState);
        Assert.Equal("Frozen", frozen.EffectiveState);
        Assert.False(frozen.Whitelisted);

        var whitelisted = detail.Templates.Single(t => t.TemplateName == "kept-alive");
        Assert.Equal(nameof(ECommandTemplateState.Active), whitelisted.StoredState);
        Assert.Equal(nameof(ECommandTemplateState.Active), whitelisted.EffectiveState);
        Assert.True(whitelisted.Whitelisted);
    }

    [Fact(DisplayName = "Ambiguous (providerName, locationName) is a 409, not a 200/404/500")]
    public async Task Ambiguous_location_name_is_conflict()
    {
        var world = new World();

        var put = await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "dup", Enforce(), CancellationToken.None);
        var get = await world.Handler.GetLocationAsync(PlatformAdmin(), "alpha", "dup", CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, StatusCodeOf(put));
        Assert.Equal(StatusCodes.Status409Conflict, StatusCodeOf(get));
        Assert.Empty(world.DbContext.LocationCommandTemplateKillSwitches);
    }

    [Fact(DisplayName = "Unknown (providerName, locationName) is a 409 (fail-closed)")]
    public async Task Unknown_location_name_is_conflict()
    {
        var world = new World();

        var result = await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "nope", Enforce(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, StatusCodeOf(result));
    }

    // ---------- reason validation ----------

    [Theory(DisplayName = "Enforce without a reason of at least 10 characters is a 400")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too short")]
    public async Task Enforce_requires_a_reason(string? reason)
    {
        var world = new World();

        var result = await world.Handler.SetLocationAsync(
            PlatformAdmin(), "alpha", "loc-a",
            new SetLocationKillSwitchRequest(IsEnforced: true, reason),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
        Assert.Empty(world.DbContext.LocationCommandTemplateKillSwitches);
    }

    [Fact(DisplayName = "Lift does not require a reason")]
    public async Task Lift_does_not_require_a_reason()
    {
        var world = new World();

        var result = await world.Handler.SetLocationAsync(
            PlatformAdmin(), "alpha", "loc-a",
            new SetLocationKillSwitchRequest(IsEnforced: false, null),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusCodeOf(result));
    }

    // ---------- #9 atomic whitelist validation (now on the standing whitelist PUT, ADR-0086 §1) ----------

    [Fact(DisplayName = "#9 Whitelist PUT with an unknown item is a 400 and writes NO rows (atomic)")]
    public async Task Unknown_whitelist_item_rejects_the_whole_put()
    {
        var world = new World();

        var result = await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a",
            Whitelist(KeptAlive(), new KillSwitchWhitelistItem("proj-1", "res-1", "does-not-exist")),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
        Assert.Empty(world.DbContext.LocationCommandTemplateWhitelists);
    }

    [Fact(DisplayName = "#9 Whitelist PUT naming a template that is not on the target location is a 400")]
    public async Task Whitelist_item_from_another_location_rejects_the_put()
    {
        var world = new World();

        var result = await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-b", Whitelist(KeptAlive()), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
        Assert.Empty(world.DbContext.LocationCommandTemplateWhitelists);
    }

    [Fact(DisplayName = "#9 Whitelist PUT with an unresolvable (project, resource) pair is a 400")]
    public async Task Unknown_project_resource_pair_rejects_the_put()
    {
        var world = new World();

        var result = await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a",
            Whitelist(new KillSwitchWhitelistItem("ghost-project", "res-1", "kept-alive")),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
        Assert.Empty(world.DbContext.LocationCommandTemplateWhitelists);
    }

    [Fact(DisplayName = "#9 Whitelist PUT stamps who added each exception and when (ADR-0086 §1)")]
    public async Task Whitelist_put_stamps_the_audit_columns()
    {
        var world = new World();

        await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist(KeptAlive()), CancellationToken.None);

        var row = Assert.Single(world.DbContext.LocationCommandTemplateWhitelists);
        Assert.Equal(_callerUserId, row.AddedByUserId);
        Assert.NotEqual(default, row.AddedAtUtc);
    }

    [Fact(DisplayName = "#9 An empty whitelist PUT clears the whitelist (replace semantics)")]
    public async Task Empty_whitelist_put_clears_the_whitelist()
    {
        var world = new World();
        await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist(KeptAlive()), CancellationToken.None);
        Assert.NotEmpty(world.DbContext.LocationCommandTemplateWhitelists);

        var result = await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusCodeOf(result));
        Assert.Empty(world.DbContext.LocationCommandTemplateWhitelists);
    }

    // ---------- #6 cache eviction ----------
    //
    // A REAL cache round-trip (via the actual HybridCache surface + the production
    // HeappeCacheKeys.KillSwitchGlobalTag constant), not a Moq Verify(RemoveByTagAsync) assertion:
    // each test proves entries carrying the global tag are genuinely GONE afterwards (a fresh
    // GetOrCreateAsync call re-invokes the factory), across TWO differently-keyed entries (as two
    // users' cached permission responses would be), while an entry tagged only with an UNRELATED
    // tag survives untouched — proving eviction is selective by tag, not a wholesale cache clear.

    [Fact(DisplayName = "#6 Enforce evicts every cache entry carrying the B01 global tag, and only those")]
    public async Task Enforce_evicts_the_global_cache_tag()
    {
        var world = new World();
        await world.SeedCachedPermissionEntriesAsync();
        Assert.True(await world.IsStillCachedAsync(World.GlobalTaggedKeyOne));

        await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Enforce(), CancellationToken.None);

        Assert.False(await world.IsStillCachedAsync(World.GlobalTaggedKeyOne));
        Assert.False(await world.IsStillCachedAsync(World.GlobalTaggedKeyTwo));
        Assert.True(await world.IsStillCachedAsync(World.UnrelatedTaggedKey));
    }

    [Fact(DisplayName = "#6 Lift evicts every cache entry carrying the B01 global tag, and only those")]
    public async Task Lift_evicts_the_global_cache_tag()
    {
        var world = new World();
        await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Enforce(), CancellationToken.None);
        await world.SeedCachedPermissionEntriesAsync();

        await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Lift(), CancellationToken.None);

        Assert.False(await world.IsStillCachedAsync(World.GlobalTaggedKeyOne));
        Assert.False(await world.IsStillCachedAsync(World.GlobalTaggedKeyTwo));
        Assert.True(await world.IsStillCachedAsync(World.UnrelatedTaggedKey));
    }

    [Fact(DisplayName = "#6 Provider-wide enforce evicts every cache entry carrying the B01 global tag, and only those")]
    public async Task Provider_wide_enforce_evicts_the_global_cache_tag()
    {
        var world = new World();
        await world.SeedCachedPermissionEntriesAsync();

        await world.Handler.SetProviderAsync(
            PlatformAdmin(), "alpha", new SetProviderKillSwitchRequest(IsEnforced: true, _reason), CancellationToken.None);

        Assert.False(await world.IsStillCachedAsync(World.GlobalTaggedKeyOne));
        Assert.False(await world.IsStillCachedAsync(World.GlobalTaggedKeyTwo));
        Assert.True(await world.IsStillCachedAsync(World.UnrelatedTaggedKey));
    }

    [Fact(DisplayName = "#258 The whitelist PUT evicts the global cache tag even when the location is NOT enforced")]
    public async Task Whitelist_put_evicts_the_cache_when_not_enforced()
    {
        var world = new World();
        await world.SeedCachedPermissionEntriesAsync();
        Assert.Empty(world.DbContext.LocationCommandTemplateKillSwitches);

        await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist(KeptAlive()), CancellationToken.None);

        // Conditioning the eviction on IsEnforced would save one eviction on a rare operation and buy a race:
        // a freeze starting between the check and this write leaves a template that was REMOVED from the
        // whitelist launchable for the cache's full TTL (ADR-0084, ADR-0086 Consequences).
        Assert.False(await world.IsStillCachedAsync(World.GlobalTaggedKeyOne));
        Assert.False(await world.IsStillCachedAsync(World.GlobalTaggedKeyTwo));
        Assert.True(await world.IsStillCachedAsync(World.UnrelatedTaggedKey));
    }

    [Fact(DisplayName = "#8 Lift enqueues a resync WorkItem per (project, resource) partition on that location")]
    public async Task Lift_enqueues_a_resync_per_project_resource()
    {
        var world = new World();
        await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Enforce(), CancellationToken.None);

        var result = await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Lift(), CancellationToken.None);

        var payload = ValueOf<KillSwitchLocationUpdateResult>(result);
        Assert.Empty(payload.UnqueuedResyncItems);
        Assert.Equal(
            [("proj-1", "res-1"), ("proj-1", "res-2")],
            world.DrainChannel().Select(w => (w.ProjectShortName, w.ResourceName)).Order());
    }

    [Fact(DisplayName = "#8 Resync overflow logs a Warning and lists the unqueued items in the response")]
    public async Task Lift_overflow_lists_unqueued_items()
    {
        var world = new World(resyncChannelCapacity: 1);
        Assert.True(world.ResyncChannel.Writer.TryWrite(new WorkItem("filler", "filler")));
        await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Enforce(), CancellationToken.None);

        var result = await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Lift(), CancellationToken.None);

        var payload = ValueOf<KillSwitchLocationUpdateResult>(result);
        Assert.NotEmpty(payload.UnqueuedResyncItems);
        Assert.All(payload.UnqueuedResyncItems, i => Assert.Equal("proj-1", i.ProjectShortName));
    }

    // ---------- ADR-0086: the whitelist is STANDING configuration (LOAD-BEARING) ----------
    //
    // These three INVERT what ADR-0077 §7 / ADR-0082 §2a pinned: a lift and a provider-wide enforce used to
    // DELETE the whitelist rows. ADR-0086 makes the whitelist configuration prepared ahead of an incident, so
    // it must survive both; "freeze everything, exceptions included" is IgnoreWhitelist instead.

    [Fact(DisplayName = "#258 The whitelist SURVIVES a lift and is still in force on the next enforce (ADR-0086)")]
    public async Task Whitelist_survives_a_lift()
    {
        var world = new World();
        await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist(KeptAlive()), CancellationToken.None);
        var addedTemplateId = Assert.Single(world.DbContext.LocationCommandTemplateWhitelists).ProjectCommandTemplateId;

        await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Enforce(), CancellationToken.None);
        await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Lift(), CancellationToken.None);

        // The row itself outlives the incident that used it...
        var surviving = Assert.Single(world.DbContext.LocationCommandTemplateWhitelists);
        Assert.Equal(addedTemplateId, surviving.ProjectCommandTemplateId);

        // ...and is still honoured by the NEXT freeze, with no re-authoring by the administrator.
        await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Enforce(), CancellationToken.None);

        var detail = ValueOf<LocationKillSwitchDetail>(
            await world.Handler.GetLocationAsync(PlatformAdmin(), "alpha", "loc-a", CancellationToken.None));
        var stillExempt = detail.Templates.Single(t => t.TemplateName == "kept-alive");
        Assert.True(stillExempt.Whitelisted);
        Assert.Equal(nameof(ECommandTemplateState.Active), stillExempt.EffectiveState);
    }

    [Fact(DisplayName = "#258 A provider-wide lift leaves the whitelists of ALL the provider's locations standing")]
    public async Task Provider_wide_lift_leaves_whitelists_standing()
    {
        var world = new World();
        await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist(KeptAlive()), CancellationToken.None);

        await world.Handler.SetProviderAsync(
            PlatformAdmin(), "alpha", new SetProviderKillSwitchRequest(IsEnforced: false, null), CancellationToken.None);

        Assert.NotEmpty(world.DbContext.LocationCommandTemplateWhitelists);
    }

    [Fact(DisplayName = "#258 A provider-wide enforce does NOT clear the whitelist; lockdown suppresses it instead (ADR-0086 revises ADR-0082 §2a)")]
    public async Task Provider_wide_enforce_suppresses_rather_than_clears_whitelists()
    {
        var world = new World();
        await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist(KeptAlive()), CancellationToken.None);

        // A plain "freeze the whole provider" honours the standing exceptions.
        await world.Handler.SetProviderAsync(
            PlatformAdmin(), "alpha", new SetProviderKillSwitchRequest(IsEnforced: true, _reason), CancellationToken.None);

        Assert.NotEmpty(world.DbContext.LocationCommandTemplateWhitelists);
        var honoured = ValueOf<LocationKillSwitchDetail>(
            await world.Handler.GetLocationAsync(PlatformAdmin(), "alpha", "loc-a", CancellationToken.None));
        Assert.Equal(nameof(ECommandTemplateState.Active), honoured.Templates.Single(t => t.TemplateName == "kept-alive").EffectiveState);

        // "Freeze everything, exceptions included" is the flag — and the rows still survive it.
        await world.Handler.SetProviderAsync(
            PlatformAdmin(), "alpha", new SetProviderKillSwitchRequest(IsEnforced: true, _reason, IgnoreWhitelist: true), CancellationToken.None);

        Assert.NotEmpty(world.DbContext.LocationCommandTemplateWhitelists);
        var lockedDown = ValueOf<LocationKillSwitchDetail>(
            await world.Handler.GetLocationAsync(PlatformAdmin(), "alpha", "loc-a", CancellationToken.None));
        var suppressed = lockedDown.Templates.Single(t => t.TemplateName == "kept-alive");

        // "Ticked, yet Frozen" — explicable only because IgnoreWhitelist is surfaced (ADR-0086 §5).
        Assert.True(lockedDown.IgnoreWhitelist);
        Assert.True(suppressed.Whitelisted);
        Assert.Equal("Frozen", suppressed.EffectiveState);
    }

    // ---------- ADR-0086 §3: lockdown ----------

    [Fact(DisplayName = "#258 IgnoreWhitelist=true with IsEnforced=false is a 400 and changes nothing")]
    public async Task Lockdown_without_a_freeze_is_a_bad_request()
    {
        var world = new World();
        await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Enforce(), CancellationToken.None);

        var result = await world.Handler.SetLocationAsync(
            PlatformAdmin(), "alpha", "loc-a",
            new SetLocationKillSwitchRequest(IsEnforced: false, null, IgnoreWhitelist: true),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));

        // Nothing to ignore when nothing is frozen: the request must not have half-applied the lift either.
        var row = Assert.Single(world.DbContext.LocationCommandTemplateKillSwitches);
        Assert.True(row.IsEnforced);
        Assert.Null(row.LiftedAtUtc);
    }

    [Fact(DisplayName = "#258 Provider-wide IgnoreWhitelist=true with IsEnforced=false is a 400 and changes nothing")]
    public async Task Provider_wide_lockdown_without_a_freeze_is_a_bad_request()
    {
        var world = new World();

        var result = await world.Handler.SetProviderAsync(
            PlatformAdmin(), "alpha",
            new SetProviderKillSwitchRequest(IsEnforced: false, null, IgnoreWhitelist: true),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
        Assert.Empty(world.DbContext.LocationCommandTemplateKillSwitches);
    }

    [Fact(DisplayName = "#258 A lift always resets IgnoreWhitelist, so a stale lockdown cannot revive on the next enforce")]
    public async Task Lift_resets_the_lockdown_flag()
    {
        var world = new World();
        await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", EnforceUnderLockdown(), CancellationToken.None);
        Assert.True(world.DbContext.LocationCommandTemplateKillSwitches.Single().IgnoreWhitelist);

        await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Lift(), CancellationToken.None);
        Assert.False(world.DbContext.LocationCommandTemplateKillSwitches.Single().IgnoreWhitelist);

        // The next, ordinary enforce must NOT inherit the previous incident's lockdown.
        await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Enforce(), CancellationToken.None);
        Assert.False(world.DbContext.LocationCommandTemplateKillSwitches.Single().IgnoreWhitelist);
    }

    [Fact(DisplayName = "#258 Provider-wide lockdown fans IgnoreWhitelist out to EVERY location of the provider")]
    public async Task Provider_wide_lockdown_fans_out_to_every_location()
    {
        var world = new World();

        await world.Handler.SetProviderAsync(
            PlatformAdmin(), "alpha",
            new SetProviderKillSwitchRequest(IsEnforced: true, _reason, IgnoreWhitelist: true),
            CancellationToken.None);

        Assert.Equal(4, world.DbContext.LocationCommandTemplateKillSwitches.Count(k => k.IgnoreWhitelist));
    }

    // ---------- ADR-0086 §4: StillAllowed / StillAllowedCount ----------

    [Fact(DisplayName = "#258 Enforce reports the standing whitelist as StillAllowed")]
    public async Task Enforce_reports_still_allowed()
    {
        var world = new World();
        await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist(KeptAlive()), CancellationToken.None);

        var result = await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Enforce(), CancellationToken.None);

        var payload = ValueOf<KillSwitchLocationUpdateResult>(result);
        Assert.Equal(KeptAlive(), Assert.Single(payload.StillAllowed));
    }

    [Fact(DisplayName = "#258 StillAllowed is EMPTY under lockdown, even though the whitelist rows survive")]
    public async Task Still_allowed_is_empty_under_lockdown()
    {
        var world = new World();
        await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist(KeptAlive()), CancellationToken.None);

        var result = await world.Handler.SetLocationAsync(
            PlatformAdmin(), "alpha", "loc-a", EnforceUnderLockdown(), CancellationToken.None);

        // Nothing is exempt, so nothing may be reported as still launchable — the rows themselves are untouched.
        var payload = ValueOf<KillSwitchLocationUpdateResult>(result);
        Assert.Empty(payload.StillAllowed);
        Assert.NotEmpty(world.DbContext.LocationCommandTemplateWhitelists);
    }

    [Fact(DisplayName = "#258 StillAllowed is EMPTY on a lift")]
    public async Task Still_allowed_is_empty_on_a_lift()
    {
        var world = new World();
        await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist(KeptAlive()), CancellationToken.None);
        await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Enforce(), CancellationToken.None);

        var result = await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Lift(), CancellationToken.None);

        var payload = ValueOf<KillSwitchLocationUpdateResult>(result);
        Assert.Empty(payload.StillAllowed);
    }

    [Fact(DisplayName = "#258 StillAllowedCount counts ONLY the acting provider's locations, never other providers' exceptions")]
    public async Task Still_allowed_count_excludes_foreign_providers()
    {
        var world = new World();

        // alpha/loc-a has ONE exception; beta/loc-c — a FOREIGN provider — has one of its own.
        await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist(KeptAlive()), CancellationToken.None);
        await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "beta", "loc-c",
            Whitelist(new KillSwitchWhitelistItem("proj-1", "res-1", "beta-template")),
            CancellationToken.None);
        Assert.Equal(2, world.DbContext.LocationCommandTemplateWhitelists.Count());

        var result = await world.Handler.SetProviderAsync(
            ProviderAdmin(_alphaProviderId), "alpha", new SetProviderKillSwitchRequest(IsEnforced: true, _reason), CancellationToken.None);

        // A bare table-wide COUNT(*) would say 2 and leak how many exceptions beta has (ADR-0086 Consequences).
        var payload = ValueOf<KillSwitchProviderUpdateResult>(result);
        Assert.Equal(1, payload.StillAllowedCount);
    }

    [Fact(DisplayName = "#258 StillAllowedCount is 0 under a provider-wide lockdown")]
    public async Task Still_allowed_count_is_zero_under_lockdown()
    {
        var world = new World();
        await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist(KeptAlive()), CancellationToken.None);

        var result = await world.Handler.SetProviderAsync(
            PlatformAdmin(), "alpha",
            new SetProviderKillSwitchRequest(IsEnforced: true, _reason, IgnoreWhitelist: true),
            CancellationToken.None);

        Assert.Equal(0, ValueOf<KillSwitchProviderUpdateResult>(result).StillAllowedCount);
    }

    [Fact(DisplayName = "#10 Provider-wide enforce fans out a kill row for EVERY current location of the provider")]
    public async Task Provider_wide_enforce_fans_out_to_every_location()
    {
        var world = new World();

        var result = await world.Handler.SetProviderAsync(
            ProviderAdmin(_alphaProviderId), "alpha", new SetProviderKillSwitchRequest(IsEnforced: true, _reason), CancellationToken.None);

        var payload = ValueOf<KillSwitchProviderUpdateResult>(result);
        Assert.Equal(4, payload.AffectedLocationCount);
        Assert.Equal(
            new[] { _alphaLocationA, _alphaLocationB, _duplicateLocationOne, _duplicateLocationTwo }.Order(),
            world.DbContext.LocationCommandTemplateKillSwitches.Where(k => k.IsEnforced).Select(k => k.LocationId).ToList().Order());
        Assert.DoesNotContain(_betaLocation, world.DbContext.LocationCommandTemplateKillSwitches.Select(k => k.LocationId).ToList());
    }

    [Fact(DisplayName = "#10 A partition on any of the provider's locations is killed after a provider-wide enforce")]
    public async Task Provider_wide_enforce_kills_partitions()
    {
        var world = new World();

        await world.Handler.SetProviderAsync(
            PlatformAdmin(), "alpha", new SetProviderKillSwitchRequest(IsEnforced: true, _reason), CancellationToken.None);

        var detail = ValueOf<LocationKillSwitchDetail>(
            await world.Handler.GetLocationAsync(PlatformAdmin(), "alpha", "loc-a", CancellationToken.None));
        Assert.All(detail.Templates, t => Assert.Equal("Frozen", t.EffectiveState));
    }

    [Fact(DisplayName = "#11 Provider-wide lift-all clears every location, including one with an independent location kill")]
    public async Task Provider_wide_lift_clears_independent_location_kills()
    {
        var world = new World();
        await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-b", Enforce(), CancellationToken.None);
        await world.Handler.SetProviderAsync(
            PlatformAdmin(), "alpha", new SetProviderKillSwitchRequest(IsEnforced: true, _reason), CancellationToken.None);

        var result = await world.Handler.SetProviderAsync(
            PlatformAdmin(), "alpha", new SetProviderKillSwitchRequest(IsEnforced: false, null), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusCodeOf(result));
        Assert.Empty(world.DbContext.LocationCommandTemplateKillSwitches.Where(k => k.IsEnforced));
        Assert.Equal(
            [("proj-1", "res-1"), ("proj-1", "res-2")],
            world.DrainChannel().Select(w => (w.ProjectShortName, w.ResourceName)).Distinct().Order());
    }

    [Fact(DisplayName = "#12 Provider admin acting provider-wide on a FOREIGN provider is forbidden")]
    public async Task Provider_wide_on_foreign_provider_is_forbidden()
    {
        var world = new World();

        var result = await world.Handler.SetProviderAsync(
            ProviderAdmin(_alphaProviderId), "beta", new SetProviderKillSwitchRequest(IsEnforced: true, _reason), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
        Assert.Empty(world.DbContext.LocationCommandTemplateKillSwitches);
    }

    [Fact(DisplayName = "#12 Ambiguous providerName on the provider-wide route is a 409")]
    public async Task Provider_wide_ambiguous_provider_name_is_conflict()
    {
        var world = new World();

        var result = await world.Handler.SetProviderAsync(
            PlatformAdmin(), "ambiguous", new SetProviderKillSwitchRequest(IsEnforced: true, _reason), CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, StatusCodeOf(result));
        Assert.Empty(world.DbContext.LocationCommandTemplateKillSwitches);
    }

    [Fact(DisplayName = "Provider-wide enforce without a reason of at least 10 characters is a 400")]
    public async Task Provider_wide_enforce_requires_a_reason()
    {
        var world = new World();

        var result = await world.Handler.SetProviderAsync(
            PlatformAdmin(), "alpha", new SetProviderKillSwitchRequest(IsEnforced: true, "short"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
        Assert.Empty(world.DbContext.LocationCommandTemplateKillSwitches);
    }

    [Fact(DisplayName = "Re-enforcing the same location overwrites the row rather than appending")]
    public async Task Enforce_is_last_write_wins()
    {
        var world = new World();
        await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Enforce(), CancellationToken.None);

        await world.Handler.SetLocationAsync(
            PlatformAdmin(), "alpha", "loc-a",
            new SetLocationKillSwitchRequest(IsEnforced: true, "A newer and different incident reason"),
            CancellationToken.None);

        var row = Assert.Single(world.DbContext.LocationCommandTemplateKillSwitches.Where(k => k.LocationId == _alphaLocationA));
        Assert.Equal("A newer and different incident reason", row.Reason);
        Assert.Equal(_callerUserId, row.EnforcedByUserId);
    }

    [Fact(DisplayName = "Lift records who lifted it and when, keeping the row for audit")]
    public async Task Lift_records_the_audit_fields()
    {
        var world = new World();
        await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Enforce(), CancellationToken.None);

        await world.Handler.SetLocationAsync(PlatformAdmin(), "alpha", "loc-a", Lift(), CancellationToken.None);

        var row = Assert.Single(world.DbContext.LocationCommandTemplateKillSwitches.Where(k => k.LocationId == _alphaLocationA));
        Assert.False(row.IsEnforced);
        Assert.NotNull(row.LiftedAtUtc);
        Assert.Equal(_callerUserId, row.LiftedByUserId);
    }

    private static SetLocationKillSwitchRequest Enforce() => new(IsEnforced: true, _reason);

    private static SetLocationKillSwitchRequest EnforceUnderLockdown() => new(IsEnforced: true, _reason, IgnoreWhitelist: true);

    private static SetLocationKillSwitchRequest Lift() => new(IsEnforced: false, null);

    private static SetLocationWhitelistRequest Whitelist(params KillSwitchWhitelistItem[] items) => new(items);

    private static KillSwitchWhitelistItem KeptAlive() => new("proj-1", "res-1", "kept-alive");

    private static int? StatusCodeOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static T ValueOf<T>(IResult result)
    {
        Assert.Equal(StatusCodes.Status200OK, StatusCodeOf(result));
        return Assert.IsType<T>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value, exactMatch: false);
    }

    private sealed class World
    {
        internal HeappeIntegrationDbContext DbContext { get; }
        internal KillSwitchAdminHandler Handler { get; }
        internal Channel<WorkItem> ResyncChannel { get; }

        private readonly FakeHybridCache _cache = new();

        internal World(int resyncChannelCapacity = 100)
        {
            DbContext = CreateSeededDbContext();
            ResyncChannel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(resyncChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

            Handler = new KillSwitchAdminHandler(
                DbContext,
                CreateUserorgService(),
                new KillSwitchStateService(DbContext),
                _cache,
                ResyncChannel,
                NullLogger<KillSwitchAdminHandler>.Instance);
        }

        // Two keys carry the SAME global tag, as two different users' cached permission entries would
        // (mirrors the real producer's tags: [...KillSwitchGlobalTag] wiring); a third carries only an
        // UNRELATED tag, so the eviction test can prove it is selective by tag, not a wholesale clear.
        internal const string GlobalTaggedKeyOne = "CmdTemplatePerm_user-a_heappe-a";
        internal const string GlobalTaggedKeyTwo = "CmdTemplatePerm_user-b_heappe-b";
        internal const string UnrelatedTaggedKey = "CmdTemplatePerm_user-c_heappe-c";
        private const string _unrelatedTag = "CmdTemplatePerm_Project_unrelated";

        internal async Task SeedCachedPermissionEntriesAsync()
        {
            // GetOrCreateAsync (not a raw Set) so this is a genuine cache round-trip through the same
            // API the real producer uses, not a hand-wired shortcut.
            await _cache.GetOrCreateAsync(GlobalTaggedKeyOne, GlobalTaggedKeyOne, (k, _) => ValueTask.FromResult(k), tags: [HeappeCacheKeys.KillSwitchGlobalTag]);
            await _cache.GetOrCreateAsync(GlobalTaggedKeyTwo, GlobalTaggedKeyTwo, (k, _) => ValueTask.FromResult(k), tags: [HeappeCacheKeys.KillSwitchGlobalTag]);
            await _cache.GetOrCreateAsync(UnrelatedTaggedKey, UnrelatedTaggedKey, (k, _) => ValueTask.FromResult(k), tags: [_unrelatedTag]);
        }

        internal async Task<bool> IsStillCachedAsync(string key)
        {
            var recreated = false;
            _ = await _cache.GetOrCreateAsync(key, 0, (_, _) =>
            {
                recreated = true;
                return ValueTask.FromResult("recreated");
            });

            return !recreated;
        }

        internal List<WorkItem> DrainChannel()
        {
            var items = new List<WorkItem>();
            while (ResyncChannel.Reader.TryRead(out var item))
            {
                items.Add(item);
            }

            return items;
        }

        private static IUserorgService CreateUserorgService()
        {
            var mock = new Mock<IUserorgService>(MockBehavior.Strict);

            mock.Setup(m => m.GetProviderLocationsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<IList<ProviderLocationEntry>, Error>.Success(
                [
                    new ProviderLocationEntry(_alphaLocationA, "loc-a", _alphaProviderId, "alpha"),
                    new ProviderLocationEntry(_alphaLocationB, "loc-b", _alphaProviderId, "alpha"),
                    new ProviderLocationEntry(_duplicateLocationOne, "dup", _alphaProviderId, "alpha"),
                    new ProviderLocationEntry(_duplicateLocationTwo, "dup", _alphaProviderId, "alpha"),
                    new ProviderLocationEntry(_betaLocation, "loc-c", _betaProviderId, "beta")
                ], State.Ok));

            mock.Setup(m => m.ResolveProviderIdByNameAsync("alpha", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid, Error>.Success(_alphaProviderId, State.Ok));
            mock.Setup(m => m.ResolveProviderIdByNameAsync("beta", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid, Error>.Success(_betaProviderId, State.Ok));
            mock.Setup(m => m.ResolveProviderIdByNameAsync("ambiguous", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid, Error>.Failure(TestError("ambiguous provider name"), State.CriticalError));

            mock.Setup(m => m.GetLocationIdsForProviderAsync(_alphaProviderId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<IList<Guid>, Error>.Success(
                    [_alphaLocationA, _alphaLocationB, _duplicateLocationOne, _duplicateLocationTwo], State.Ok));
            mock.Setup(m => m.GetLocationIdsForProviderAsync(_betaProviderId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<IList<Guid>, Error>.Success([_betaLocation], State.Ok));

            // B02's batch contract: a pair that does not resolve is OMITTED from the result, never a failure.
            mock.Setup(m => m.ResolveProjectResourceIdsAsync(It.IsAny<IReadOnlyCollection<ProjectResourceNamePair>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<ProjectResourceNamePair> pairs, CancellationToken _) =>
                    Result<IList<ProjectResourceNameEntry>, Error>.Success(
                        [.. pairs.Select(Resolve).OfType<ProjectResourceNameEntry>()],
                        State.Ok));

            mock.Setup(m => m.ResolveUserIdByIdentityProviderIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid, Error>.Success(_callerUserId, State.Ok));

            mock.Setup(m => m.ResolveProjectResourceNamesAsync(It.IsAny<IReadOnlyCollection<ProjectResourceIdEntry>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<ProjectResourceIdEntry> ids, CancellationToken _) =>
                    Result<IList<ProjectResourceNameEntry>, Error>.Success(
                        ids.Select(id => new ProjectResourceNameEntry(
                            id.ProjectId,
                            id.ProjectResourceId,
                            "proj-1",
                            id.ProjectResourceId == _resourceOne ? "res-1" : "res-2")).ToList(),
                        State.Ok));

            return mock.Object;
        }

        private static ProjectResourceNameEntry? Resolve(ProjectResourceNamePair pair)
        {
            if (!string.Equals(pair.ProjectShortName, "proj-1", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return pair.ResourceName.ToUpperInvariant() switch
            {
                "RES-1" => new ProjectResourceNameEntry(_projectOne, _resourceOne, "proj-1", "res-1"),
                "RES-2" => new ProjectResourceNameEntry(_projectOne, _resourceTwo, "proj-1", "res-2"),
                _ => null
            };
        }

        private static Error TestError(string description) => new ErrorBuilder()
            .WithDescriptionText(description)
            .WithSeverity(ErrorSeverity.Error)
            .Build();

        private static HeappeIntegrationDbContext CreateSeededDbContext()
        {
            var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
                .UseInMemoryDatabase($"KillSwitchAdmin_{Guid.NewGuid()}")
                .Options;

            var dbContext = new HeappeIntegrationDbContext(options);

            // loc-a: two templates on res-1 (one later whitelisted) plus one on res-2 → two resync pairs.
            AddTemplateOnLocation(dbContext, _alphaLocationA, "frozen-template", _resourceOne);
            AddTemplateOnLocation(dbContext, _alphaLocationA, "kept-alive", _resourceOne);
            AddTemplateOnLocation(dbContext, _alphaLocationA, "other-resource-template", _resourceTwo);
            AddTemplateOnLocation(dbContext, _betaLocation, "beta-template", _resourceOne);

            dbContext.SaveChanges();
            return dbContext;
        }

        private static void AddTemplateOnLocation(HeappeIntegrationDbContext dbContext, Guid locationId, string templateName, Guid resourceId)
        {
            var template = ProjectCommandTemplate.Create(templateName, ECommandTemplateType.Unknown, _projectOne, resourceId);
            dbContext.Set<ProjectCommandTemplate>().Add(template);

            var locationPartition = new LocationPartition
            {
                Id = Guid.NewGuid(),
                Name = $"{templateName}-queue",
                LocationId = locationId
            };
            dbContext.Set<LocationPartition>().Add(locationPartition);

            dbContext.Set<ProjectCommandTemplatePartition>().Add(new ProjectCommandTemplatePartition
            {
                Id = Guid.NewGuid(),
                State = ECommandTemplateState.Active,
                LocationPartitionId = locationPartition.Id,
                LocationPartition = locationPartition,
                ProjectCommandTemplate = template
            });
        }
    }
}
