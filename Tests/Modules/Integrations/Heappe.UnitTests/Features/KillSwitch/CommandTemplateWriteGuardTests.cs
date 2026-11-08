using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Heappe.Features.KillSwitch;
using Heappe.Persistence;
using Heappe.Persistence.Entities;

using Microsoft.EntityFrameworkCore;

using Moq;

using Xunit;

namespace Heappe.UnitTests.Features.KillSwitch;

/// <summary>
/// Pins the B07 write-guard decision (ADR-0077 §5): Activate/Deactivate/Create reject a frozen
/// Location with a 409-mapped error, at LOCATION grain — the whitelist is ignored (it governs
/// runnability on the read paths, not writability). <c>reason</c> never appears in the message.
/// </summary>
public sealed class CommandTemplateWriteGuardTests
{
    [Fact(DisplayName = "No frozen location among the input -> null (write allowed)")]
    public async Task Returns_null_When_no_location_is_frozen()
    {
        var locationId = Guid.NewGuid();
        using var dbContext = CreateDbContext();

        var guard = new CommandTemplateWriteGuard(new KillSwitchStateService(dbContext), dbContext);

        var error = await guard.RejectIfFrozenAsync(
            new Dictionary<Guid, string> { [locationId] = "Karolina" },
            CancellationToken.None);

        Assert.Null(error);
    }

    [Fact(DisplayName = "Empty input -> null without querying the database")]
    public async Task Returns_null_When_input_is_empty()
    {
        using var dbContext = CreateDbContext();
        var guard = new CommandTemplateWriteGuard(new KillSwitchStateService(dbContext), dbContext);

        var error = await guard.RejectIfFrozenAsync(
            new Dictionary<Guid, string>(),
            CancellationToken.None);

        Assert.Null(error);
    }

    [Fact(DisplayName = "Frozen location -> 409-coded error naming the location and the enforced-at timestamp, without reason")]
    public async Task Returns_409_error_When_location_is_frozen()
    {
        var locationId = Guid.NewGuid();
        var enforcedAt = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        using var dbContext = CreateDbContext();
        AddKillSwitch(dbContext, locationId, enforcedAt, reason: "Suspected RCE in BulkCopy");
        await dbContext.SaveChangesAsync();

        var guard = new CommandTemplateWriteGuard(new KillSwitchStateService(dbContext), dbContext);

        var error = await guard.RejectIfFrozenAsync(
            new Dictionary<Guid, string> { [locationId] = "Karolina" },
            CancellationToken.None);

        Assert.NotNull(error);
        Assert.Equal(KillSwitchErrorCodes.LocationFrozen, error!.Code);
        Assert.Contains("Karolina", error.Description);
        Assert.Contains(enforcedAt.ToString(), error.Description);
        Assert.DoesNotContain("RCE", error.Description);
        Assert.DoesNotContain("BulkCopy", error.Description);
    }

    [Fact(DisplayName = "Frozen location with a whitelisted template still rejects -- whitelist governs runnability, not writability")]
    public async Task Still_rejects_When_template_is_whitelisted()
    {
        var locationId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        using var dbContext = CreateDbContext();
        AddKillSwitch(dbContext, locationId, DateTime.UtcNow, reason: "Suspected RCE in BulkCopy");
        dbContext.LocationCommandTemplateWhitelists.Add(new LocationCommandTemplateWhitelist
        {
            LocationId = locationId,
            ProjectCommandTemplateId = templateId
        });
        await dbContext.SaveChangesAsync();

        var guard = new CommandTemplateWriteGuard(new KillSwitchStateService(dbContext), dbContext);

        var error = await guard.RejectIfFrozenAsync(
            new Dictionary<Guid, string> { [locationId] = "Karolina" },
            CancellationToken.None);

        Assert.NotNull(error);
        Assert.Equal(KillSwitchErrorCodes.LocationFrozen, error!.Code);
    }

    [Fact(DisplayName = "F2: B02 reports the location frozen, but by re-read time the kill row has been lifted (IsEnforced=false) -> write PROCEEDS, no 409")]
    public async Task Proceeds_When_KillSwitchServiceSaysFrozen_But_RowIsNoLongerEnforced()
    {
        var locationId = Guid.NewGuid();
        using var dbContext = CreateDbContext();
        // B09 lifts by flipping IsEnforced to false, not deleting the row -- the race this pins.
        AddKillSwitch(dbContext, locationId, DateTime.UtcNow, reason: "lifted after the incident closed", isEnforced: false);
        await dbContext.SaveChangesAsync();

        var staleKillSwitchState = CreateStaleFrozenReport(locationId);
        var guard = new CommandTemplateWriteGuard(staleKillSwitchState, dbContext);

        var error = await guard.RejectIfFrozenAsync(
            new Dictionary<Guid, string> { [locationId] = "Karolina" },
            CancellationToken.None);

        Assert.Null(error);
    }

    [Fact(DisplayName = "F2: B02 reports the location frozen, but by re-read time the kill row is gone -> write PROCEEDS, no 500")]
    public async Task Proceeds_When_KillSwitchServiceSaysFrozen_But_RowIsGone()
    {
        var locationId = Guid.NewGuid();
        using var dbContext = CreateDbContext(); // no kill-switch row at all for this location

        var staleKillSwitchState = CreateStaleFrozenReport(locationId);
        var guard = new CommandTemplateWriteGuard(staleKillSwitchState, dbContext);

        var error = await guard.RejectIfFrozenAsync(
            new Dictionary<Guid, string> { [locationId] = "Karolina" },
            CancellationToken.None);

        Assert.Null(error);
    }

    /// <summary>
    /// A stale <see cref="IKillSwitchStateService"/> that still reports <paramref name="locationId"/>
    /// as frozen, simulating a lift landing between B02's read and the guard's own re-read.
    /// </summary>
    private static IKillSwitchStateService CreateStaleFrozenReport(Guid locationId)
    {
        var mock = new Mock<IKillSwitchStateService>(MockBehavior.Strict);
        mock.Setup(s => s.GetEnforcedKilledLocationIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { locationId });
        return mock.Object;
    }

    private static void AddKillSwitch(HeappeIntegrationDbContext dbContext, Guid locationId, DateTime enforcedAtUtc, string reason, bool isEnforced = true)
        => dbContext.LocationCommandTemplateKillSwitches.Add(new LocationCommandTemplateKillSwitch
        {
            LocationId = locationId,
            IsEnforced = isEnforced,
            Reason = reason,
            EnforcedAtUtc = enforcedAtUtc,
            EnforcedByUserId = Guid.NewGuid()
        });

    private static HeappeIntegrationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase($"WriteGuard_{Guid.NewGuid()}")
            .Options;

        return new HeappeIntegrationDbContext(options);
    }
}
