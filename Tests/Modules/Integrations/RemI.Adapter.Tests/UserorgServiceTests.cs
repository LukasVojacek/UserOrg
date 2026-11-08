using Core.DataAccess.Queries.Contracts;
using Core.Domain.Primitives;
using Core.Domain.Project;
using Core.Domain.Provider;
using Core.Domain.Shared;
using Core.Domain.User;
using Core.Shared;
using Forge.Core.Features.Role.Services;
using Lukdrasil.Result;
using Moq;

using Userorg.Adapter.RemI.Userorg;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories;
using IUserorgService = RemI.HostContracts.IUserorgService;

namespace RemI.Adapter.Tests;

public sealed class UserorgServiceTests
{
    private const int PageSize = 200;

    [Fact]
    public async Task Should_Map_UserSummary_Fields_When_Query_Returns_A_User()
    {
        var summary = new UserSummary(
            new IdpId("sub-1"),
            new Email("a@b.cz"),
            new UserName("alice"),
            [new ProjectShortName("PRJ1"), new ProjectShortName("PRJ2")],
            new ProviderName("it4i"));

        var sut = BuildSut(users: [summary]);

        var result = await sut.GetUsersAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = Assert.Single(result.Value);
        Assert.Equal("sub-1", user.IdpUserId);
        Assert.Equal("a@b.cz", user.Email);
        Assert.Equal("alice", user.UserName);
        Assert.Equal(["PRJ1", "PRJ2"], user.Projects);
        Assert.Equal("it4i", user.AssignedProviderName);
    }

    [Fact]
    public async Task Should_Map_A_Null_AssignedProviderName_To_Null()
    {
        var summary = new UserSummary(
            new IdpId("sub-1"),
            new Email("a@b.cz"),
            new UserName("alice"),
            [],
            null);

        var sut = BuildSut(users: [summary]);

        var result = await sut.GetUsersAsync(CancellationToken.None);

        Assert.Null(Assert.Single(result.Value).AssignedProviderName);
    }

    [Fact]
    public async Task Should_Map_ProjectSummary_Fields_When_Query_Returns_A_Project()
    {
        var summary = new ProjectSummary(
            new ProjectShortName("PRJ1"),
            new ProjectName("Project One"),
            new Email("owner@b.cz"),
            new ExternalId("ext-9"),
            EOperationMode.Editable,
            EProjectState.Active,
            new ServiceName("svc"),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero));

        var sut = BuildSut(projects: [summary]);

