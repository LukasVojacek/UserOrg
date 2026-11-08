using Core.Features.Cache.ResetAllCaches;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Cache;

/// <summary>
/// Contract tests for the admin-only output-cache flush (F2 C2): 204 + evicts the global tag for an admin,
/// 403 + no eviction for a non-admin.
/// </summary>
public sealed class ResetAllCachesEndpointTests
{
    private static Caller AdminCaller() => new(
        new IdpId("admin"), "Admin", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller NonAdminCaller() => new(
        new IdpId("u"), "User", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller MixedCaseAdminCaller() => new(
        new IdpId("admin"), "aDmIn", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static void AssertRoleDiffersFromConfiguredAdminOnlyInCase(Caller caller)
    {
        var configured = Roles().Value.DefaultAdminRoleName;
        Assert.NotEqual(configured, caller.Role);
        Assert.Equal(configured, caller.Role, ignoreCase: true);
    }

    private static IOptions<WellKnownRoles> Roles() => Options.Create(new WellKnownRoles());

    private static int? StatusOf(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    [Fact]
    public async Task Should_ReturnNoContentAndEvictAll_When_Admin()
    {
        var invalidator = new Mock<IOutputCacheInvalidator>();

        var result = await ResetAllCachesEndpoint.HandleAsync(
            AdminCaller(), Roles(), invalidator.Object,
            new RecordingAuditLogger<ResetAllCachesEndpoint.AuditCategory>(), NullLogger<ResetAllCachesEndpoint.AuditCategory>.Instance, CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        invalidator.Verify(i => i.EvictAsync(CacheTags.All, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnNoContentAndEvictAll_When_CallerRoleMatchesAdminRoleOnlyIgnoringCase()
    {
        var caller = MixedCaseAdminCaller();
        AssertRoleDiffersFromConfiguredAdminOnlyInCase(caller);
        var invalidator = new Mock<IOutputCacheInvalidator>();

        var result = await ResetAllCachesEndpoint.HandleAsync(
            caller, Roles(), invalidator.Object,
            new RecordingAuditLogger<ResetAllCachesEndpoint.AuditCategory>(), NullLogger<ResetAllCachesEndpoint.AuditCategory>.Instance, CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        invalidator.Verify(i => i.EvictAsync(CacheTags.All, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnForbiddenAndNotEvict_When_NotAdmin()
    {
        var invalidator = new Mock<IOutputCacheInvalidator>();

        var result = await ResetAllCachesEndpoint.HandleAsync(
            NonAdminCaller(), Roles(), invalidator.Object,
            new RecordingAuditLogger<ResetAllCachesEndpoint.AuditCategory>(), NullLogger<ResetAllCachesEndpoint.AuditCategory>.Instance, CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        invalidator.Verify(i => i.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
