using Forge.Core.Features.Role.Services;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using UserOrg.Shared;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Application.UserOrg.Services;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;
using UserOrganizationManagement.Domain.ValueObjects;

public sealed class AdministrationServiceTests
{
    private static readonly ProjectId _projectId = ProjectId.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private const string _projectShortName = "TESTPRJ";
    private const string _idpUserId = "test-idp-user-123";

    [Fact]
    public async Task ResetAllCachesAsync_Should_evict_static_cache_keys()
    {
        var (service, cache) = CreateService([], []);

        var snapshotCallCount = 0;
        var jwksCallCount = 0;
        await cache.GetOrCreateAsync("EndpointPermissionService:Snapshot", _ => { snapshotCallCount++; return ValueTask.FromResult(42); });
        await cache.GetOrCreateAsync("jwks:json", _ => { jwksCallCount++; return ValueTask.FromResult("key"); });

        await service.ResetAllCachesAsync(CancellationToken.None);

        await cache.GetOrCreateAsync("EndpointPermissionService:Snapshot", _ => { snapshotCallCount++; return ValueTask.FromResult(42); });
        await cache.GetOrCreateAsync("jwks:json", _ => { jwksCallCount++; return ValueTask.FromResult("key"); });

        Assert.Equal(2, snapshotCallCount);
        Assert.Equal(2, jwksCallCount);
    }

    [Fact]
    public async Task ResetAllCachesAsync_Should_evict_UserInfo_tag_for_each_user()
    {
        var users = new[] { new UserModel(_idpUserId, "test@example.com", "testuser", [], null) };
        var (service, cache) = CreateService(users, []);

        var factoryCallCount = 0;
        await cache.GetOrCreateAsync(
            $"UserInfoModel_{_idpUserId}_v1",
            _ => { factoryCallCount++; return ValueTask.FromResult(42); },
            tags: [$"UserInfoModel_{_idpUserId}"]);

        await service.ResetAllCachesAsync(CancellationToken.None);

        await cache.GetOrCreateAsync(
            $"UserInfoModel_{_idpUserId}_v1",
            _ => { factoryCallCount++; return ValueTask.FromResult(42); },
            tags: [$"UserInfoModel_{_idpUserId}"]);

        Assert.Equal(2, factoryCallCount);
    }

    [Fact]
    public async Task ResetAllCachesAsync_Should_evict_UserInfoExtended_tag_for_each_user()
    {
        var users = new[] { new UserModel(_idpUserId, "test@example.com", "testuser", [], null) };
        var (service, cache) = CreateService(users, []);

        var factoryCallCount = 0;
        await cache.GetOrCreateAsync(
            $"UserInfoModel_Extended_{_idpUserId}_v1",
            _ => { factoryCallCount++; return ValueTask.FromResult(42); },
            tags: [$"UserInfoModel_Extended_{_idpUserId}"]);

        await service.ResetAllCachesAsync(CancellationToken.None);

        await cache.GetOrCreateAsync(
            $"UserInfoModel_Extended_{_idpUserId}_v1",
            _ => { factoryCallCount++; return ValueTask.FromResult(42); },
            tags: [$"UserInfoModel_Extended_{_idpUserId}"]);

        Assert.Equal(2, factoryCallCount);
    }

    [Fact]
    public async Task ResetAllCachesAsync_Should_evict_TemplatesInfo_tag_for_each_project()
    {
        var projects = new[] { new ProjectModel { Id = _projectId, ShortName = _projectShortName } };
        var (service, cache) = CreateService([], projects);

        var factoryCallCount = 0;
        var tag = CacheTags.TemplatesInfo(_projectShortName);
        await cache.GetOrCreateAsync(
            "templates-key",
            _ => { factoryCallCount++; return ValueTask.FromResult("data"); },
            tags: [tag]);

        await service.ResetAllCachesAsync(CancellationToken.None);

        await cache.GetOrCreateAsync(
            "templates-key",
            _ => { factoryCallCount++; return ValueTask.FromResult("data"); },
            tags: [tag]);

        Assert.Equal(2, factoryCallCount);
    }

    [Fact]
    public async Task ResetAllCachesAsync_Should_evict_HEAppE_permission_project_tag_for_each_project()
    {
        var projects = new[] { new ProjectModel { Id = _projectId, ShortName = _projectShortName } };
        var (service, cache) = CreateService([], projects);

        var factoryCallCount = 0;
        var tag = $"CmdTemplatePerm_Project_{(Guid)_projectId}";
        await cache.GetOrCreateAsync(
            "heappe-perm-key",
            _ => { factoryCallCount++; return ValueTask.FromResult("perms"); },
            tags: [tag]);

        await service.ResetAllCachesAsync(CancellationToken.None);

        await cache.GetOrCreateAsync(
            "heappe-perm-key",
            _ => { factoryCallCount++; return ValueTask.FromResult("perms"); },
            tags: [tag]);

        Assert.Equal(2, factoryCallCount);
    }

