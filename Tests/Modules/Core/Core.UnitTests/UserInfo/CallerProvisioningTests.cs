using Core.DataAccess.Exceptions;
using Core.DataAccess.Queries.Contracts;
using Core.Features.UserInfo.UseCases;
using Core.UnitTests.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace Core.UnitTests.UserInfo;

public sealed class CallerProvisioningTests
{
    private static readonly IdpId Sub = new("user-sub-abc");
    private static readonly Email TokenEmail = Email.Create("caller@example.com");
    private static readonly UserName TokenUserName = UserName.Create("callername");

    private static Caller FullCaller() => new(
        Sub,
        "User",
        "Display Name",
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        Email: TokenEmail,
        UserName: TokenUserName);

    private static Caller CallerMissingEmail() => FullCaller() with { Email = null };

    private static Caller CallerMissingUserName() => FullCaller() with { UserName = null };

    private static Mock<IUnitOfWork> SavingUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    private static Mock<IUserInfoQuery> LookupReporting(bool deactivated)
    {
        var query = new Mock<IUserInfoQuery>();
        query.Setup(q => q.IsDeactivatedAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>())).ReturnsAsync(deactivated);
        return query;
    }

    private static Mock<IUserInfoQuery> ActiveLookup() => LookupReporting(deactivated: false);

    /// <summary>Mimics DatabaseContext.UserRepository.AddAsync: assigns exactly one system role on insert.</summary>
    private static Mock<IRepository<Domain.User.User, IdpId>> RepositoryWithNoExistingUser()
    {
        var users = new Mock<IRepository<Domain.User.User, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(null));
        users.Setup(u => u.AddAsync(It.IsAny<Domain.User.User>(), It.IsAny<CancellationToken>()))
            .Returns((Domain.User.User user, CancellationToken _) =>
            {
                user.PromoteUserToRole(ProjectRoleId.Create(1));
                return ValueTask.FromResult<Domain.User.User?>(user);
            });
        return users;
    }

    private static Mock<IRepository<Domain.User.User, IdpId>> RepositoryWithExistingUser(Domain.User.User user)
    {
        var users = new Mock<IRepository<Domain.User.User, IdpId>>();
        users.Setup(u => u.FindAsync(Sub, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(user));
        return users;
    }

    [Fact]
    public async Task Should_CreateUser_WithExactlyOneSystemPermission_When_SubUnknown()
    {
        var users = RepositoryWithNoExistingUser();
        var unitOfWork = SavingUnitOfWork();

        var provisioned = await CallerProvisioning.ProvisionAsync(FullCaller(), users.Object, ActiveLookup().Object, unitOfWork.Object,
            new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None);

        Assert.True(provisioned.IsSuccess);
        users.Verify(u => u.AddAsync(It.IsAny<Domain.User.User>(), It.IsAny<CancellationToken>()), Times.Once);
        users.Verify(u => u.AddAsync(
            It.Is<Domain.User.User>(created => created.SystemPermissionsCount == 1
                && created.Email == TokenEmail
                && created.UserName == TokenUserName
                && created.IdentityProviderId == Sub),
            It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_NotDuplicateUser_When_AlreadyExists()
    {
        var existing = Domain.User.User.CreateUser(TokenEmail, TokenUserName, Sub);
        var users = RepositoryWithExistingUser(existing);
        var unitOfWork = SavingUnitOfWork();

        var provisioned = await CallerProvisioning.ProvisionAsync(FullCaller(), users.Object, ActiveLookup().Object, unitOfWork.Object,
            new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None);

        Assert.True(provisioned.IsSuccess);
        users.Verify(u => u.AddAsync(It.IsAny<Domain.User.User>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_SyncEmail_When_TokenEmailDiffersFromStored()
    {
        var stale = Domain.User.User.CreateUser(Email.Create("old@example.com"), TokenUserName, Sub);
        var users = RepositoryWithExistingUser(stale);
        var unitOfWork = SavingUnitOfWork();

        await CallerProvisioning.ProvisionAsync(FullCaller(), users.Object, ActiveLookup().Object, unitOfWork.Object,
            new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None);

        Assert.Equal(TokenEmail, stale.Email);
    }

    [Fact]
    public async Task Should_NotThrow_When_ConcurrentInsertLosesUniqueIndexRace()
    {
        // Simulates the ADR-0068 §6 race: two concurrent first logins for the same new sub both miss
        // FindAsync and both AddAsync; this request's SaveChangesAsync loses and EF/SQL Server raises the
        // DbUpdateException for the UX_User_IdentityProviderUserId unique-index violation. We cannot
        // reproduce the real SQL race with EF InMemory (no unique-index enforcement), so this test forces
        // SaveChangesAsync to throw that specific DbUpdateException and has the second FindAsync return the
        // row the winning request committed, then asserts ProvisionAsync converges on it instead of throwing.
        var winner = Domain.User.User.CreateUser(TokenEmail, TokenUserName, Sub);

        var users = new Mock<IRepository<Domain.User.User, IdpId>>();
        var findCallCount = 0;
        users.Setup(u => u.FindAsync(Sub, It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(++findCallCount == 1 ? null : winner));
        users.Setup(u => u.AddAsync(It.IsAny<Domain.User.User>(), It.IsAny<CancellationToken>()))
            .Returns((Domain.User.User user, CancellationToken _) => ValueTask.FromResult<Domain.User.User?>(user));

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException(
                "An error occurred while saving the entity changes.",
                new Exception($"Cannot insert duplicate key row in object 'dbo.User' with unique index '{UserIdentityConflictClassifier.IdentityProviderIndexName}'.")));

        var provisioned = await CallerProvisioning.ProvisionAsync(FullCaller(), users.Object, ActiveLookup().Object, unitOfWork.Object,
            new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None);

        Assert.True(provisioned.IsSuccess);
        users.Verify(u => u.FindAsync(Sub, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Should_NotThrow_When_ConcurrentInsertLosesUserNameUniqueIndexRace()
    {
        // Same race as Should_NotThrow_When_ConcurrentInsertLosesUniqueIndexRace, but this time SQL Server
        // happens to report the OTHER unique index the duplicate insert violates (UX_User_UserName, not
        // UX_User_IdentityProviderUserId) - both concurrent requests carry the same token, so the same
        // preferred_username collides too, and SQL Server names only the first index it hits. Whichever one
        // that is, the request must still converge on the winner's row instead of surfacing a 500.
        var winner = Domain.User.User.CreateUser(TokenEmail, TokenUserName, Sub);

        var users = new Mock<IRepository<Domain.User.User, IdpId>>();
        var findCallCount = 0;
        users.Setup(u => u.FindAsync(Sub, It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(++findCallCount == 1 ? null : winner));
        users.Setup(u => u.AddAsync(It.IsAny<Domain.User.User>(), It.IsAny<CancellationToken>()))
            .Returns((Domain.User.User user, CancellationToken _) => ValueTask.FromResult<Domain.User.User?>(user));

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException(
                "An error occurred while saving the entity changes.",
                new Exception($"Cannot insert duplicate key row in object 'dbo.User' with unique index '{UserIdentityConflictClassifier.UserNameIndexName}'.")));

        var provisioned = await CallerProvisioning.ProvisionAsync(FullCaller(), users.Object, ActiveLookup().Object, unitOfWork.Object,
            new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None);

        Assert.True(provisioned.IsSuccess);
        users.Verify(u => u.FindAsync(Sub, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Should_Rethrow_When_SaveFailsForAnUnrelatedReason()
    {
        // A DbUpdateException NOT matching the identity-provider unique index (e.g. some other DB fault)
        // must still surface as a genuine server error, not be swallowed as a lost provisioning race.
        var users = RepositoryWithNoExistingUser();
        var unitOfWork = new Mock<IUnitOfWork>();
        var unrelatedFailure = new DbUpdateException("Timeout expired.", new Exception("Timeout expired."));
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(unrelatedFailure);

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() =>
            CallerProvisioning.ProvisionAsync(FullCaller(), users.Object, ActiveLookup().Object, unitOfWork.Object,
            new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None));

        Assert.Same(unrelatedFailure, thrown);
    }

    [Fact]
    public async Task Should_NotProvision_When_CallerEmailMissing()
    {
        var users = RepositoryWithNoExistingUser();
        var unitOfWork = SavingUnitOfWork();

        var provisioned = await CallerProvisioning.ProvisionAsync(CallerMissingEmail(), users.Object, ActiveLookup().Object, unitOfWork.Object,
            new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None);

        Assert.False(provisioned.IsSuccess);
        Assert.Equal(CallerProvisioningErrors.NotProvisionableCode, provisioned.Error.Code);
        users.Verify(u => u.AddAsync(It.IsAny<Domain.User.User>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_NotProvision_When_CallerUserNameMissing()
    {
        var users = RepositoryWithNoExistingUser();
        var unitOfWork = SavingUnitOfWork();

        var provisioned = await CallerProvisioning.ProvisionAsync(CallerMissingUserName(), users.Object, ActiveLookup().Object, unitOfWork.Object,
            new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None);

        Assert.False(provisioned.IsSuccess);
        Assert.Equal(CallerProvisioningErrors.NotProvisionableCode, provisioned.Error.Code);
        users.Verify(u => u.AddAsync(It.IsAny<Domain.User.User>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_FailWithDeactivated_When_UserIsSoftDeleted()
    {
        // The !IsDeleted query filter hides the row from FindAsync; without the filter-bypassing lookup the
        // caller would be re-provisioned as a brand-new user and the insert would hit UX_User_IdentityProviderUserId.
        var users = RepositoryWithNoExistingUser();
        var lookup = LookupReporting(deactivated: true);
        var unitOfWork = SavingUnitOfWork();

        var provisioned = await CallerProvisioning.ProvisionAsync(FullCaller(), users.Object, lookup.Object, unitOfWork.Object,
            new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None);

        Assert.False(provisioned.IsSuccess);
        Assert.Equal(UserErrorCodes.Deactivated, provisioned.Error.Code);
        users.Verify(u => u.AddAsync(It.IsAny<Domain.User.User>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_NotConsultDeactivationLookup_When_UserFound()
    {
        var existing = Domain.User.User.CreateUser(TokenEmail, TokenUserName, Sub);
        var users = RepositoryWithExistingUser(existing);
        var lookup = LookupReporting(deactivated: true);
        var unitOfWork = SavingUnitOfWork();

        var provisioned = await CallerProvisioning.ProvisionAsync(FullCaller(), users.Object, lookup.Object, unitOfWork.Object,
            new RecordingAuditLogger<CallerProvisioning.AuditCategory>(), NullLogger<CallerProvisioning.AuditCategory>.Instance, CancellationToken.None);

        Assert.True(provisioned.IsSuccess);
        lookup.Verify(q => q.IsDeactivatedAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
