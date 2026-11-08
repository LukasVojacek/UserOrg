using Core.DataAccess.Queries.Contracts;
using Core.Features.UserInfo;
using Core.Features.UserInfo.UseCases;
using Core.UnitTests.Shared;

using Forge.Core.Features.Permission.Services;
using Forge.Core.Features.Role.Services;

using Lukdrasil.Result;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using UserOrg.Shared;

namespace Core.UnitTests.UserInfo;

public sealed class UserInfoLoaderTests
{
    private const string Sub = "user-sub-abc";
    private const string DefaultRole = "User";

    private static readonly IdpId SubId = new(Sub);
    private static readonly Email TokenEmail = Email.Create("caller@example.com");
    private static readonly UserName TokenUserName = UserName.Create("callername");

    private static readonly UserDetails SampleUser = new(
        IdentityProviderUserId: SubId,
        Email: TokenEmail,
        UserName: TokenUserName);

    private static Caller BuildCaller(string role = DefaultRole, string? name = null, bool provisionable = true, params (string Role, string[] ShortNames)[] projectRoles)
        => new(
            SubId,
            role,
            name,
            projectRoles.ToDictionary(
                p => p.Role,
                p => (IReadOnlySet<ProjectShortName>)p.ShortNames.Select(ProjectShortName.Create).ToHashSet()),
            Email: provisionable ? TokenEmail : null,
            UserName: provisionable ? TokenUserName : null);

    private static IList<RoleInfo> BuildRoles(string roleName = DefaultRole, params string[] permKeys)
    {
        var perms = permKeys.Select(k => new PermissionInfo(new PermissionKey(k), k)).ToList();
        return [new RoleInfo(1, roleName, "", "default", perms, true, 1)];
    }

    private static Mock<IRoleListService> RoleListServiceReturning(IList<RoleInfo> roles)
    {
        var roleListService = new Mock<IRoleListService>();
        roleListService.Setup(s => s.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Lukdrasil.Error.Error>.Success(roles));
        return roleListService;
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
    public async Task Should_ReturnNull_When_CallerCannotBeProvisioned()
    {
        var userQuery = new Mock<IUserInfoQuery>();
        var roleListService = RoleListServiceReturning(BuildRoles());
        var users = RepositoryReturning(existing: null);
        var unitOfWork = SavingUnitOfWork();

        var result = await UserInfoLoader.LoadUserInfoAsync(
            userQuery.Object, roleListService.Object, users.Object, unitOfWork.Object,
            BuildCaller(provisionable: false),
            new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("caller.not_provisioned", result.Error.Code);
        userQuery.Verify(q => q.GetBySubAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ThrowWhenRoleServiceFails()
    {
        var userQuery = new Mock<IUserInfoQuery>();
        userQuery.Setup(q => q.GetBySubAsync(SubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleUser);

        var roleListService = new Mock<IRoleListService>();
        roleListService.Setup(s => s.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Lukdrasil.Error.Error>.Failure(new Lukdrasil.Error.Error("TestError", Lukdrasil.Error.ErrorSeverity.Error, "Test failure")));

        var users = RepositoryReturning(Domain.User.User.CreateUser(TokenEmail, TokenUserName, SubId));
        var unitOfWork = SavingUnitOfWork();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UserInfoLoader.LoadUserInfoAsync(
                userQuery.Object, roleListService.Object, users.Object, unitOfWork.Object, BuildCaller(),
                new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None));
    }

    [Fact]
    public async Task Should_UseCallerRole_AsDefaultRole()
    {
        var userQuery = new Mock<IUserInfoQuery>();
        userQuery.Setup(q => q.GetBySubAsync(SubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleUser);

        var roleListService = RoleListServiceReturning(BuildRoles(DefaultRole, "perm:read"));
        var users = RepositoryReturning(Domain.User.User.CreateUser(TokenEmail, TokenUserName, SubId));
        var unitOfWork = SavingUnitOfWork();

        var result = await UserInfoLoader.LoadUserInfoAsync(
            userQuery.Object, roleListService.Object, users.Object, unitOfWork.Object, BuildCaller(DefaultRole),
            new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None);

        Assert.Equal(DefaultRole, result.Value.SystemRoles[0].RoleName);
    }

    [Fact]
    public async Task Should_ComposeResult_FromCallerAndUserDetails()
    {
        var userQuery = new Mock<IUserInfoQuery>();
        userQuery.Setup(q => q.GetBySubAsync(SubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleUser);

        var roleListService = RoleListServiceReturning(BuildRoles(DefaultRole, "perm:read"));
        var users = RepositoryReturning(Domain.User.User.CreateUser(TokenEmail, TokenUserName, SubId));
        var unitOfWork = SavingUnitOfWork();

        var result = await UserInfoLoader.LoadUserInfoAsync(
            userQuery.Object, roleListService.Object, users.Object, unitOfWork.Object, BuildCaller(DefaultRole, "Claim Name"),
            new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None);

        Assert.Equal(SampleUser.IdentityProviderUserId.Value, result.Value.IdentityProviderUserId);
        Assert.Equal("Claim Name", result.Value.Name);
        Assert.Equal(SampleUser.Email.Value, result.Value.Email);
        Assert.Equal(SampleUser.UserName.Value, result.Value.UserName);
        Assert.Single(result.Value.SystemRoles);
    }

    [Fact]
    public async Task Should_QueryUserDetailsByCallerIdpId()
    {
        var userQuery = new Mock<IUserInfoQuery>();
        userQuery.Setup(q => q.GetBySubAsync(SubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleUser);

        var roleListService = RoleListServiceReturning(BuildRoles(DefaultRole, "perm:read"));
        var users = RepositoryReturning(Domain.User.User.CreateUser(TokenEmail, TokenUserName, SubId));
        var unitOfWork = SavingUnitOfWork();

        await UserInfoLoader.LoadUserInfoAsync(
            userQuery.Object, roleListService.Object, users.Object, unitOfWork.Object, BuildCaller(),
            new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None);

        userQuery.Verify(q => q.GetBySubAsync(SubId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
