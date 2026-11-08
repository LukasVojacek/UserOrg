using System.Text.Json;

using Core.Domain;
using Core.Domain.Shared;
using Core.Features.Project.ChangeProjectUserRole;
using Core.Shared.OutputCache;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.UnitTests.Projects;

public sealed class ChangeProjectUserRoleEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private const string TargetIdp = "target-idp";

    // One shared instance, not a fresh one per call: Project matches member-role rows by User
    // reference (`r.User == target`), so a test that pre-seeds a prior role must seed the very
    // object the user repository later returns, or ChangeUserRole reports no previous role.
    private static readonly Domain.User.User TargetUser = Domain.User.User.CreateUser(
        Email.Create("t@example.com"), UserName.Create("target"), IdpId.Create(TargetIdp));

    private sealed class FakeAuditLogger : IAuditLogger<ChangeProjectUserRoleEndpoint.AuditCategory>
    {
        public List<AuditEvent> Events { get; } = [];

        // Reproduces a fail-closed sink outage on the post-commit success write.
        public bool FailsOnSuccess { get; init; }

        public ValueTask AuditAsync(AuditEvent auditEvent)
        {
            Events.Add(auditEvent);
            if (FailsOnSuccess && auditEvent.Outcome == AuditOutcome.Success)
                throw new InvalidOperationException("audit sink unavailable");

            return ValueTask.CompletedTask;
        }

        public ValueTask AuditAsync(AuditEvent auditEvent, Action<ILogger> log) => AuditAsync(auditEvent);
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

    // Admin L100, Owner L20, Manager L15, Member L10.
    private static IList<RoleInfo> Roles() =>
    [
        new RoleInfo(1, "Admin", "Admin", "Project", [], true, 100),
        new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20),
        new RoleInfo(5, "Manager", "Manager", "Project", [], true, 15),
        new RoleInfo(3, "Member", "Member", "Project", [], true, 10),
    ];

    private static ProjectAggregate BuildProject(EOperationMode mode = EOperationMode.Editable)
    {
        var project = ProjectAggregate.Create(
            ShortName, ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, mode);
        project.ClearDomainEvents();
        return project;
    }

    private static Caller CallerWithRole(string roleName) => new(
        new IdpId("u"), roleName, null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { [roleName] = new HashSet<ProjectShortName> { ShortName } });

    private static Domain.User.User NewUser(string name) =>
        Domain.User.User.CreateUser(Email.Create($"{name}@example.com"), UserName.Create(name), IdpId.Create($"idp-{name}"));

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true,
    };

    private static ChangeProjectUserRoleEndpoint.Body BodyFromWire(string roleName) =>
        JsonSerializer.Deserialize<ChangeProjectUserRoleEndpoint.Body>(
            JsonSerializer.Serialize(new { RoleName = roleName }), WireJson)!;

    private static async Task<(IResult Result, FakeAuditLogger Audit)> InvokeAsync(
        Caller caller, string roleName, ProjectAggregate? project,
        FakeAuditLogger? auditLogger = null, IOutputCacheInvalidator? cacheInvalidator = null)
    {
        var repository = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(project));

        var users = new Mock<IRepository<Domain.User.User, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(TargetUser));

        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(Roles()));

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var cache = cacheInvalidator ?? new RecordingOutputCacheInvalidator();
        var audit = auditLogger ?? new FakeAuditLogger();

        var result = await ChangeProjectUserRoleEndpoint.HandleAsync(
            new ChangeProjectUserRoleEndpoint.Request(ShortName, TargetIdp, BodyFromWire(roleName)),
            caller, repository.Object, users.Object,
            roleList.Object, Options.Create(new WellKnownRoles()), unitOfWork.Object, cache, audit, new DefaultHttpContext(), CancellationToken.None);

        return (result, audit);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    [Fact]
    public async Task Should_ReturnNoContent_When_OwnerAssignsMember()
    {
        // Owner (L20) assigns Member (id 3, L10) — strictly below, at baseline.
        var (result, _) = await InvokeAsync(CallerWithRole("Owner"), roleName: "Member", BuildProject());

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnForbidden_When_AssigningEqualOrHigherRole()
    {
        // Manager (L15) tries to assign Owner (id 4, L20) — not strictly below → 403.
        var (result, _) = await InvokeAsync(CallerWithRole("Manager"), roleName: "Owner", BuildProject());

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnConflict_When_SecondOwner()
    {
        // A higher-level caller (Admin L100) assigns Owner to a member while an active owner already exists → 409.
        var project = BuildProject();
        project.AssignUserWithRole(NewUser("existing-owner"), UserId.New(), ProjectRoleId.Create(4));
        project.ClearDomainEvents();

        var (result, audit) = await InvokeAsync(CallerWithRole("Admin"), roleName: "Owner", project);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        // A 409 conflict is not an authorization refusal and is out of the PoC audit scope.
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_UnknownRole()
    {
        var (result, _) = await InvokeAsync(CallerWithRole("Owner"), roleName: "NoSuchRole", BuildProject());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ProjectErrorCodes.UnknownRole, problem.ProblemDetails.Extensions["code"]);
        Assert.False(string.IsNullOrWhiteSpace(problem.ProblemDetails.Detail));
    }

    [Fact]
    public async Task Should_MatchRoleNameIgnoringCase_When_ChangingRole()
    {
        var (result, audit) = await InvokeAsync(CallerWithRole("Owner"), roleName: "mAnAgEr", BuildProject());

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal(5, auditEvent.Data!["new_role_id"]);
        Assert.Equal("Manager", auditEvent.Data!["new_role_name"]);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_RoleNameIsANumericRoleId()
    {
        var (result, _) = await InvokeAsync(CallerWithRole("Owner"), roleName: "3", BuildProject());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ProjectErrorCodes.UnknownRole, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_CallerOutOfScope()
    {
        var caller = new Caller(new IdpId("u"), "Owner", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

        var (result, _) = await InvokeAsync(caller, roleName: "Member", BuildProject());

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnConflict_When_ProjectReadOnly()
    {
        var (result, audit) = await InvokeAsync(CallerWithRole("Owner"), roleName: "Member", BuildProject(EOperationMode.ReadOnly));

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        // A 409 conflict is not an authorization refusal and is out of the PoC audit scope.
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_ReturnForbidden_When_RequesterHasNoKnownRoleInProject()
    {
        // Caller is in scope but holds only a role name that is not in the role list, so the
        // requester level resolves to 0 — the hierarchy check then denies every assignment (fail-closed).
        var caller = new Caller(
            new IdpId("u"), "ext", null,
            new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["UnknownRole"] = new HashSet<ProjectShortName> { ShortName } });

        var (result, _) = await InvokeAsync(caller, roleName: "Member", BuildProject());

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
    }

    [Fact]
    public async Task Should_WriteSuccessAuditEvent_When_RoleChangeCommits()
    {
        // Target already holds Member (id 3); Owner promotes to Manager (id 5) — both roles known.
        var project = BuildProject();
        project.AssignUserWithRole(TargetUser, UserId.New(), ProjectRoleId.Create(3));
        project.ClearDomainEvents();

        var (_, audit) = await InvokeAsync(CallerWithRole("Owner"), roleName: "Manager", project);

        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal(AuditActions.ProjectUserRoleChanged, auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal(TargetIdp, auditEvent.TargetId);
        Assert.Equal(5, auditEvent.Data!["new_role_id"]);
        Assert.Equal("Manager", auditEvent.Data!["new_role_name"]);
        Assert.Equal(3, auditEvent.Data!["old_role_id"]);
        Assert.Equal("Member", auditEvent.Data!["old_role_name"]);
    }

    [Fact]
    public async Task Should_OmitOldRoleKeys_When_TargetHasNoPriorRole()
    {
        // A null previous role from ChangeUserRole is ambiguous — it also covers a regrant over a
        // soft-deleted role row, not only "never a member". Asserting an old_role_id here would
        // misrepresent that case, so the keys are omitted instead.
        var (_, audit) = await InvokeAsync(CallerWithRole("Owner"), roleName: "Member", BuildProject());

        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal(3, auditEvent.Data!["new_role_id"]);
        Assert.DoesNotContain("old_role_id", auditEvent.Data!.Keys);
        Assert.DoesNotContain("old_role_name", auditEvent.Data!.Keys);
    }

    [Fact]
    public async Task Should_OmitOldRoleName_When_PriorRoleIsNotInTheRoleCatalog()
    {
        // The target holds a role id the role service no longer lists (deleted role): old_role_id is
        // still a fact and stays, the unresolvable name is omitted rather than written as null.
        var project = BuildProject();
        project.AssignUserWithRole(TargetUser, UserId.New(), ProjectRoleId.Create(99));
        project.ClearDomainEvents();

        var (_, audit) = await InvokeAsync(CallerWithRole("Owner"), roleName: "Member", project);

        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal(99, auditEvent.Data!["old_role_id"]);
        Assert.DoesNotContain("old_role_name", auditEvent.Data!.Keys);
    }

    [Fact]
    public async Task Should_WriteDeniedAuditEvent_When_CallerOutOfScope()
    {
        var caller = new Caller(new IdpId("u"), "Owner", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

        var (_, audit) = await InvokeAsync(caller, roleName: "Member", BuildProject());

        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal(AuditActions.ProjectUserRoleChanged, auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal("project.caller_out_of_scope", auditEvent.Reason);
    }

    [Fact]
    public async Task Should_WriteDeniedAuditEvent_When_HierarchyViolation()
    {
        // Manager (L15) tries to assign Owner (id 4, L20) — not strictly below → denied before commit.
        var (_, audit) = await InvokeAsync(CallerWithRole("Manager"), roleName: "Owner", BuildProject());

        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal(AuditActions.ProjectUserRoleChanged, auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal(TargetIdp, auditEvent.TargetId);
        Assert.Equal(ProjectErrorCodes.CannotAssignEqualOrHigherRole, auditEvent.Reason);
    }

    [Fact]
    public async Task Should_EvictOutputCache_When_SuccessAuditWriteFails()
    {
        // Fail-closed audit lets the committed role change stand behind a failed response; it must not
        // also leave the old role readable from the output cache until the entry expires.
        var audit = new FakeAuditLogger { FailsOnSuccess = true };
        var cache = new RecordingOutputCacheInvalidator();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeAsync(CallerWithRole("Owner"), roleName: "Member", BuildProject(), audit, cache));

        Assert.Equal(
            [CacheTags.Project(ShortName.Value), CacheTags.UserInfo(TargetIdp)],
            cache.EvictedTags);
    }
}
