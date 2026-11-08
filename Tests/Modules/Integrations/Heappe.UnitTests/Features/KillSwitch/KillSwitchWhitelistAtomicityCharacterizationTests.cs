using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Heappe.Contracts.Entries;
using Heappe.Features.BackgroundSync;
using Heappe.Features.KillSwitch;
using Heappe.Features.KillSwitch.Contract;
using Heappe.HostContracts;
using Heappe.Persistence;
using Heappe.Persistence.Entities;
using Heappe.Persistence.Enums;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using UserOrg.Shared;

using Xunit;

namespace Heappe.UnitTests.Features.KillSwitch;

/// <summary>
/// CHARACTERIZATION tests (#258 safety net) pinning the ATOMICITY of whitelist name-&gt;id resolution, PORTED
/// from <c>SetLocationKillSwitchRequest.Whitelist</c> — hard-cut by B04 (ADR-0013 §2) — onto the standing
/// whitelist PUT that replaced it.
/// </summary>
/// <remarks>
/// <para>
/// The invariant under protection is a FAIL-CLOSED security control: the resolution refuses the WHOLE request
/// the moment any item is unresolvable, BEFORE a single row is written, and the 400's message promises "no
/// changes were applied". B02 replaced the per-item loop with a batch Core call whose contract is PARTIAL
/// SUCCESS ("pairs that don't resolve are omitted from the result"), so this path diffs the returned set
/// against the request to reconstruct the 400. If that diff is forgotten, an unknown item is silently DROPPED
/// and the PUT succeeds with a partially-applied whitelist — a fail-OPEN regression.
/// </para>
/// <para>
/// The second hazard is the replace path being delete-then-insert: a rejection reached AFTER the delete would
/// CLEAR the standing whitelist while reporting 400 — the exact opposite of the promise. Under ADR-0013 the
/// whitelist is STANDING configuration that survives lift, so this hazard is strictly worse than it was: the
/// cleared rows are no longer re-supplied by the next enforce. These tests therefore assert on a PRE-EXISTING
/// whitelist surviving a rejected PUT, not merely on an empty table.
/// </para>
/// </remarks>
public sealed class KillSwitchWhitelistAtomicityCharacterizationTests
{
    private static readonly Guid _alphaProviderId = Guid.NewGuid();
    private static readonly Guid _alphaLocationA = Guid.NewGuid();
    private static readonly Guid _alphaLocationB = Guid.NewGuid();

    private static readonly Guid _projectOne = Guid.NewGuid();
    private static readonly Guid _resourceOne = Guid.NewGuid();
    private static readonly Guid _resourceTwo = Guid.NewGuid();
    private static readonly Guid _callerUserId = Guid.NewGuid();

    private const string _originalReason = "Suspected RCE in the job submission path";

    private static CallerInfo PlatformAdmin() => new("idp-platform-admin", null, IsPlatformAdmin: true);

    // Items that DO resolve on loc-a.
    private static KillSwitchWhitelistItem KeptAlive() => new("proj-1", "res-1", "kept-alive");
    private static KillSwitchWhitelistItem OtherResource() => new("proj-1", "res-2", "other-resource-template");

    // Item whose (project, resource) pair resolves, but whose template name does not exist on loc-a.
    private static KillSwitchWhitelistItem UnknownTemplate() => new("proj-1", "res-1", "does-not-exist");

    // Item whose (project, resource) pair itself is unresolvable by the Core adapter — B02's batch omits it
    // from the result rather than failing, so only the diff turns it back into a 400.
    private static KillSwitchWhitelistItem UnresolvablePair() => new("ghost-project", "res-1", "kept-alive");

    [Fact(DisplayName = "#258 A rejected whitelist PUT does not clear the whitelist an EARLIER accepted PUT wrote")]
    public async Task Rejected_put_does_not_clear_an_existing_whitelist()
    {
        var world = new World();

        // An earlier, accepted whitelist PUT: "kept-alive" is exempt and must keep running.
        var accepted = await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist(KeptAlive()), CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, StatusCodeOf(accepted));
        var whitelistedTemplateId = Assert.Single(world.DbContext.LocationCommandTemplateWhitelists).ProjectCommandTemplateId;

