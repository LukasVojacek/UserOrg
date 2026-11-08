using Core.Domain;
using Core.Domain.Shared;
using Core.Features.Project.AssignProjectUser;
using Core.Features.Project.ChangeProjectUserRole;
using Core.Features.Project.UnassignProjectUser;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.Options;

using Moq;

using DomainUser = Core.Domain.User.User;
using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.UnitTests.Projects;

/// <summary>
/// The gateway keys permission snapshots by the stored IdP id, ordinally, so the header names the loaded
/// user's id even when the route spells it in another case.
/// </summary>
public sealed class MembershipEndpointsStoredIdpIdTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private const string StoredIdp = "target-idp";
    private const string RouteIdp = "TARGET-IDP";
    private const int MemberRoleId = 3;
    private const int OwnerRoleId = 4;

    private static IList<RoleInfo> Roles() =>
    [
        new RoleInfo(OwnerRoleId, "Owner", "Owner", "Project", [], true, 20),
        new RoleInfo(MemberRoleId, "Member", "Member", "Project", [], true, 10),
    ];

    private static Caller CallerInScope() => new(
        new IdpId("u"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private static DomainUser StoredUser() =>
        DomainUser.CreateUser(Email.Create($"{StoredIdp}@example.com"), UserName.Create(StoredIdp), IdpId.Create(StoredIdp));

    private static ProjectAggregate BuildProject(params DomainUser[] members)
    {
        var project = ProjectAggregate.Create(
            ShortName, ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, EOperationMode.Editable);
        foreach (var member in members)
            project.AssignUserWithRole(member, UserId.New(), ProjectRoleId.Create(MemberRoleId));
        project.ClearDomainEvents();
        return project;
    }

    private static IRepository<ProjectAggregate, ProjectShortName> Projects(ProjectAggregate project)
    {
        var repository = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProjectAggregate?>(project));
        return repository.Object;
    }

    private static IRepository<DomainUser, IdpId> Users(DomainUser user)
    {
        var users = new Mock<IRepository<DomainUser, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<DomainUser?>(user));
        return users.Object;
    }

    private static IUserDirectory Directory()
    {
        var directory = new Mock<IUserDirectory>();
        directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>())).ReturnsAsync(UserId.New());
        return directory.Object;
    }

    private static IRoleListService RoleList()
    {
        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(Roles()));
        return roleList.Object;
    }

    private static IUnitOfWork UnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork.Object;
    }

    private static IOutputCacheInvalidator Cache()
    {
        var cache = new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        return cache.Object;
    }

    [Fact]
    public async Task Should_NameTheStoredIdpId_When_AssignRouteIdDiffersInCase()
    {
        var context = PermissionsChangedResponse.Create();

        await AssignProjectUserEndpoint.HandleAsync(
            new AssignProjectUserEndpoint.Request(ShortName, RouteIdp),
            CallerInScope(), Projects(BuildProject()), Users(StoredUser()), Directory(), RoleList(),
            Options.Create(new WellKnownRoles()), UnitOfWork(), Cache(),
            new RecordingAuditLogger<AssignProjectUserEndpoint.AuditCategory>(), context, CancellationToken.None);

        Assert.Equal([StoredIdp], PermissionsChangedResponse.Ids(await PermissionsChangedResponse.StartAsync(context)));
    }

    [Fact]
    public async Task Should_NameTheStoredIdpId_When_UnassignRouteIdDiffersInCase()
    {
        var context = PermissionsChangedResponse.Create();
        var target = StoredUser();

        await UnassignProjectUserEndpoint.HandleAsync(
            new UnassignProjectUserEndpoint.Request(ShortName, RouteIdp),
            CallerInScope(), Projects(BuildProject(target)), Users(target), Directory(), RoleList(),
            Options.Create(new WellKnownRoles()), UnitOfWork(), Cache(),
            new RecordingAuditLogger<UnassignProjectUserEndpoint.AuditCategory>(), context, CancellationToken.None);

        Assert.Equal([StoredIdp], PermissionsChangedResponse.Ids(await PermissionsChangedResponse.StartAsync(context)));
    }

    [Fact]
    public async Task Should_NameTheStoredIdpId_When_ChangeRoleRouteIdDiffersInCase()
    {
        var context = PermissionsChangedResponse.Create();

        await ChangeProjectUserRoleEndpoint.HandleAsync(
            new ChangeProjectUserRoleEndpoint.Request(ShortName, RouteIdp, new ChangeProjectUserRoleEndpoint.Body("Member")),
            CallerInScope(), Projects(BuildProject()), Users(StoredUser()), RoleList(),
            Options.Create(new WellKnownRoles()), UnitOfWork(), Cache(),
            new RecordingAuditLogger<ChangeProjectUserRoleEndpoint.AuditCategory>(), context, CancellationToken.None);

        Assert.Equal([StoredIdp], PermissionsChangedResponse.Ids(await PermissionsChangedResponse.StartAsync(context)));
    }
}