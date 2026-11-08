using Core.DataAccess.Queries.Contracts;
using Core.Features.User.PromoteToProviderAdmin;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

using DomainUser = Core.Domain.User.User;

namespace Core.UnitTests.Users;

/// <summary>
/// Contract tests for the admin-only promote endpoint (F3): 204 + ProviderAdmin role for an admin caller,
/// 400 for invalid provider name, 403 for non-admins, 404 when the user or provider is missing,
/// 409 when the target is an administrator.
/// </summary>
public sealed class PromoteToProviderAdminEndpointTests
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

    // Mirrors the repository hydration path so role-dependent operations work.
    private static DomainUser HydratedUser(string roleName = "User")
    {
        var user = DomainUser.CreateUser(Email.Create("test@example.com"), UserName.Create("testuser"), IdpId.Create("idp-test-123"));
        user.PromoteUserToRole(ProjectRoleId.Create(RoleMap.Single(kv => kv.Value == roleName).Key));
        user.InitializeRoleList(RoleMap, new WellKnownRoles());
        user.ClearDomainEvents();
        return user;
    }

    private static Task<IResult> InvokeAsync(
        Caller caller,
        DomainUser? user,
        ProviderId? providerId,
        Mock<IUnitOfWork>? unitOfWork = null,
        string providerName = "hpc")
    {
        var users = new Mock<IRepository<DomainUser, IdpId>>();
        users.Setup(r => r.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(user));

        var providers = new Mock<IProviderQuery>();
        providers.Setup(q => q.FindActiveProviderIdByNameAsync(It.IsAny<Domain.Provider.ProviderName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(providerId);

        unitOfWork ??= new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return PromoteToProviderAdminEndpoint.HandleAsync(
            new PromoteToProviderAdminEndpoint.Request("idp-t", providerName),
            caller, users.Object, providers.Object, Roles(), unitOfWork.Object,
            new RecordingAuditLogger<PromoteToProviderAdminEndpoint.AuditCategory>(), CancellationToken.None);
    }

    [Fact]
    public async Task Should_ReturnNoContentAndPromote_When_Admin()
    {
        var user = HydratedUser("User");
        var providerId = ProviderId.New();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await InvokeAsync(AdminCaller(), user, providerId, unitOfWork);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.Equal("ProviderAdmin", user.SystemRoleName);
        Assert.Contains(providerId, user.AssignedProviderIds);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnBadRequestWithInvalidNameCode_When_ProviderNameIsBlank()
    {
        var result = await InvokeAsync(AdminCaller(), HydratedUser("User"), providerId: ProviderId.New(), providerName: " ");

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal("provider.invalid_name", problem.ProblemDetails.Extensions["code"]);
        Assert.False(string.IsNullOrWhiteSpace(problem.ProblemDetails.Detail));
    }

    [Fact]
    public async Task Should_ReturnNoContentAndPromote_When_CallerRoleMatchesAdminRoleOnlyIgnoringCase()
    {
        var caller = MixedCaseAdminCaller();
        AssertRoleDiffersFromConfiguredAdminOnlyInCase(caller);
        var user = HydratedUser("User");
        var providerId = ProviderId.New();

        var result = await InvokeAsync(caller, user, providerId);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.Equal("ProviderAdmin", user.SystemRoleName);
        Assert.Contains(providerId, user.AssignedProviderIds);
    }

    [Fact]
    public async Task Should_ReturnForbidden_When_NotAdmin()
    {
        var result = await InvokeAsync(NonAdminCaller(), HydratedUser("User"), ProviderId.New());

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
    }

    [Fact]
    public async Task Should_Return403NotBadRequest_When_NonAdminSendsBlankProviderName()
    {
        // Pins the auth-before-validation order: the 403 role check (line 36) must win over the 400
        // provider-name check (line 43) even when both conditions are true, so an unauthorized caller
        // can never learn — via a 400 — that their malformed input reached validation.
        var result = await InvokeAsync(NonAdminCaller(), HydratedUser("User"), ProviderId.New(), providerName: " ");

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_UserMissing()
    {
        var result = await InvokeAsync(AdminCaller(), user: null, providerId: ProviderId.New());

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_ProviderMissing()
    {
        var result = await InvokeAsync(AdminCaller(), HydratedUser("User"), providerId: null);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnConflict_When_TargetIsAdmin()
    {
        var result = await InvokeAsync(AdminCaller(), HydratedUser("Admin"), ProviderId.New());

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnBadGateway_When_ProviderAdminRoleNotResolvable()
    {
        // Gateway RbacSeed race: role map hydrated before ProviderAdmin was seeded.
        var incompleteRoleMap = new Dictionary<int, string> { { 1, "User" }, { 2, "Admin" } };
        var user = DomainUser.CreateUser(Email.Create("test@example.com"), UserName.Create("testuser"), IdpId.Create("idp-test-123"));
        user.PromoteUserToRole(ProjectRoleId.Create(1));
        user.InitializeRoleList(incompleteRoleMap, new WellKnownRoles());
        user.ClearDomainEvents();

        var result = await InvokeAsync(AdminCaller(), user, ProviderId.New());

        Assert.Equal(StatusCodes.Status502BadGateway, StatusOf(result));
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(UserErrorCodes.RoleNotResolved, problem.ProblemDetails.Extensions["code"]);
    }
}
