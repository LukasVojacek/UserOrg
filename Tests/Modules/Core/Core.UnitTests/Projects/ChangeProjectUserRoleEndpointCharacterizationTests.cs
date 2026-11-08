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

/// <summary>
/// Characterization tests pinning the guards of <see cref="ChangeProjectUserRoleEndpoint.HandleAsync"/> that the
/// existing suite leaves open, their order, status and code, before they are extracted (T-197-03).
/// </summary>
public sealed class ChangeProjectUserRoleEndpointCharacterizationTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private const string TargetIdp = "target-idp";

    private sealed class RecordingAuditLogger : IAuditLogger<ChangeProjectUserRoleEndpoint.AuditCategory>
    {
        public List<AuditEvent> Events { get; } = [];

        public ValueTask AuditAsync(AuditEvent auditEvent)
        {
            Events.Add(auditEvent);
            return ValueTask.CompletedTask;
        }

        public ValueTask AuditAsync(AuditEvent auditEvent, Action<ILogger> log) => AuditAsync(auditEvent);
    }

    private sealed class Fixture
    {
        public Mock<IRepository<ProjectAggregate, ProjectShortName>> Repository { get; } = new();
        public Mock<IRepository<Domain.User.User, IdpId>> Users { get; } = new();
        public Mock<IRoleListService> RoleList { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IOutputCacheInvalidator> Cache { get; } = new();
        public RecordingAuditLogger Audit { get; } = new();

        public Fixture()
        {
            Repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.FromResult<ProjectAggregate?>(BuildProject()));
            Users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.FromResult<Domain.User.User?>(Domain.User.User.CreateUser(
                    Email.Create("t@example.com"), UserName.Create("target"), IdpId.Create(TargetIdp))));
            WithRoles(Roles());
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        }

        public void WithRoles(IList<RoleInfo> roles) =>
            RoleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(roles));

        public Task<IResult> InvokeAsync(Caller caller, string roleName) =>
            ChangeProjectUserRoleEndpoint.HandleAsync(
                new ChangeProjectUserRoleEndpoint.Request(ShortName, TargetIdp, new ChangeProjectUserRoleEndpoint.Body(roleName)),
                caller, Repository.Object, Users.Object, RoleList.Object, Options.Create(new WellKnownRoles()),
                UnitOfWork.Object, Cache.Object, Audit, new DefaultHttpContext(), CancellationToken.None);

        public void VerifyNothingWritten()
        {
            UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            Cache.Verify(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Empty(Audit.Events);
        }
    }

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

    private static Caller CallerWithRoles(Dictionary<string, IReadOnlySet<ProjectShortName>> projectRoles) =>
        new(new IdpId("u"), "User", null, projectRoles);

    private static Caller OwnerCaller() =>
        CallerWithRoles(new() { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private static void AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_AuditDeniedWithRawTargetAndSkipRoleLookup_When_CallerOutOfScope()
    {
        var fixture = new Fixture();

        var result = await fixture.InvokeAsync(CallerWithRoles(new()), "Member");

        AssertProblem(result, StatusCodes.Status404NotFound, "project.not_found");
        var auditEvent = Assert.Single(fixture.Audit.Events);
        Assert.Equal("user", auditEvent.TargetType);
        Assert.Equal(TargetIdp, auditEvent.TargetId);
        fixture.RoleList.VerifyNoOtherCalls();
        fixture.Users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReturnUpstreamWithoutAudit_When_RoleServiceFails()
    {
        var fixture = new Fixture();
        fixture.RoleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Failure(new Error("RoleServiceDown", ErrorSeverity.Error, "down")));

        var result = await fixture.InvokeAsync(OwnerCaller(), "Member");

        AssertProblem(result, StatusCodes.Status502BadGateway, "upstream.role_service_unavailable");
        fixture.Users.VerifyNoOtherCalls();
        fixture.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnUnknownRole_Before_CheckingConfiguredRoles()
    {
        var fixture = new Fixture();
        fixture.WithRoles([new RoleInfo(5, "Manager", "Manager", "Project", [], true, 15)]);

        var result = await fixture.InvokeAsync(OwnerCaller(), "NoSuchRole");

        AssertProblem(result, StatusCodes.Status400BadRequest, ProjectErrorCodes.UnknownRole);
    }

    [Fact]
    public async Task Should_ReturnUpstream_When_MemberRoleIsNotInTheCatalog()
    {
        var fixture = new Fixture();
        fixture.WithRoles(
        [
            new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20),
            new RoleInfo(5, "Manager", "Manager", "Project", [], true, 15),
        ]);

        var result = await fixture.InvokeAsync(OwnerCaller(), "Manager");

        AssertProblem(result, StatusCodes.Status502BadGateway, "upstream.role_service_unavailable");
        fixture.Users.VerifyNoOtherCalls();
        fixture.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnUpstream_When_OwnerRoleIsNotInTheCatalog()
    {
        var fixture = new Fixture();
        fixture.WithRoles(
        [
            new RoleInfo(5, "Manager", "Manager", "Project", [], true, 15),
            new RoleInfo(3, "Member", "Member", "Project", [], true, 10),
        ]);

        var result = await fixture.InvokeAsync(OwnerCaller(), "Member");

        AssertProblem(result, StatusCodes.Status502BadGateway, "upstream.role_service_unavailable");
        fixture.Users.VerifyNoOtherCalls();
        fixture.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnUserNotFound_Before_ProjectLookup()
    {
        var fixture = new Fixture();
        fixture.Users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(null));

        var result = await fixture.InvokeAsync(OwnerCaller(), "Member");

        AssertProblem(result, StatusCodes.Status404NotFound, "project.user_not_found");
        fixture.Repository.VerifyNoOtherCalls();
        fixture.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnProjectNotFound_When_ProjectMissing()
    {
        var fixture = new Fixture();
        fixture.Repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProjectAggregate?>(null));

        var result = await fixture.InvokeAsync(OwnerCaller(), "Member");

        AssertProblem(result, StatusCodes.Status404NotFound, "project.not_found");
        fixture.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_UseTheHighestOfTheCallersRolesInThisProject()
    {
        var fixture = new Fixture();
        var caller = CallerWithRoles(new()
        {
            ["member"] = new HashSet<ProjectShortName> { ShortName },
            ["OWNER"] = new HashSet<ProjectShortName> { ShortName },
        });

        var result = await fixture.InvokeAsync(caller, "Manager");

        Assert.IsType<NoContent>(result);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_IgnoreRolesTheCallerHoldsInOtherProjects()
    {
        var fixture = new Fixture();
        var caller = CallerWithRoles(new()
        {
            ["Manager"] = new HashSet<ProjectShortName> { ShortName },
            ["Admin"] = new HashSet<ProjectShortName> { ProjectShortName.Create("OTHER-1") },
        });

        var result = await fixture.InvokeAsync(caller, "Owner");

        AssertProblem(result, StatusCodes.Status403Forbidden, ProjectErrorCodes.CannotAssignEqualOrHigherRole);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnForbidden_When_TargetRoleIsBelowTheMemberBaseline()
    {
        var fixture = new Fixture();
        fixture.WithRoles([.. Roles(), new RoleInfo(6, "Guest", "Guest", "Project", [], true, 5)]);

        var result = await fixture.InvokeAsync(OwnerCaller(), "Guest");

        AssertProblem(result, StatusCodes.Status403Forbidden, ProjectErrorCodes.CannotAssignEqualOrHigherRole);
        Assert.Equal(AuditOutcome.Denied, Assert.Single(fixture.Audit.Events).Outcome);
    }

    [Fact]
    public async Task Should_SaveThenEvictThenAudit_When_RoleChangeCommits()
    {
        var fixture = new Fixture();
        var order = new List<string>();
        fixture.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("save")).ReturnsAsync(1);
        fixture.Cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string tag, CancellationToken _) => order.Add(tag)).Returns(ValueTask.CompletedTask);

        var result = await fixture.InvokeAsync(OwnerCaller(), "Member");

        Assert.IsType<NoContent>(result);
        Assert.Equal(["save", CacheTags.Project("PROJ-1"), CacheTags.UserInfo(TargetIdp)], order);
        Assert.Equal(AuditOutcome.Success, Assert.Single(fixture.Audit.Events).Outcome);
    }
}
