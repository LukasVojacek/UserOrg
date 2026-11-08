using Core.DataAccess.Queries.Contracts;
using Core.Features.UserInfo.GetExtendedUserInfo;
using Core.Features.UserInfo.UseCases;
using Core.UnitTests.Shared;

using Forge.Core.Features.Permission.Services;
using Forge.Core.Features.Role.Services;

using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using UserOrg.Shared;

namespace Core.UnitTests.UserInfo;

public sealed class GetExtendedUserInfoEndpointTests
{
    private static readonly IdpId Sub = new("user-sub-abc");
    private static readonly Email TokenEmail = Email.Create("caller@example.com");
    private static readonly UserName TokenUserName = UserName.Create("callername");

    private static readonly UserExtendedDetails SampleUser = new(
        IdentityProviderId: Sub,
        Email: TokenEmail,
        UserName: TokenUserName,
        ProjectResources: []);

    private static Mock<IRoleListService> RoleListServiceReturning()
    {
        var roleListService = new Mock<IRoleListService>();
        roleListService.Setup(s => s.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Lukdrasil.Error.Error>.Success(
                [new RoleInfo(1, "User", "", "default", [new PermissionInfo(new PermissionKey("perm:read"), "perm:read")], true, 1)]));
        return roleListService;
    }

    private static Mock<IUserInfoQuery> QueryReturning(UserExtendedDetails user)
    {
        var userQuery = new Mock<IUserInfoQuery>();
        userQuery.Setup(q => q.GetExtendedBySubAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        return userQuery;
    }

    private static Mock<IRepository<Domain.User.User, IdpId>> RepositoryReturning(Domain.User.User? existing)
    {
        var users = new Mock<IRepository<Domain.User.User, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(existing));
        users.Setup(u => u.AddAsync(It.IsAny<Domain.User.User>(), It.IsAny<CancellationToken>()))
            .Returns((Domain.User.User user, CancellationToken _) => ValueTask.FromResult<Domain.User.User?>(user));
        return users;
    }

    private static Mock<IUnitOfWork> SavingUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    [Fact]
    public async Task Should_CreateUser_When_SubUnknown_AndReturnOk()
    {
        var caller = new Caller(
            Sub, "User", "Display Name", new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
            Email: TokenEmail, UserName: TokenUserName);

        var userQuery = QueryReturning(SampleUser);
        var roleListService = RoleListServiceReturning();
        var users = RepositoryReturning(existing: null);
        var unitOfWork = SavingUnitOfWork();

        var result = await GetExtendedUserInfoEndpoint.HandleAsync(
            caller, userQuery.Object, roleListService.Object, users.Object, unitOfWork.Object,
            new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None);

        var ok = Assert.IsType<Ok<UserInfoExtendedResponse>>(result);
        Assert.Equal(Sub.Value, ok.Value!.IdentityProviderUserId);
        users.Verify(u => u.AddAsync(It.IsAny<Domain.User.User>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnCallerNotProvisioned_When_TokenEmailMissing()
    {
        var caller = new Caller(
            Sub, "User", "Display Name", new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
            Email: null, UserName: TokenUserName);

        var userQuery = QueryReturning(SampleUser);
        var roleListService = RoleListServiceReturning();
        var users = RepositoryReturning(existing: null);
        var unitOfWork = SavingUnitOfWork();

        var result = await GetExtendedUserInfoEndpoint.HandleAsync(
            caller, userQuery.Object, roleListService.Object, users.Object, unitOfWork.Object,
            new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("caller.not_provisioned", problem.ProblemDetails.Extensions["code"]);
        users.Verify(u => u.AddAsync(It.IsAny<Domain.User.User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnCallerNotProvisioned_When_TokenUserNameMissing()
    {
        var caller = new Caller(
            Sub, "User", "Display Name", new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
            Email: TokenEmail, UserName: null);

        var userQuery = QueryReturning(SampleUser);
        var roleListService = RoleListServiceReturning();
        var users = RepositoryReturning(existing: null);
        var unitOfWork = SavingUnitOfWork();

        var result = await GetExtendedUserInfoEndpoint.HandleAsync(
            caller, userQuery.Object, roleListService.Object, users.Object, unitOfWork.Object,
            new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("caller.not_provisioned", problem.ProblemDetails.Extensions["code"]);
        users.Verify(u => u.AddAsync(It.IsAny<Domain.User.User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_NotDuplicateUser_When_AlreadyProvisioned()
    {
        var caller = new Caller(
            Sub, "User", "Display Name", new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
            Email: TokenEmail, UserName: TokenUserName);

        var userQuery = QueryReturning(SampleUser);
        var roleListService = RoleListServiceReturning();
        var existing = Domain.User.User.CreateUser(TokenEmail, TokenUserName, Sub);
        var users = RepositoryReturning(existing);
        var unitOfWork = SavingUnitOfWork();

        var result = await GetExtendedUserInfoEndpoint.HandleAsync(
            caller, userQuery.Object, roleListService.Object, users.Object, unitOfWork.Object,
            new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None);

        Assert.IsType<Ok<UserInfoExtendedResponse>>(result);
        users.Verify(u => u.AddAsync(It.IsAny<Domain.User.User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnForbidden_When_CallerIsDeactivated()
    {
        var caller = new Caller(
            Sub, "User", "Display Name", new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
            Email: TokenEmail, UserName: TokenUserName);

        var userQuery = QueryReturning(SampleUser);
        userQuery.Setup(q => q.IsDeactivatedAsync(Sub, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var roleListService = RoleListServiceReturning();
        var users = RepositoryReturning(existing: null);
        var unitOfWork = SavingUnitOfWork();

        var result = await GetExtendedUserInfoEndpoint.HandleAsync(
            caller, userQuery.Object, roleListService.Object, users.Object, unitOfWork.Object,
            new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal(UserErrorCodes.Deactivated, problem.ProblemDetails.Extensions["code"]);
        users.Verify(u => u.AddAsync(It.IsAny<Domain.User.User>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