    [Fact]
    public async Task ResetAllCachesAsync_Should_evict_untagged_entries_and_tags_no_producer_can_enumerate()
    {
        var (service, cache) = CreateService([], []);

        var taggedCallCount = 0;
        var untaggedCallCount = 0;
        var heappeClusterTag = CacheTags.HeappeClusters(new UserOrg.Shared.HeappeIdentifier("heappe-example"));
        await cache.GetOrCreateAsync(
            "heappe-cluster-key",
            _ => { taggedCallCount++; return ValueTask.FromResult("cluster-data"); },
            tags: [heappeClusterTag]);
        await cache.GetOrCreateAsync("untagged-key", _ => { untaggedCallCount++; return ValueTask.FromResult("data"); });

        await cache.GetOrCreateAsync(
            "heappe-cluster-key",
            _ => { taggedCallCount++; return ValueTask.FromResult("cluster-data"); },
            tags: [heappeClusterTag]);
        await cache.GetOrCreateAsync("untagged-key", _ => { untaggedCallCount++; return ValueTask.FromResult("data"); });
        Assert.Equal(1, taggedCallCount);
        Assert.Equal(1, untaggedCallCount);

        await service.ResetAllCachesAsync(CancellationToken.None);

        await cache.GetOrCreateAsync(
            "heappe-cluster-key",
            _ => { taggedCallCount++; return ValueTask.FromResult("cluster-data"); },
            tags: [heappeClusterTag]);
        await cache.GetOrCreateAsync("untagged-key", _ => { untaggedCallCount++; return ValueTask.FromResult("data"); });

        Assert.Equal(2, taggedCallCount);
        Assert.Equal(2, untaggedCallCount);
    }

    private static (AdministrationService Service, HybridCache Cache) CreateService(
        IEnumerable<UserModel> users,
        IEnumerable<ProjectModel> projects)
    {
        var mockUow = new Mock<IUserOrgUnitOfWork>();
        var mockRoleList = new Mock<IRoleListService>();

        var mockUserService = new Mock<IUserService>();
        mockUserService
            .Setup(s => s.GetUsersAsync(It.IsAny<ProjectShortName?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        var mockProjectService = new Mock<IProjectService>();
        mockProjectService
            .Setup(s => s.GetProjectsAsync(It.IsAny<ProjectShortName?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(projects.ToList());

        var cache = new ServiceCollection()
            .AddHybridCache()
            .Services
            .BuildServiceProvider()
            .GetRequiredService<HybridCache>();

        var service = new AdministrationService(
            mockUserService.Object,
            mockProjectService.Object,
            mockUow.Object,
            mockRoleList.Object,
            cache);

        return (service, cache);
    }

    [Fact]
    public async Task Should_ReportDeactivatedUsersInBothGroups_When_GetSyncDetails()
    {
        // Group 1 (provider projects): a soft-deleted user with a live membership link stays in the list.
        // Group 2 (everyone else): the id source must include soft-deleted users so the consumer sees
        // IsDeleted = true instead of the user silently vanishing (#52).
        var activeMember = new User { IdentityProviderUserId = "idp-active", IsDeleted = false };
        var deactivatedMember = new User { IdentityProviderUserId = "idp-deactivated", IsDeleted = true };
        var project = new Project { Id = _projectId, ShortName = ProjectShortName.Create(_projectShortName) };
        project.AssignedUsers.Add(new AssignedUserToProject { User = activeMember, ProjectId = _projectId });
        project.AssignedUsers.Add(new AssignedUserToProject { User = deactivatedMember, ProjectId = _projectId });

        var mockProjectService = new Mock<IProjectService>();
        mockProjectService
            .Setup(s => s.GetProjectsForProvider(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([project]);

        var mockUserRepository = new Mock<UserOrganizationManagement.Domain.IRepositories.IUserRepository>();
        mockUserRepository
            .Setup(r => r.GetAllIdpUserIdsIncludingDeletedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["idp-active", "idp-deactivated", "idp-deactivated-elsewhere"]);
        var mockUow = new Mock<IUserOrgUnitOfWork>();
        mockUow.Setup(u => u.UserRepository).Returns(mockUserRepository.Object);

        var mockUserService = new Mock<IUserService>();
        mockUserService
            .Setup(s => s.GetUserProjectsRoleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<ProjectShortName, string>());
        mockUserService
            .Setup(s => s.PopulateExtendedUserInfo(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<ProjectId>>(),
                It.IsAny<IDictionary<string, HashSet<ProjectShortName>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string idp, IEnumerable<ProjectId> _, IDictionary<string, HashSet<ProjectShortName>> _, CancellationToken _) =>
                new UserInfoExtendedModel { KeycloakSid = idp, IsDeleted = idp.StartsWith("idp-deactivated") });

        var cache = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider().GetRequiredService<HybridCache>();
        var service = new AdministrationService(
            mockUserService.Object, mockProjectService.Object, mockUow.Object, new Mock<IRoleListService>().Object, cache);

        var result = await service.GetSyncDetails(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(["idp-active", "idp-deactivated", "idp-deactivated-elsewhere"], result.Select(r => r.KeycloakSid));
        Assert.Equal([false, true, true], result.Select(r => r.IsDeleted));
        mockUserService.Verify(s => s.PopulateExtendedUserInfo(
            "idp-deactivated",
            It.Is<IEnumerable<ProjectId>>(f => f != null && f.Contains(_projectId)),
            It.IsAny<IDictionary<string, HashSet<ProjectShortName>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        mockUserService.Verify(s => s.PopulateExtendedUserInfo(
            "idp-deactivated-elsewhere",
            null!,
            It.IsAny<IDictionary<string, HashSet<ProjectShortName>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