        var result = await sut.GetProjectsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var project = Assert.Single(result.Value);
        Assert.Equal("PRJ1", project.ShortName);
        Assert.Equal("Project One", project.Name);
        Assert.Equal("owner@b.cz", project.Email);
        Assert.Equal("ext-9", project.ExternalId);
        Assert.Equal("Editable", project.Mode);
        Assert.Equal("svc", project.ServiceName);
    }

    [Fact]
    public async Task Should_Map_Null_Project_Email_And_ExternalId_To_Null()
    {
        var summary = new ProjectSummary(
            new ProjectShortName("PRJ1"),
            new ProjectName("Project One"),
            null,
            null,
            EOperationMode.Unspecified,
            EProjectState.Active,
            new ServiceName("svc"),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

        var sut = BuildSut(projects: [summary]);

        var project = Assert.Single((await sut.GetProjectsAsync(CancellationToken.None)).Value);
        Assert.Null(project.Email);
        Assert.Null(project.ExternalId);
    }    

    [Fact]
    public async Task Should_Drain_Every_Page_When_Projects_Span_More_Than_One_Page()
    {
        var projects = Enumerable.Range(0, PageSize + 3)
            .Select(i => new ProjectSummary(
                new ProjectShortName($"P{i}"),
                new ProjectName($"Project {i}"),
                null,
                null,
                EOperationMode.ReadOnly,
                EProjectState.Active,
                new ServiceName("svc"),
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch))
            .ToList();

        var query = new Mock<IProjectQuery>();
        query.Setup(q => q.GetProjectsAsync(It.IsAny<ProjectFilter>(), It.IsAny<int>(), PageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectFilter _, int page, int limit, CancellationToken _) =>
                new QueryPage<ProjectSummary>([.. projects.Skip((page - 1) * limit).Take(limit)], projects.Count));

        var sut = new UserorgService(EmptyUserQuery(), query.Object, EmptyRoleListService(), EmptyUserPermissionRepository());

        var result = await sut.GetProjectsAsync(CancellationToken.None);

        Assert.Equal(projects.Count, result.Value.Count);
    }

    [Fact]
    public async Task Should_Succeed_With_An_Empty_List_When_There_Are_No_Rows()
    {
        var sut = BuildSut();

        var users = await sut.GetUsersAsync(CancellationToken.None);
        var projects = await sut.GetProjectsAsync(CancellationToken.None);

        Assert.True(users.IsSuccess);
        Assert.Empty(users.Value);
        Assert.True(projects.IsSuccess);
        Assert.Empty(projects.Value);
    }

    [Fact]
    public async Task Should_Fail_When_The_User_Query_Throws()
    {
        var query = new Mock<IUserInfoQuery>();
        query.Setup(q => q.GetUsersAsync(It.IsAny<UserFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var sut = new UserorgService(query.Object, EmptyProjectQuery(), EmptyRoleListService(), EmptyUserPermissionRepository());

        var result = await sut.GetUsersAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Should_Fail_When_The_Project_Query_Throws()
    {
        var query = new Mock<IProjectQuery>();
        query.Setup(q => q.GetProjectsAsync(It.IsAny<ProjectFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var sut = new UserorgService(EmptyUserQuery(), query.Object, EmptyRoleListService(), EmptyUserPermissionRepository());

        var result = await sut.GetProjectsAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    private static IUserorgService BuildSut(
        IReadOnlyList<UserSummary>? users = null,
        IReadOnlyList<ProjectSummary>? projects = null)
    {
        var userQuery = new Mock<IUserInfoQuery>();
        userQuery.Setup(q => q.GetUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => users?.Select(u => new UserWithIdSummary(UserId.New(), u.IdentityProviderId, u.Email, u.UserName, u.Projects, u.AssignedProviderName)).ToList() ?? []);

        var projectQuery = new Mock<IProjectQuery>();
        projectQuery.Setup(q => q.GetProjectsAsync(It.IsAny<ProjectFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryPage<ProjectSummary>(projects ?? [], projects?.Count ?? 0));

        return new UserorgService(userQuery.Object, projectQuery.Object, EmptyRoleListService(), EmptyUserPermissionRepository());
    }

    private static IUserInfoQuery PagedUserQuery(IReadOnlyList<UserSummary> all, out List<int> requestedPages)
    {
        var pages = new List<int>();
        requestedPages = pages;

        var query = new Mock<IUserInfoQuery>();
        query.Setup(q => q.GetUsersAsync(It.IsAny<UserFilter>(), It.IsAny<int>(), PageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserFilter _, int page, int limit, CancellationToken _) =>
            {
                pages.Add(page);
                return new QueryPage<UserSummary>([.. all.Skip((page - 1) * limit).Take(limit)], all.Count);
            });

        return query.Object;
    }

    private static IUserInfoQuery EmptyUserQuery()
    {
        var query = new Mock<IUserInfoQuery>();
        query.Setup(q => q.GetUsersAsync(It.IsAny<UserFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryPage<UserSummary>([], 0));
        return query.Object;
    }

    private static IProjectQuery EmptyProjectQuery()
    {
        var query = new Mock<IProjectQuery>();
        query.Setup(q => q.GetProjectsAsync(It.IsAny<ProjectFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryPage<ProjectSummary>([], 0));
        return query.Object;
    }

    private static IRoleListService EmptyRoleListService()
    {
        var service = new Mock<IRoleListService>();
        service.Setup(s => s.ListRoles(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Result<IList<RoleInfo>, Lukdrasil.Error.Error>.Success([], State.Ok)));
        return service.Object;
    }

    private static IUserPermissionRepository EmptyUserPermissionRepository()
    {
        var repository = new Mock<IUserPermissionRepository>();
        repository.Setup(r => r.GetPermissionsForUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new List<UserOrganizationManagement.Domain.Entities.UserPermission>()));
        return repository.Object;
    }
}
