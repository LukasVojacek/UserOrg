using Core.Domain;
using Core.Domain.Shared;
using Core.Features.Project.AssignProjectUser;
using Core.Features.Project.UnassignProjectUser;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.UnitTests.Projects;

/// <summary>
/// Covers the guards of <see cref="AssignProjectUserEndpoint.HandleAsync"/> and
/// <see cref="UnassignProjectUserEndpoint.HandleAsync"/> that the existing suites leave open (T-197-03).
/// </summary>
public sealed class ProjectUserAssignmentEndpointsGuardTests
{
    private const string Assign = "AssignProjectUser";
    private const string Unassign = "UnassignProjectUser";
    private const string TargetIdp = "target-idp";

    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");

    public static TheoryData<string> Endpoints() => new() { Assign, Unassign };

    private sealed class Fixture
    {
        public Mock<IRepository<ProjectAggregate, ProjectShortName>> Repository { get; } = new();
        public Mock<IRepository<Domain.User.User, IdpId>> Users { get; } = new();
        public Mock<IUserDirectory> Directory { get; } = new();
        public Mock<IRoleListService> RoleList { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public List<string> EvictedTags { get; } = [];
        public Mock<IOutputCacheInvalidator> Cache { get; } = new();

        public Fixture(ProjectAggregate? project, Domain.User.User target)
        {
            Repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.FromResult(project));
            Users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.FromResult<Domain.User.User?>(target));
            Directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(UserId.New());
            WithRoles(Roles());
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback((string tag, CancellationToken _) => EvictedTags.Add(tag))
                .Returns(ValueTask.CompletedTask);
        }

        public void WithRoles(IList<RoleInfo> roles) =>
            RoleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(roles));

        public Task<IResult> InvokeAsync(string endpoint, Caller? caller = null) => endpoint switch
        {
            Assign => AssignProjectUserEndpoint.HandleAsync(
                new AssignProjectUserEndpoint.Request(ShortName, TargetIdp), caller ?? InScopeCaller(),
                Repository.Object, Users.Object, Directory.Object, RoleList.Object,
                Options.Create(new WellKnownRoles()), UnitOfWork.Object, Cache.Object, new RecordingAuditLogger<AssignProjectUserEndpoint.AuditCategory>(), new DefaultHttpContext(), CancellationToken.None),
            Unassign => UnassignProjectUserEndpoint.HandleAsync(
                new UnassignProjectUserEndpoint.Request(ShortName, TargetIdp), caller ?? InScopeCaller(),
                Repository.Object, Users.Object, Directory.Object, RoleList.Object,
                Options.Create(new WellKnownRoles()), UnitOfWork.Object, Cache.Object, new RecordingAuditLogger<UnassignProjectUserEndpoint.AuditCategory>(), new DefaultHttpContext(), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, null),
        };

        public void VerifyNothingWritten()
        {
            UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            Assert.Empty(EvictedTags);
        }
    }

    private static IList<RoleInfo> Roles() =>
    [
        new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20),
        new RoleInfo(3, "Member", "Member", "Project", [], true, 10),
    ];

    private static Domain.User.User Target() =>
        Domain.User.User.CreateUser(Email.Create("t@example.com"), UserName.Create("target"), IdpId.Create(TargetIdp));

    private static ProjectAggregate BuildProject()
    {
        var project = ProjectAggregate.Create(
            ShortName, ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, EOperationMode.Editable);
        project.ClearDomainEvents();
        return project;
    }

    private static (ProjectAggregate Project, Domain.User.User Target) ReadyFor(string endpoint)
    {
        var project = BuildProject();
        var target = Target();
        if (endpoint == Unassign)
        {
            project.AssignUserWithRole(target, UserId.New(), ProjectRoleId.Create(3));
            project.ClearDomainEvents();
        }

        return (project, target);
    }

    private static Caller InScopeCaller() => new(
        new IdpId("u"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private static void AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Should_ReturnProjectNotFound_Before_DirectoryLookup_When_CallerOutOfScope(string endpoint)
    {
        var fixture = new Fixture(BuildProject(), Target());
        var caller = new Caller(new IdpId("u"), "Owner", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

        var result = await fixture.InvokeAsync(endpoint, caller);

        AssertProblem(result, StatusCodes.Status404NotFound, "project.not_found");
        fixture.Directory.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Should_ReturnCallerNotProvisioned_Before_TargetLookup(string endpoint)
    {
        var fixture = new Fixture(BuildProject(), Target());
        fixture.Directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserId?)null);

        var result = await fixture.InvokeAsync(endpoint);

        AssertProblem(result, StatusCodes.Status404NotFound, "caller.not_provisioned");
        fixture.Users.VerifyNoOtherCalls();
        fixture.VerifyNothingWritten();
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Should_ReturnUserNotFound_Before_RoleLookup(string endpoint)
    {
        var fixture = new Fixture(BuildProject(), Target());
        fixture.Users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(null));

        var result = await fixture.InvokeAsync(endpoint);

        AssertProblem(result, StatusCodes.Status404NotFound, "project.user_not_found");
        fixture.RoleList.VerifyNoOtherCalls();
        fixture.VerifyNothingWritten();
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Should_ReturnUpstream_Before_ProjectLookup_When_RoleServiceFails(string endpoint)
    {
        var fixture = new Fixture(BuildProject(), Target());
        fixture.RoleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Failure(new Error("RoleServiceDown", ErrorSeverity.Error, "down")));

        var result = await fixture.InvokeAsync(endpoint);

        AssertProblem(result, StatusCodes.Status502BadGateway, "upstream.role_service_unavailable");
        fixture.Repository.VerifyNoOtherCalls();
        fixture.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnUpstream_When_AssignFindsNoMemberRole()
    {
        var fixture = new Fixture(BuildProject(), Target());
        fixture.WithRoles([new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20)]);

        var result = await fixture.InvokeAsync(Assign);

        AssertProblem(result, StatusCodes.Status502BadGateway, "upstream.role_service_unavailable");
        fixture.Repository.VerifyNoOtherCalls();
        fixture.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnUpstream_When_UnassignFindsNoOwnerRole()
    {
        var fixture = new Fixture(BuildProject(), Target());
        fixture.WithRoles([new RoleInfo(3, "Member", "Member", "Project", [], true, 10)]);

        var result = await fixture.InvokeAsync(Unassign);

        AssertProblem(result, StatusCodes.Status502BadGateway, "upstream.role_service_unavailable");
        fixture.Repository.VerifyNoOtherCalls();
        fixture.VerifyNothingWritten();
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Should_ReturnProjectNotFound_When_ProjectMissing(string endpoint)
    {
        var fixture = new Fixture(project: null, Target());

        var result = await fixture.InvokeAsync(endpoint);

        AssertProblem(result, StatusCodes.Status404NotFound, "project.not_found");
        fixture.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnUserAlreadyAssigned_When_AssigningAnActiveMember()
    {
        var (project, target) = ReadyFor(Unassign);
        var fixture = new Fixture(project, target);

        var result = await fixture.InvokeAsync(Assign);

        AssertProblem(result, StatusCodes.Status409Conflict, ProjectErrorCodes.UserAlreadyAssigned);
        fixture.VerifyNothingWritten();
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Should_SaveAndEvictProjectThenUserInfo_When_Valid(string endpoint)
    {
        var (project, target) = ReadyFor(endpoint);
        var fixture = new Fixture(project, target);

        var result = await fixture.InvokeAsync(endpoint);

        Assert.IsType<NoContent>(result);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal([CacheTags.Project("PROJ-1"), CacheTags.UserInfo(TargetIdp)], fixture.EvictedTags);
    }
}
