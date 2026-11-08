using Core.Domain;
using Core.Domain.Shared;
using Core.Features.Project.AssignProjectUser;
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

public sealed class AssignProjectUserEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private const string TargetIdp = "target-idp";

    private static IList<RoleInfo> Roles() =>
    [
        new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20),
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

    private static Caller CallerInScope() => new(
        new IdpId("u"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private static async Task<IResult> InvokeAsync(
        Caller caller,
        ProjectAggregate? project,
        UserId? assignedBy = null,
        Domain.User.User? target = null,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<IOutputCacheInvalidator>? cache = null,
        IList<RoleInfo>? catalog = null)
    {
        var repository = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(project));

        var users = new Mock<IRepository<Domain.User.User, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(target ?? Domain.User.User.CreateUser(
                Email.Create("t@example.com"), UserName.Create("target"), IdpId.Create(TargetIdp))));

        var directory = new Mock<IUserDirectory>();
        directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignedBy ?? UserId.New());

        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(catalog ?? Roles()));

        unitOfWork ??= new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        cache ??= new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);

        return await AssignProjectUserEndpoint.HandleAsync(
            new AssignProjectUserEndpoint.Request(ShortName, TargetIdp),
            caller, repository.Object, users.Object, directory.Object,
            roleList.Object, Options.Create(new WellKnownRoles()), unitOfWork.Object, cache.Object,
            new RecordingAuditLogger<AssignProjectUserEndpoint.AuditCategory>(), new DefaultHttpContext(), CancellationToken.None);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    [Fact]
    public async Task Should_ReturnNoContent_When_AssigningMember()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        var cache = new Mock<IOutputCacheInvalidator>();

        var result = await InvokeAsync(CallerInScope(), BuildProject(), unitOfWork: unitOfWork, cache: cache);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.EvictAsync(CacheTags.Project(ShortName.Value), It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.EvictAsync(CacheTags.UserInfo(TargetIdp), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_CallerOutOfScope()
    {
        var caller = new Caller(new IdpId("u"), "Owner", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

        var result = await InvokeAsync(caller, BuildProject());

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_ProjectMissing()
    {
        var result = await InvokeAsync(CallerInScope(), project: null);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnConflict_When_ProjectReadOnly()
    {
        var result = await InvokeAsync(CallerInScope(), BuildProject(EOperationMode.ReadOnly));

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotFound_With_ProjectNotFoundCode_When_CallerOutOfScope()
    {
        var caller = new Caller(new IdpId("u"), "Owner", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

        var problem = Assert.IsType<ProblemHttpResult>(await InvokeAsync(caller, BuildProject()).ConfigureAwait(false));
        Assert.Equal("project.not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnNotFound_With_CallerNotProvisionedCode_When_CallerNotInDirectory()
    {
        var problem = Assert.IsType<ProblemHttpResult>(
            await InvokeWithOverridesAsync(callerProvisioned: false).ConfigureAwait(false));
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("caller.not_provisioned", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnNotFound_With_UserNotFoundCode_When_TargetMissing()
    {
        var problem = Assert.IsType<ProblemHttpResult>(
            await InvokeWithOverridesAsync(targetExists: false).ConfigureAwait(false));
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("project.user_not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnBadGateway_With_UpstreamCode_When_RoleServiceFails()
    {
        var problem = Assert.IsType<ProblemHttpResult>(
            await InvokeWithOverridesAsync(rolesSucceed: false).ConfigureAwait(false));
        Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
        Assert.Equal("upstream.role_service_unavailable", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("Failed to retrieve roles.", problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_ReturnBadGateway_With_MemberRoleMissingDetail_When_CatalogLacksMemberRole()
    {
        var unitOfWork = new Mock<IUnitOfWork>();

        var result = await InvokeAsync(
            CallerInScope(), BuildProject(), unitOfWork: unitOfWork,
            catalog: [new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20)]);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
        Assert.Equal("upstream.role_service_unavailable", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("Project member role is not configured.", problem.ProblemDetails.Detail);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_AssignCatalogMemberRole_When_CatalogNameCasingDiffersFromConfiguredName()
    {
        var project = BuildProject();
        var target = Domain.User.User.CreateUser(Email.Create("t@example.com"), UserName.Create("target"), IdpId.Create(TargetIdp));

        var result = await InvokeAsync(
            CallerInScope(), project, target: target,
            catalog: [new RoleInfo(4, "OWNER", "Owner", "Project", [], true, 20), new RoleInfo(3, "MEMBER", "Member", "Project", [], true, 10)]);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.Equal(ProjectRoleId.Create(3), project.ActiveRoleOf(target));
    }

    private static async Task<IResult> InvokeWithOverridesAsync(
        bool callerProvisioned = true,
        bool targetExists = true,
        bool rolesSucceed = true)
    {
        var repository = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProjectAggregate?>(BuildProject()));

        var users = new Mock<IRepository<Domain.User.User, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(targetExists
                ? Domain.User.User.CreateUser(Email.Create("t@example.com"), UserName.Create("target"), IdpId.Create(TargetIdp))
                : null));

        var directory = new Mock<IUserDirectory>();
        directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerProvisioned ? UserId.New() : (UserId?)null);

        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rolesSucceed
                ? Result<IList<RoleInfo>, Error>.Success(Roles())
                : Result<IList<RoleInfo>, Error>.Failure(new Error("RoleServiceDown", ErrorSeverity.Error, "down")));

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var cache = new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);

        return await AssignProjectUserEndpoint.HandleAsync(
            new AssignProjectUserEndpoint.Request(ShortName, TargetIdp),
            CallerInScope(), repository.Object, users.Object, directory.Object,
            roleList.Object, Options.Create(new WellKnownRoles()), unitOfWork.Object, cache.Object,
            new RecordingAuditLogger<AssignProjectUserEndpoint.AuditCategory>(), new DefaultHttpContext(), CancellationToken.None);
    }
}
