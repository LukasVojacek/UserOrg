using Core.Domain;
using Core.Domain.Shared;
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

public sealed class UnassignProjectUserEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private const string TargetIdp = "target-idp";

    private static readonly ProjectRoleId OwnerRoleId = ProjectRoleId.Create(4);
    private static readonly ProjectRoleId MemberRoleId = ProjectRoleId.Create(3);

    private static IList<RoleInfo> Roles() =>
    [
        new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20),
        new RoleInfo(3, "Member", "Member", "Project", [], true, 10),
    ];

    private static ProjectAggregate BuildProjectWithMember(out Domain.User.User member, ProjectRoleId role, EOperationMode mode = EOperationMode.Editable)
    {
        var project = ProjectAggregate.Create(
            ShortName, ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, mode);
        member = Domain.User.User.CreateUser(Email.Create("t@example.com"), UserName.Create("target"), IdpId.Create(TargetIdp));
        if (mode != EOperationMode.ReadOnly)
            project.AssignUserWithRole(member, UserId.New(), role);
        project.ClearDomainEvents();
        return project;
    }

    private static Caller CallerInScope() => new(
        new IdpId("u"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private static async Task<IResult> InvokeAsync(
        Caller caller, ProjectAggregate? project, Domain.User.User target,
        Mock<IUnitOfWork>? unitOfWork = null, Mock<IOutputCacheInvalidator>? cache = null, IList<RoleInfo>? catalog = null)
    {
        var repository = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(project));

        var users = new Mock<IRepository<Domain.User.User, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(target));

        var directory = new Mock<IUserDirectory>();
        directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserId.New());

        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(catalog ?? Roles()));

        unitOfWork ??= new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        cache ??= new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);

        return await UnassignProjectUserEndpoint.HandleAsync(
            new UnassignProjectUserEndpoint.Request(ShortName, TargetIdp),
            caller, repository.Object, users.Object, directory.Object,
            roleList.Object, Options.Create(new WellKnownRoles()), unitOfWork.Object, cache.Object,
            new RecordingAuditLogger<UnassignProjectUserEndpoint.AuditCategory>(), new DefaultHttpContext(), CancellationToken.None);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    [Fact]
    public async Task Should_ReturnNoContent_When_UnassigningMember()
    {
        var project = BuildProjectWithMember(out var member, MemberRoleId);
        var cache = new Mock<IOutputCacheInvalidator>();

        var result = await InvokeAsync(CallerInScope(), project, member, cache: cache);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        cache.Verify(c => c.EvictAsync(CacheTags.UserInfo(TargetIdp), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnConflict_When_TargetIsOwner()
    {
        var project = BuildProjectWithMember(out var member, OwnerRoleId);

        var result = await InvokeAsync(CallerInScope(), project, member);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_CallerOutOfScope()
    {
        var project = BuildProjectWithMember(out var member, MemberRoleId);
        var caller = new Caller(new IdpId("u"), "Owner", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

        var result = await InvokeAsync(caller, project, member);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_NotAssigned()
    {
        var project = ProjectAggregate.Create(
            ShortName, ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, EOperationMode.Editable);
        var stranger = Domain.User.User.CreateUser(Email.Create("s@example.com"), UserName.Create("stranger"), IdpId.Create(TargetIdp));

        var result = await InvokeAsync(CallerInScope(), project, stranger);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    private static IList<RoleInfo> UpperCaseCatalog() =>
    [
        new RoleInfo(3, "MEMBER", "Member", "Project", [], true, 10),
        new RoleInfo(4, "OWNER", "Owner", "Project", [], true, 20),
    ];

    [Fact]
    public async Task Should_ReturnNoContent_When_CatalogNameCasingDiffersFromConfiguredName()
    {
        var project = BuildProjectWithMember(out var member, MemberRoleId);

        var result = await InvokeAsync(CallerInScope(), project, member, catalog: UpperCaseCatalog());

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.Null(project.ActiveRoleOf(member));
    }

    [Fact]
    public async Task Should_ProtectCatalogOwnerRole_When_CatalogNameCasingDiffersFromConfiguredName()
    {
        var project = BuildProjectWithMember(out var member, OwnerRoleId);

        var result = await InvokeAsync(CallerInScope(), project, member, catalog: UpperCaseCatalog());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal(ProjectErrorCodes.OwnerCannotBeUnassigned, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(OwnerRoleId, project.ActiveRoleOf(member));
    }
}
