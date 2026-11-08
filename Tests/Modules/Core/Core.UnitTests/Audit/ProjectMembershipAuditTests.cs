using System.Collections;

using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Shared;
using Core.Features.Project.AssignProjectUser;
using Core.Features.Project.DeleteProject;
using Core.Features.Project.UnassignProjectUser;
using Core.Features.User.PromoteToProviderAdmin;
using Core.Features.User.ResetToPlatformUser;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using Moq;

using DomainUser = Core.Domain.User.User;
using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.UnitTests.Audit;

public sealed class ProjectMembershipAuditTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private const string TargetIdp = "target-idp";
    private const string TargetEmail = "t@example.com";
    private const string OutOfScopeReason = "project.caller_out_of_scope";
    private const string RoleWriteForbidden = "user.role_write_forbidden";
    private const int MemberRoleId = 3;
    private const int OwnerRoleId = 4;

    private static readonly IReadOnlyDictionary<int, string> SystemRoleMap = new Dictionary<int, string>
    {
        { 1, "User" }, { 2, "Admin" }, { 7, "ProviderAdmin" }
    };

    private static IList<RoleInfo> ProjectRoles() =>
    [
        new RoleInfo(OwnerRoleId, "Owner", "Owner", "Project", [], true, 20),
        new RoleInfo(MemberRoleId, "Member", "Member", "Project", [], true, 10),
    ];

    private static Caller CallerInScope() => new(
        new IdpId("u"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private static Caller CallerOutOfScope() => new(
        new IdpId("u"), "Owner", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller AdminCaller() => new(
        new IdpId("admin"), "Admin", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller NonAdminCaller() => new(
        new IdpId("u"), "User", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static ProjectAggregate BuildProject(EOperationMode mode = EOperationMode.Editable)
    {
        var project = ProjectAggregate.Create(
            ShortName, ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, mode);
        project.ClearDomainEvents();
        return project;
    }

    private static DomainUser TargetUser() =>
        DomainUser.CreateUser(Email.Create(TargetEmail), UserName.Create("target"), IdpId.Create(TargetIdp));

    private static ProjectAggregate BuildProjectWithMember(DomainUser member, int roleId)
    {
        var project = BuildProject();
        project.AssignUserWithRole(member, UserId.New(), ProjectRoleId.Create(roleId));
        project.ClearDomainEvents();
        return project;
    }

    private static DomainUser HydratedUser(string systemRoleName)
    {
        var user = DomainUser.CreateUser(Email.Create(TargetEmail), UserName.Create("target"), IdpId.Create(TargetIdp));
        user.PromoteUserToRole(ProjectRoleId.Create(SystemRoleMap.Single(kv => kv.Value == systemRoleName).Key));
        user.InitializeRoleList(SystemRoleMap, new WellKnownRoles());
        user.ClearDomainEvents();
        return user;
    }

    private static DomainUser ProviderAdminUser(ProviderId providerId)
    {
        var user = HydratedUser("User");
        Assert.True(user.PromoteToProviderAdmin(new HashSet<ProviderId> { providerId }).IsSuccess);
        user.ClearDomainEvents();
        return user;
    }

    private static Mock<IUnitOfWork> UnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    private sealed class RecordingOutputCacheInvalidator : IOutputCacheInvalidator
    {
        public List<string> EvictedTags { get; } = [];

        public ValueTask EvictAsync(string tag, CancellationToken ct = default)
        {
            EvictedTags.Add(tag);
            return ValueTask.CompletedTask;
        }
    }

    private static IReadOnlyList<string> DataValues(AuditEvent auditEvent) =>
        [.. (auditEvent.Data ?? new Dictionary<string, object?>()).Values
            .SelectMany(value => value is IEnumerable items and not string
                ? items.Cast<object?>()
                : [value])
            .Select(value => value?.ToString() ?? string.Empty)];

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static async Task<IResult> AssignAsync(
        Caller caller,
        ProjectAggregate project,
        RecordingAuditLogger<AssignProjectUserEndpoint.AuditCategory> audit,
        RecordingOutputCacheInvalidator? cache = null,
        Mock<IUnitOfWork>? unitOfWork = null,
        bool callerProvisioned = true)
    {
        var repository = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProjectAggregate?>(project));

        var users = new Mock<IRepository<DomainUser, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<DomainUser?>(TargetUser()));

        var directory = new Mock<IUserDirectory>();
        directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerProvisioned ? UserId.New() : (UserId?)null);

        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(ProjectRoles()));

        return await AssignProjectUserEndpoint.HandleAsync(
            new AssignProjectUserEndpoint.Request(ShortName, TargetIdp),
            caller, repository.Object, users.Object, directory.Object, roleList.Object,
            Options.Create(new WellKnownRoles()), (unitOfWork ?? UnitOfWork()).Object,
            cache ?? new RecordingOutputCacheInvalidator(), audit, new DefaultHttpContext(), CancellationToken.None);
    }

    private static async Task<IResult> UnassignAsync(
        Caller caller,
        ProjectAggregate project,
        DomainUser target,
        RecordingAuditLogger<UnassignProjectUserEndpoint.AuditCategory> audit,
        RecordingOutputCacheInvalidator? cache = null)
    {
        var repository = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProjectAggregate?>(project));

        var users = new Mock<IRepository<DomainUser, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<DomainUser?>(target));

        var directory = new Mock<IUserDirectory>();
        directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserId.New());

        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(ProjectRoles()));

        return await UnassignProjectUserEndpoint.HandleAsync(
            new UnassignProjectUserEndpoint.Request(ShortName, TargetIdp),
            caller, repository.Object, users.Object, directory.Object, roleList.Object,
            Options.Create(new WellKnownRoles()), UnitOfWork().Object,
            cache ?? new RecordingOutputCacheInvalidator(), audit, new DefaultHttpContext(), CancellationToken.None);
    }

    private static async Task<IResult> DeleteAsync(
        Caller caller,
        ProjectAggregate? project,
        RecordingAuditLogger<DeleteProjectEndpoint.AuditCategory> audit,
        RecordingOutputCacheInvalidator? cache = null)
    {
        var repository = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(project));

        return await DeleteProjectEndpoint.HandleAsync(
            ShortName, caller, repository.Object, UnitOfWork().Object,
            cache ?? new RecordingOutputCacheInvalidator(), audit, new DefaultHttpContext(), CancellationToken.None);
    }

    private static ProjectAggregate BuildProjectWithTwoMembers()
    {
        var project = BuildProject();
        project.AssignUserWithRole(
            DomainUser.CreateUser(Email.Create("a@example.com"), UserName.Create("user-a"), IdpId.Create("user-a")),
            UserId.New(), ProjectRoleId.Create(MemberRoleId));
        project.AssignUserWithRole(
            DomainUser.CreateUser(Email.Create("b@example.com"), UserName.Create("user-b"), IdpId.Create("user-b")),
            UserId.New(), ProjectRoleId.Create(MemberRoleId));
        project.ClearDomainEvents();
        return project;
    }

    private static Task<IResult> PromoteAsync(
        Caller caller,
        DomainUser user,
        RecordingAuditLogger<PromoteToProviderAdminEndpoint.AuditCategory> audit,
        Mock<IUnitOfWork>? unitOfWork = null,
        ProviderId? providerId = null)
    {
        var users = new Mock<IRepository<DomainUser, IdpId>>();
        users.Setup(r => r.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<DomainUser?>(user));

        var providers = new Mock<IProviderQuery>();
        providers.Setup(q => q.FindActiveProviderIdByNameAsync(It.IsAny<Domain.Provider.ProviderName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(providerId);

        return PromoteToProviderAdminEndpoint.HandleAsync(
            new PromoteToProviderAdminEndpoint.Request(TargetIdp, "hpc"),
            caller, users.Object, providers.Object, Options.Create(new WellKnownRoles()),
            (unitOfWork ?? UnitOfWork()).Object, audit, CancellationToken.None);
    }

    private static Task<IResult> ResetAsync(
        Caller caller,
        DomainUser user,
        RecordingAuditLogger<ResetProviderAdminEndpoint.AuditCategory> audit,
        Mock<IUnitOfWork>? unitOfWork = null)
    {
        var users = new Mock<IRepository<DomainUser, IdpId>>();
        users.Setup(r => r.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<DomainUser?>(user));

        return ResetProviderAdminEndpoint.HandleAsync(
            new ResetProviderAdminEndpoint.Request(TargetIdp),
            caller, users.Object, Options.Create(new WellKnownRoles()),
            (unitOfWork ?? UnitOfWork()).Object, audit, CancellationToken.None);
    }

    [Fact]
    public async Task Should_WriteSuccess_When_AssigningAUserCommits()
    {
        var audit = new RecordingAuditLogger<AssignProjectUserEndpoint.AuditCategory>();

        var result = await AssignAsync(CallerInScope(), BuildProject(), audit);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project.user.assigned", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal("user", auditEvent.TargetType);
        Assert.Equal(TargetIdp, auditEvent.TargetId);
        Assert.Equal(ShortName.Value, auditEvent.TenantId);
        Assert.Contains(MemberRoleId.ToString(System.Globalization.CultureInfo.InvariantCulture), DataValues(auditEvent));
        Assert.DoesNotContain(TargetEmail, DataValues(auditEvent));
    }

    [Fact]
    public async Task Should_WriteDenied_When_AssigningOutsideTheCallersScope()
    {
        var audit = new RecordingAuditLogger<AssignProjectUserEndpoint.AuditCategory>();

        var result = await AssignAsync(CallerOutOfScope(), BuildProject(), audit);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project.user.assigned", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal("user", auditEvent.TargetType);
        Assert.Equal(TargetIdp, auditEvent.TargetId);
        Assert.Equal(ShortName.Value, auditEvent.TenantId);
        Assert.Equal(OutOfScopeReason, auditEvent.Reason);
    }

    [Fact]
    public async Task Should_CommitAndEvictBeforeThrowing_When_TheAssignSuccessWriteFails()
    {
        var audit = new RecordingAuditLogger<AssignProjectUserEndpoint.AuditCategory> { ThrowOn = AuditOutcome.Success };
        var cache = new RecordingOutputCacheInvalidator();
        var unitOfWork = UnitOfWork();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AssignAsync(CallerInScope(), BuildProject(), audit, cache, unitOfWork));

        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal([CacheTags.Project(ShortName.Value), CacheTags.UserInfo(TargetIdp)], cache.EvictedTags);
        Assert.Equal(AuditOutcome.Success, Assert.Single(audit.Events).Outcome);
    }

    [Fact]
    public async Task Should_WriteNothing_When_AssigningIntoAReadOnlyProjectConflicts()
    {
        var audit = new RecordingAuditLogger<AssignProjectUserEndpoint.AuditCategory>();

        var result = await AssignAsync(CallerInScope(), BuildProject(EOperationMode.ReadOnly), audit);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteNothing_When_TheAssigningCallerIsNotProvisioned()
    {
        var audit = new RecordingAuditLogger<AssignProjectUserEndpoint.AuditCategory>();

        var result = await AssignAsync(CallerInScope(), BuildProject(), audit, callerProvisioned: false);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteSuccess_When_UnassigningAUserCommits()
    {
        var target = TargetUser();
        var audit = new RecordingAuditLogger<UnassignProjectUserEndpoint.AuditCategory>();

        var result = await UnassignAsync(CallerInScope(), BuildProjectWithMember(target, MemberRoleId), target, audit);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project.user.unassigned", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal("user", auditEvent.TargetType);
        Assert.Equal(TargetIdp, auditEvent.TargetId);
        Assert.Equal(ShortName.Value, auditEvent.TenantId);
        Assert.DoesNotContain(TargetEmail, DataValues(auditEvent));
    }

    [Fact]
    public async Task Should_WriteDenied_When_UnassigningOutsideTheCallersScope()
    {
        var target = TargetUser();
        var audit = new RecordingAuditLogger<UnassignProjectUserEndpoint.AuditCategory>();

        var result = await UnassignAsync(CallerOutOfScope(), BuildProjectWithMember(target, MemberRoleId), target, audit);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project.user.unassigned", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal("user", auditEvent.TargetType);
        Assert.Equal(TargetIdp, auditEvent.TargetId);
        Assert.Equal(ShortName.Value, auditEvent.TenantId);
        Assert.Equal(OutOfScopeReason, auditEvent.Reason);
    }

    [Fact]
    public async Task Should_EvictBeforeThrowing_When_TheUnassignSuccessWriteFails()
    {
        var target = TargetUser();
        var audit = new RecordingAuditLogger<UnassignProjectUserEndpoint.AuditCategory> { ThrowOn = AuditOutcome.Success };
        var cache = new RecordingOutputCacheInvalidator();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UnassignAsync(CallerInScope(), BuildProjectWithMember(target, MemberRoleId), target, audit, cache));

        Assert.Equal([CacheTags.Project(ShortName.Value), CacheTags.UserInfo(TargetIdp)], cache.EvictedTags);
        Assert.Equal(AuditOutcome.Success, Assert.Single(audit.Events).Outcome);
    }

    [Fact]
    public async Task Should_WriteNothing_When_UnassigningTheOwnerConflicts()
    {
        var target = TargetUser();
        var audit = new RecordingAuditLogger<UnassignProjectUserEndpoint.AuditCategory>();

        var result = await UnassignAsync(CallerInScope(), BuildProjectWithMember(target, OwnerRoleId), target, audit);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteSuccessWithTheAffectedMemberCount_When_DeletingAProjectCommits()
    {
        var audit = new RecordingAuditLogger<DeleteProjectEndpoint.AuditCategory>();

        var result = await DeleteAsync(CallerInScope(), BuildProjectWithTwoMembers(), audit);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project.self.deleted", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal(ShortName.Value, auditEvent.TenantId);
        Assert.Contains("2", DataValues(auditEvent));
    }

    [Fact]
    public async Task Should_WriteDenied_When_DeletingOutsideTheCallersScope()
    {
        var audit = new RecordingAuditLogger<DeleteProjectEndpoint.AuditCategory>();

        var result = await DeleteAsync(CallerOutOfScope(), BuildProjectWithTwoMembers(), audit);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project.self.deleted", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal(ShortName.Value, auditEvent.TenantId);
        Assert.Equal(OutOfScopeReason, auditEvent.Reason);
    }

    [Fact]
    public async Task Should_EvictEveryAffectedMemberBeforeThrowing_When_TheDeleteSuccessWriteFails()
    {
        var audit = new RecordingAuditLogger<DeleteProjectEndpoint.AuditCategory> { ThrowOn = AuditOutcome.Success };
        var cache = new RecordingOutputCacheInvalidator();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DeleteAsync(CallerInScope(), BuildProjectWithTwoMembers(), audit, cache));

        Assert.Equal(
            [CacheTags.Project(ShortName.Value), CacheTags.UserInfo("user-a"), CacheTags.UserInfo("user-b")],
            cache.EvictedTags);
        Assert.Equal(AuditOutcome.Success, Assert.Single(audit.Events).Outcome);
    }

    [Fact]
    public async Task Should_WriteNothing_When_DeletingAReadOnlyProjectConflicts()
    {
        var audit = new RecordingAuditLogger<DeleteProjectEndpoint.AuditCategory>();

        var result = await DeleteAsync(CallerInScope(), BuildProject(EOperationMode.ReadOnly), audit);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteNothing_When_TheProjectToDeleteIsMissing()
    {
        var audit = new RecordingAuditLogger<DeleteProjectEndpoint.AuditCategory>();

        var result = await DeleteAsync(CallerInScope(), project: null, audit);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteSuccessWithoutTenant_When_PromotingToProviderAdminCommits()
    {
        var audit = new RecordingAuditLogger<PromoteToProviderAdminEndpoint.AuditCategory>();

        var result = await PromoteAsync(AdminCaller(), HydratedUser("User"), audit, providerId: ProviderId.New());

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("user.provider_admin.granted", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("admin", auditEvent.ActorId);
        Assert.Equal("user", auditEvent.TargetType);
        Assert.Equal(TargetIdp, auditEvent.TargetId);
        Assert.Null(auditEvent.TenantId);
        Assert.Contains("hpc", DataValues(auditEvent));
        Assert.DoesNotContain(TargetEmail, DataValues(auditEvent));
    }

    [Fact]
    public async Task Should_WriteDeniedWithoutTenant_When_ANonAdminPromotes()
    {
        var audit = new RecordingAuditLogger<PromoteToProviderAdminEndpoint.AuditCategory>();

        var result = await PromoteAsync(NonAdminCaller(), HydratedUser("User"), audit, providerId: ProviderId.New());

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("user.provider_admin.granted", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal("user", auditEvent.TargetType);
        Assert.Equal(TargetIdp, auditEvent.TargetId);
        Assert.Null(auditEvent.TenantId);
        Assert.Equal(RoleWriteForbidden, auditEvent.Reason);
    }

    [Fact]
    public async Task Should_CommitBeforeThrowing_When_ThePromoteSuccessWriteFails()
    {
        var audit = new RecordingAuditLogger<PromoteToProviderAdminEndpoint.AuditCategory> { ThrowOn = AuditOutcome.Success };
        var unitOfWork = UnitOfWork();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PromoteAsync(AdminCaller(), HydratedUser("User"), audit, unitOfWork, ProviderId.New()));

        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(AuditOutcome.Success, Assert.Single(audit.Events).Outcome);
    }

    [Fact]
    public async Task Should_WriteNothing_When_PromotingAnAdministratorConflicts()
    {
        var audit = new RecordingAuditLogger<PromoteToProviderAdminEndpoint.AuditCategory>();

        var result = await PromoteAsync(AdminCaller(), HydratedUser("Admin"), audit, providerId: ProviderId.New());

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteNothing_When_TheProviderToPromoteIntoIsMissing()
    {
        var audit = new RecordingAuditLogger<PromoteToProviderAdminEndpoint.AuditCategory>();

        var result = await PromoteAsync(AdminCaller(), HydratedUser("User"), audit, providerId: null);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteSuccessWithTheProviderIdsHeldBeforeTheReset_When_ResettingCommits()
    {
        var providerId = ProviderId.New();
        var audit = new RecordingAuditLogger<ResetProviderAdminEndpoint.AuditCategory>();

        var result = await ResetAsync(AdminCaller(), ProviderAdminUser(providerId), audit);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("user.provider_admin.revoked", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("admin", auditEvent.ActorId);
        Assert.Equal("user", auditEvent.TargetType);
        Assert.Equal(TargetIdp, auditEvent.TargetId);
        Assert.Null(auditEvent.TenantId);
        Assert.Contains(DataValues(auditEvent), value => value.Contains(providerId.Value.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(TargetEmail, DataValues(auditEvent));
    }

    [Fact]
    public async Task Should_WriteDeniedWithoutTenant_When_ANonAdminResets()
    {
        var audit = new RecordingAuditLogger<ResetProviderAdminEndpoint.AuditCategory>();

        var result = await ResetAsync(NonAdminCaller(), ProviderAdminUser(ProviderId.New()), audit);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("user.provider_admin.revoked", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal("user", auditEvent.TargetType);
        Assert.Equal(TargetIdp, auditEvent.TargetId);
        Assert.Null(auditEvent.TenantId);
        Assert.Equal(RoleWriteForbidden, auditEvent.Reason);
    }

    [Fact]
    public async Task Should_CommitBeforeThrowing_When_TheResetSuccessWriteFails()
    {
        var audit = new RecordingAuditLogger<ResetProviderAdminEndpoint.AuditCategory> { ThrowOn = AuditOutcome.Success };
        var unitOfWork = UnitOfWork();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ResetAsync(AdminCaller(), ProviderAdminUser(ProviderId.New()), audit, unitOfWork));

        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(AuditOutcome.Success, Assert.Single(audit.Events).Outcome);
    }

    [Fact]
    public async Task Should_WriteNothing_When_ResettingAnAdministratorConflicts()
    {
        var audit = new RecordingAuditLogger<ResetProviderAdminEndpoint.AuditCategory>();

        var result = await ResetAsync(AdminCaller(), HydratedUser("Admin"), audit);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Empty(audit.Events);
    }
}