        // A later PUT mixing a KNOWN item with an UNKNOWN one must be refused in full.
        var rejected = await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist(KeptAlive(), UnknownTemplate()), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(rejected));

        // The standing whitelist must be BYTE-FOR-BYTE what the accepted PUT left behind: a delete-then-insert
        // replace that rejects mid-flight would leave this table empty and silently freeze "kept-alive".
        var surviving = Assert.Single(world.DbContext.LocationCommandTemplateWhitelists);
        Assert.Equal(_alphaLocationA, surviving.LocationId);
        Assert.Equal(whitelistedTemplateId, surviving.ProjectCommandTemplateId);
    }

    [Fact(DisplayName = "#258 A rejected whitelist PUT leaves the kill-switch row exactly as the enforce left it")]
    public async Task Rejected_put_leaves_the_kill_switch_row_untouched()
    {
        var world = new World();
        await world.Handler.SetLocationAsync(
            PlatformAdmin(), "alpha", "loc-a", Enforce(_originalReason), CancellationToken.None);
        await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist(KeptAlive()), CancellationToken.None);
        var before = Assert.Single(world.DbContext.LocationCommandTemplateKillSwitches);
        var enforcedAtBefore = before.EnforcedAtUtc;

        var rejected = await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist(UnknownTemplate()), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(rejected));

        var after = Assert.Single(world.DbContext.LocationCommandTemplateKillSwitches);
        Assert.True(after.IsEnforced);
        Assert.Equal(_originalReason, after.Reason);
        Assert.Equal(enforcedAtBefore, after.EnforcedAtUtc);
        Assert.Null(after.LiftedAtUtc);
    }

    [Fact(DisplayName = "#258 A rejected whitelist PUT keeps the whitelisted template EFFECTIVELY runnable (kill rule unchanged)")]
    public async Task Rejected_put_keeps_the_whitelisted_template_runnable()
    {
        var world = new World();
        await world.Handler.SetLocationAsync(
            PlatformAdmin(), "alpha", "loc-a", Enforce(_originalReason), CancellationToken.None);
        await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist(KeptAlive()), CancellationToken.None);

        await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist(KeptAlive(), UnknownTemplate()), CancellationToken.None);

        // Asserted through the REAL KillSwitchStateService kill rule, not the row alone: the exemption must
        // still be in force after the refusal.
        var detail = ValueOf<LocationKillSwitchDetail>(
            await world.Handler.GetLocationAsync(PlatformAdmin(), "alpha", "loc-a", CancellationToken.None));

        var keptAlive = detail.Templates.Single(t => t.TemplateName == "kept-alive");
        Assert.True(keptAlive.Whitelisted);
        Assert.Equal(nameof(ECommandTemplateState.Active), keptAlive.EffectiveState);
    }

    // ---------- order independence: fail-closed cannot pass by luck ----------
    //
    // A partial-success batch that forgets the diff would drop the unknown item wherever it sits. Pinning
    // BOTH ends stops a regression from hiding behind an accidentally-favourable item order.

    [Theory(DisplayName = "#258 One unknown item rejects the whole whitelist PUT wherever it sits in the list")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Unknown_item_rejects_the_whole_put_at_any_position(int unknownIndex)
    {
        var world = new World();
        var items = new List<KillSwitchWhitelistItem> { KeptAlive(), OtherResource() };
        items.Insert(unknownIndex, UnknownTemplate());

        var result = await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a", Whitelist([.. items]), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
        Assert.Empty(world.DbContext.LocationCommandTemplateWhitelists);
    }

    [Fact(DisplayName = "#258 An unresolvable (project, resource) pair mixed with VALID items writes nothing")]
    public async Task Unresolvable_pair_mixed_with_valid_items_writes_nothing()
    {
        var world = new World();

        var result = await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a",
            Whitelist(KeptAlive(), UnresolvablePair(), OtherResource()),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
        Assert.Empty(world.DbContext.LocationCommandTemplateWhitelists);
    }

    [Fact(DisplayName = "#258 A template that exists but NOT on the target location rejects the whole PUT")]
    public async Task Template_from_another_location_mixed_with_valid_items_writes_nothing()
    {
        var world = new World();

        // "kept-alive" is real, but it lives on loc-a — whitelisting it on loc-b must not silently succeed
        // with just the loc-b item applied.
        var result = await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-b",
            Whitelist(new KillSwitchWhitelistItem("proj-1", "res-1", "loc-b-template"), KeptAlive()),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
        Assert.Empty(world.DbContext.LocationCommandTemplateWhitelists);
    }

    [Theory(DisplayName = "#258 The template name matches case-insensitively and is NOT a 400")]
    [InlineData("kept-alive")]
    [InlineData("KEPT-ALIVE")]
    [InlineData("Kept-Alive")]
    public async Task Template_name_matches_case_insensitively(string templateName)
    {
        var world = new World();

        // B02 re-keys this match into a lookup dictionary; an Ordinal default Dictionary would compile, pass
        // every other test, and turn a resolvable item into a production 400.
        var result = await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a",
            Whitelist(new KillSwitchWhitelistItem("proj-1", "res-1", templateName)),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusCodeOf(result));
        Assert.Single(world.DbContext.LocationCommandTemplateWhitelists);
    }

    [Fact(DisplayName = "#258 The same item listed twice writes ONE whitelist row, not two")]
    public async Task Duplicate_items_collapse_to_one_row()
    {
        var world = new World();

        var result = await world.Handler.SetLocationWhitelistAsync(
            PlatformAdmin(), "alpha", "loc-a",
            Whitelist(KeptAlive(), new KillSwitchWhitelistItem("proj-1", "res-1", "KEPT-ALIVE")),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusCodeOf(result));
        Assert.Single(world.DbContext.LocationCommandTemplateWhitelists);
    }

    private static SetLocationWhitelistRequest Whitelist(params KillSwitchWhitelistItem[] items) => new(items);

    private static SetLocationKillSwitchRequest Enforce(string reason)
        => new(IsEnforced: true, reason, IgnoreWhitelist: false);

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

        internal World()
        {
            DbContext = CreateSeededDbContext();

            Handler = new KillSwitchAdminHandler(
                DbContext,
                CreateUserorgService(),
                new KillSwitchStateService(DbContext),
                new FakeHybridCache(),
                Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.Wait }),
                NullLogger<KillSwitchAdminHandler>.Instance);
        }

        private static IUserorgService CreateUserorgService()
        {
            var mock = new Mock<IUserorgService>(MockBehavior.Strict);

            mock.Setup(m => m.GetProviderLocationsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<IList<ProviderLocationEntry>, Error>.Success(
                [
                    new ProviderLocationEntry(_alphaLocationA, "loc-a", _alphaProviderId, "alpha"),
                    new ProviderLocationEntry(_alphaLocationB, "loc-b", _alphaProviderId, "alpha")
                ], State.Ok));

            mock.Setup(m => m.ResolveProviderIdByNameAsync("alpha", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid, Error>.Success(_alphaProviderId, State.Ok));

            // Models B02's batch contract: an unresolvable pair is OMITTED from the result, never a failure —
            // so only the caller's diff can turn "ghost-project" back into the 400 these tests demand.
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
                        [.. ids.Select(id => new ProjectResourceNameEntry(
                            id.ProjectId,
                            id.ProjectResourceId,
                            "proj-1",
                            id.ProjectResourceId == _resourceOne ? "res-1" : "res-2"))],
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

        private static HeappeIntegrationDbContext CreateSeededDbContext()
        {
            var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
                .UseInMemoryDatabase($"KillSwitchWhitelistAtomicity_{Guid.NewGuid()}")
                .Options;

            var dbContext = new HeappeIntegrationDbContext(options);

            AddTemplateOnLocation(dbContext, _alphaLocationA, "kept-alive", _resourceOne);
            AddTemplateOnLocation(dbContext, _alphaLocationA, "frozen-template", _resourceOne);
            AddTemplateOnLocation(dbContext, _alphaLocationA, "other-resource-template", _resourceTwo);
            AddTemplateOnLocation(dbContext, _alphaLocationB, "loc-b-template", _resourceOne);

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
