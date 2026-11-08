using Core.Features.User.ResetToPlatformUser;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using Moq;

using DomainUser = Core.Domain.User.User;

namespace Core.UnitTests.Users;

/// <summary>
/// Contract tests for the admin-only reset endpoint (F3): 204 + platform User role (provider unassigned first)
/// for an admin, 403 for non-admins, 404 when the user is missing, 409 when the target is an administrator,
/// and an idempotent 204 when the user is already a platform user.
/// </summary>
public sealed class ResetProviderAdminEndpointTests
{
    private static readonly IReadOnlyDictionary<int, string> RoleMap = new Dictionary<int, string>
    {
        { 1, "User" }, { 2, "Admin" }, { 7, "ProviderAdmin" }
    };

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

    private static DomainUser HydratedUser(string roleName = "User")
    {
        var user = DomainUser.CreateUser(Email.Create("test@example.com"), UserName.Create("testuser"), IdpId.Create("idp-test-123"));
        user.PromoteUserToRole(ProjectRoleId.Create(RoleMap.Single(kv => kv.Value == roleName).Key));
        user.InitializeRoleList(RoleMap, new WellKnownRoles());
        user.ClearDomainEvents();
        return user;
    }

    // A ProviderAdmin user with one assigned provider — the post-promote state the reset must revert.
    private static DomainUser ProviderAdminUser()
    {
        var user = HydratedUser("User");
        var promoted = user.PromoteToProviderAdmin(new HashSet<ProviderId> { ProviderId.New() });
        Assert.True(promoted.IsSuccess);
        user.ClearDomainEvents();
        return user;
    }

    private static Task<IResult> InvokeAsync(Caller caller, DomainUser? user, Mock<IUnitOfWork>? unitOfWork = null)
    {
        var users = new Mock<IRepository<DomainUser, IdpId>>();
        users.Setup(r => r.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(user));

        unitOfWork ??= new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return ResetProviderAdminEndpoint.HandleAsync(
            new ResetProviderAdminEndpoint.Request("idp-t"),
            caller, users.Object, Roles(), unitOfWork.Object,
            new RecordingAuditLogger<ResetProviderAdminEndpoint.AuditCategory>(), CancellationToken.None);
    }

    [Fact]
    public async Task Should_ReturnNoContentAndReset_When_Admin()
    {
        var user = ProviderAdminUser();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await InvokeAsync(AdminCaller(), user, unitOfWork);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.Equal("User", user.SystemRoleName);
        Assert.False(user.HasAssignedProvider);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnNoContentAndReset_When_CallerRoleMatchesAdminRoleOnlyIgnoringCase()
    {
        var caller = MixedCaseAdminCaller();
        AssertRoleDiffersFromConfiguredAdminOnlyInCase(caller);
        var user = ProviderAdminUser();

        var result = await InvokeAsync(caller, user);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.Equal("User", user.SystemRoleName);
        Assert.False(user.HasAssignedProvider);
    }

    [Fact]
    public async Task Should_ReturnForbidden_When_NotAdmin()
    {
        var result = await InvokeAsync(NonAdminCaller(), ProviderAdminUser());

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_UserMissing()
    {
        var result = await InvokeAsync(AdminCaller(), user: null);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnConflict_When_TargetIsAdmin()
    {
        var result = await InvokeAsync(AdminCaller(), HydratedUser("Admin"));

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNoContent_When_AlreadyPlatformUser()
    {
        var user = HydratedUser("User");

        var result = await InvokeAsync(AdminCaller(), user);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.Equal("User", user.SystemRoleName);
        Assert.False(user.HasAssignedProvider);
    }
}
