using Core.Domain.Exceptions;

using Forge.Core.Features.Role.Services;

using Heappe.Contracts;
using Heappe.Contracts.CommandTemplate;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

using Moq;

using UserOrg.Shared;
using UserOrg.Shared.Extensions;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Application.UserOrg.Services;
using UserOrganizationManagement.Domain.Contracts;
using UserOrganizationManagement.Domain.Exceptions;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;
using UserOrganizationManagement.Domain.ValueObjects;

// Disambiguate from main's new Heappe.Contracts.IHeappeConnectionService now in scope after the merge (ADR 0033).
using IHeappeConnectionService = UserOrganizationManagement.Application.UserOrg.IServices.IHeappeConnectionService;

public sealed class ProjectServiceTests
{
    private const string BearerToken = "<ACCES_TOKEN>";
    private static readonly ProjectShortName _projectShortName = new("TestProject");

    // Defence in depth for #280 (plan-ready criterion 9): the nav-collection add at
    // AssignedUserToProjectWithRoleAsync's UserPermissions.Add(...) must be paired with an explicit
    // InsertAsync, mirroring the safe pattern at ProjectService.cs:227-235. B03 owns the
    // change-tracker EntityState.Added regression test against a real DbContext; this test stays at
    // the service level and asserts the explicit insert call instead.
    [Fact]
    public async Task Should_InsertAsync_new_UserPermission_When_user_newly_assigned_to_project()
    {
        var mockUnitOfWork = new Mock<IUserOrgUnitOfWork>();
        var mockRoleListService = new Mock<IRoleListService>();
        var cache = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider().GetRequiredService<HybridCache>();
        var projectService = new ProjectService(
            mockUnitOfWork.Object, new Mock<IHttpClientFactory>().Object, new Mock<IFeatureManager>().Object,
            cache, new Mock<IHeappeConnectionService>().Object, new Mock<ILogger<ProjectService>>().Object, mockRoleListService.Object);

        var project = new UserOrganizationManagement.Domain.Entities.Project
        {
            ShortName = _projectShortName,
            Id = ProjectId.Create(Guid.NewGuid())
        };
        var user = new UserOrganizationManagement.Domain.Entities.User { IdentityProviderUserId = "idp-user" };
        var assignedByUser = new UserOrganizationManagement.Domain.Entities.User();
        var memberRole = new RoleInfo(1, RoleInfoConstants.ProjectMemberRoleName, "Member", "Project", [], true, 1);

        mockUnitOfWork.Setup(u => u.ProjectRepository.GetByShortNameAsync(_projectShortName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);
        mockUnitOfWork.Setup(u => u.UserRepository.GetBySidWithProjectsAsync("idp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        mockUnitOfWork.Setup(u => u.UserRepository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignedByUser);
        mockUnitOfWork.Setup(u => u.UserPermissionRepository.InsertAsync(It.IsAny<UserOrganizationManagement.Domain.Entities.UserPermission>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mockRoleListService.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success([memberRole], State.Ok));

        await projectService.AssignedUserToProjectWithRoleAsync(
            _projectShortName, "idp-user", Guid.NewGuid(), RoleInfoConstants.ProjectMemberRoleName, CancellationToken.None);

        mockUnitOfWork.Verify(
            u => u.UserPermissionRepository.InsertAsync(
                It.Is<UserOrganizationManagement.Domain.Entities.UserPermission>(p => p.User == user && p.Project == project),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Contains(project.UserPermissions, p => p.User == user);
    }

    [Fact]
    public async Task Should_pass_providerId_through_to_repository_When_supplied()
    {
        var mockUnitOfWork = new Mock<IUserOrgUnitOfWork>();
        var providerId = Guid.NewGuid();
        var scopedProject = new UserOrganizationManagement.Domain.Entities.Project
        {
            ShortName = _projectShortName,
            Id = ProjectId.Create(Guid.NewGuid()),
            ProjectDomainType = new UserOrganizationManagement.Domain.Entities.ProjectDomainType()
        };

        mockUnitOfWork
            .Setup(u => u.ProjectRepository.GetAllBySpecificationsAsync(null, null, null, null, null, providerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserOrganizationManagement.Domain.Entities.Project> { scopedProject });

        var projectService = CreateProjectServiceWithMockUnitOfWork(mockUnitOfWork);

        var result = await projectService.GetProjectsAsync(null, null, null, null, null, providerId, CancellationToken.None);

        Assert.Single(result);
        mockUnitOfWork.Verify(
            u => u.ProjectRepository.GetAllBySpecificationsAsync(null, null, null, null, null, providerId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_return_full_list_When_providerId_null()
    {
        var mockUnitOfWork = new Mock<IUserOrgUnitOfWork>();
        var projectA = new UserOrganizationManagement.Domain.Entities.Project
        {
            ShortName = new ProjectShortName("A"),
            Id = ProjectId.Create(Guid.NewGuid()),
            ProjectDomainType = new UserOrganizationManagement.Domain.Entities.ProjectDomainType()
        };
        var projectB = new UserOrganizationManagement.Domain.Entities.Project
        {
            ShortName = new ProjectShortName("B"),
            Id = ProjectId.Create(Guid.NewGuid()),
            ProjectDomainType = new UserOrganizationManagement.Domain.Entities.ProjectDomainType()
        };

        mockUnitOfWork
            .Setup(u => u.ProjectRepository.GetAllBySpecificationsAsync(null, null, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserOrganizationManagement.Domain.Entities.Project> { projectA, projectB });

        var projectService = CreateProjectServiceWithMockUnitOfWork(mockUnitOfWork);

        var result = await projectService.GetProjectsAsync(null, null, null, null, null, null, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    // Pins the pre-existing 404-on-empty-filtered-result behaviour: it stays keyed on shortName/
    // contactEmail/domainId only — the provider scope is deliberately NOT special-cased there, so a
    // provider-scoped query whose filter matches nothing still 404s instead of returning 200 [].
    [Fact]
    public async Task Should_throw_RecordNotFoundException_When_providerScoped_query_with_filter_matches_nothing()
    {
        var mockUnitOfWork = new Mock<IUserOrgUnitOfWork>();
        var providerId = Guid.NewGuid();

        mockUnitOfWork
            .Setup(u => u.ProjectRepository.GetAllBySpecificationsAsync(_projectShortName, null, null, null, null, providerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserOrganizationManagement.Domain.Entities.Project>());

        var projectService = CreateProjectServiceWithMockUnitOfWork(mockUnitOfWork);

        await Assert.ThrowsAsync<RecordNotFoundException>(
            () => projectService.GetProjectsAsync(_projectShortName, null, null, null, null, providerId, CancellationToken.None));
    }

    private static ProjectService CreateProjectServiceWithMockUnitOfWork(Mock<IUserOrgUnitOfWork> mockUnitOfWork) => new(
        mockUnitOfWork.Object, new Mock<IHttpClientFactory>().Object, new Mock<IFeatureManager>().Object,
        new ServiceCollection().AddHybridCache().Services.BuildServiceProvider().GetRequiredService<HybridCache>(),
        new Mock<IHeappeConnectionService>().Object, new Mock<ILogger<ProjectService>>().Object, new Mock<IRoleListService>().Object);

    [Fact]
    public async Task ProjectTemplateInfoReturnsExpectedResult()
    {
        var (projectService, _, _, _) = CreateService();

        var result = await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);

        Assert.NotNull(result.Responses);
    }

    [Fact]
    public async Task Should_serve_TemplatesInfo_from_cache_When_called_twice()
    {
        var (projectService, mockUnitOfWork, _, _) = CreateService();

        await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);
        await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);

        mockUnitOfWork.Verify(
            u => u.ProjectRepository.GetHeappeLinks(_projectShortName, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_recompute_TemplatesInfo_When_tag_evicted()
    {
        var (projectService, mockUnitOfWork, cache, _) = CreateService();

        await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);
        await cache.RemoveByTagAsync(CacheTags.TemplatesInfo(_projectShortName.Value), CancellationToken.None);
        await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);

        mockUnitOfWork.Verify(
            u => u.ProjectRepository.GetHeappeLinks(_projectShortName, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // Witnesses the kill-switch evictor-to-producer wiring ACROSS the module boundary (ADR-0077 §3, §4):
    // the v1 TemplatesInfo entry this Core service caches must carry the one global tag that
    // KillSwitchAdminHandler.EvictPermissionCacheAsync removes, or enforcing a kill-switch would keep
    // advertising frozen templates as active for the entry's whole 5-minute TTL. Lift self-heals via the
    // resync's own eviction; enforce has no such path, so this tag is the only thing closing that window.
    [Fact]
    public async Task Should_recompute_TemplatesInfo_When_global_kill_switch_tag_evicted()
    {
        var (projectService, mockUnitOfWork, cache, _) = CreateService();

        await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);

        // The REAL eviction KillSwitchAdminHandler performs on enforce: one RemoveByTagAsync of the single
        // global tag — no per-project fan-out (ADR-0077 §4 rejects the N-tag partial-eviction hole).
        await cache.RemoveByTagAsync(CacheTags.KillSwitchGlobal, CancellationToken.None);

        await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);

        mockUnitOfWork.Verify(
            u => u.ProjectRepository.GetHeappeLinks(_projectShortName, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Should_bypass_TemplatesInfo_cache_When_noCache_true()
    {
        var (projectService, mockUnitOfWork, _, heappe) = CreateService();

        // noCache disables read AND write on the outer TemplatesInfo cache, so every call rebuilds from
        // scratch — the second call must not be served from a cached entry written by the first.
        await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: true, CancellationToken.None);
        await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: true, CancellationToken.None);

        mockUnitOfWork.Verify(
            u => u.ProjectRepository.GetHeappeLinks(_projectShortName, It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // ...and noCache is propagated down to the HEAppE adapter so the inner cluster cache is bypassed too.
        heappe.Verify(s => s.GetListAvailableClusters(
            It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(),
            It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), true),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Should_not_cache_TemplatesInfo_When_Heappe_call_fails()
    {
        var (projectService, mockUnitOfWork, _, heappe) = CreateService();

        // Simulate a transient HEAppE failure: the connection service yields a null result, which
        // ProcessUrlGroupAsync treats as degraded. This is exactly what tends to happen during the
        // UserInfo-triggered warm-up burst at login.
        heappe
            .Setup(s => s.GetListAvailableClusters(
                It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(),
                It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Returns(ValueTask.FromResult<Result<IList<ClusterInfoModel>, Error>>(null!));

        // A fully-degraded build surfaces as 503 and, crucially, is NOT cached — so the second call
        // rebuilds from scratch instead of serving an empty array from a poisoned cache entry.
        await Assert.ThrowsAsync<HeappeUnavailableException>(
            () => projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None));
        await Assert.ThrowsAsync<HeappeUnavailableException>(
            () => projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None));

        mockUnitOfWork.Verify(
            u => u.ProjectRepository.GetHeappeLinks(_projectShortName, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Should_not_cache_TemplatesInfo_When_build_is_empty()
    {
        var (projectService, mockUnitOfWork, _, _) = CreateService();

        // A project with no HEAppE links builds an empty — but NOT degraded — result. It must never be
        // cached: the login-triggered warm-up can snapshot a project mid-propagation, and a cached []
        // would keep serving emptiness for the full 5-minute TTL even after the data has appeared.
        mockUnitOfWork
            .Setup(u => u.ProjectRepository.GetHeappeLinks(_projectShortName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IProjectHEAppELinks>());

        var first = await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);
        var second = await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);

        Assert.Empty(first.Responses);
        Assert.Empty(second.Responses);
        Assert.Empty(first.UnavailableInstances);

        // Both calls rebuilt from scratch — the empty first result was not served back from the cache.
        mockUnitOfWork.Verify(
            u => u.ProjectRepository.GetHeappeLinks(_projectShortName, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Should_evict_Heappe_cluster_cache_When_resource_assigned_to_project()
    {
        var (projectService, mockUnitOfWork, cache, _) = CreateService();

        // A resource with no locations passes validation (providerId is null); the fixture project
        // starts with no assigned resources.
        mockUnitOfWork.Setup(u => u.ProjectResourceRepository.GetByNameWithIncludesAsync("FTA-24-49", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserOrganizationManagement.Domain.Entities.ProjectResource { Name = "FTA-24-49" });

        // Seed a layer-2 cluster entry for the instance the fixture project links to, tagged the same
        // way HeappeConnectionService tags it (per-instance tag over the normalized URL).
        var identifier = new HeappeIdentifier("heappe-it4i");
        var factoryRuns = 0;
        async ValueTask<string> ReadClusterEntry() => await cache.GetOrCreateAsync(
            CacheTags.HeappeCluster(identifier, "Karolina"),
            _ =>
            {
                factoryRuns++;
                return ValueTask.FromResult("cluster-snapshot");
            },
            tags: [CacheTags.HeappeClusters(identifier)]);

        await ReadClusterEntry();
        await ReadClusterEntry();
        Assert.Equal(1, factoryRuns);

        // Assigning a resource is the moment the HEAppE-side allocation typically appears — the shared
        // cluster catalog for the project's instances must be dropped so the next TemplatesInfo build
        // refetches instead of composing from a pre-propagation snapshot for the rest of the 5 min TTL.
        await projectService.AssignedProjectResourceToProjectAsync(_projectShortName, "FTA-24-49", null, CancellationToken.None);

        await ReadClusterEntry();
        Assert.Equal(2, factoryRuns);
    }

    [Fact]
    public async Task Should_evict_Heappe_cluster_cache_When_soft_deleted_assignment_reactivated()
    {
        var (projectService, mockUnitOfWork, cache, _) = CreateService();

        // The reactivation branch takes an early return before the new-assignment tail — it must carry
        // the same layer-2 eviction, or a revived resource composes from a pre-propagation catalog.
        var resource = new UserOrganizationManagement.Domain.Entities.ProjectResource { Name = "FTA-24-49" };
        var project = new UserOrganizationManagement.Domain.Entities.Project
        {
            ShortName = _projectShortName,
            Id = ProjectId.Create(Guid.NewGuid())
        };
        project.AssignedProjectResources.Add(new UserOrganizationManagement.Domain.Entities.AssignedProjectResourceToProject
        {
            Project = project,
            ProjectResource = resource,
            IsDeleted = true
        });

        mockUnitOfWork.Setup(u => u.ProjectRepository.GetByShortNameAsync(_projectShortName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);
        mockUnitOfWork.Setup(u => u.ProjectResourceRepository.GetByNameWithIncludesAsync("FTA-24-49", It.IsAny<CancellationToken>()))
            .ReturnsAsync(resource);

        var identifier = new HeappeIdentifier("heappe-it4i");
        var factoryRuns = 0;
        async ValueTask<string> ReadClusterEntry() => await cache.GetOrCreateAsync(
            CacheTags.HeappeCluster(identifier, "Karolina"),
            _ =>
            {
                factoryRuns++;
                return ValueTask.FromResult("cluster-snapshot");
            },
            tags: [CacheTags.HeappeClusters(identifier)]);

        await ReadClusterEntry();
        Assert.Equal(1, factoryRuns);

        await projectService.AssignedProjectResourceToProjectAsync(_projectShortName, "FTA-24-49", null, CancellationToken.None);

        Assert.False(project.AssignedProjectResources.Single().IsDeleted);
        await ReadClusterEntry();
        Assert.Equal(2, factoryRuns);
    }

    // ---- Login warm-up racing a user call over a slow HEAppE fetch -------------------------------------
    // Production timeline the next three tests simulate: at login the UserInfo endpoint fires the
    // background ClusterInfoCacheWarmer, which starts a TemplatesInfo build whose HEAppE fetch takes
    // ~45 s; the user opens the portal and calls TemplatesInfo within ~10 s — while that build is still
    // in flight. A TaskCompletionSource gate stands in for the 45 s latency so the tests drive the
    // timeline deterministically instead of sleeping.

    [Fact]
    public async Task Should_join_inflight_warmup_build_When_user_calls_during_slow_Heappe_fetch()
    {
        var (projectService, mockUnitOfWork, _, heappe) = CreateService();
        mockUnitOfWork.Setup(u => u.ProjectRepository.GetHeappeLinks(_projectShortName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IProjectHEAppELinks> { ActiveLinkOn(new HeappeIdentifier("heappe-a")) });
        var (fetchStarted, releaseFetch, fetchCount) = SetupGatedHeappeFetch(heappe,
            (_, resourceName, names) => Result<IList<ClusterInfoModel>, Error>.Success(
                names.Select(n => BuildCluster(n, id: 1, templateId: null, accountingString: resourceName)).ToList<ClusterInfoModel>()));

        // t=0: login — the background warmer starts the build (CancellationToken.None, exactly like
        // ClusterInfoCacheWarmer.WarmAsync does).
        var warmupCall = projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // t=10 s: the user calls TemplatesInfo while the 45 s fetch is still in flight.
        var userCall = projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);

        // Neither call can finish before HEAppE answers.
        Assert.False(warmupCall.IsCompleted);
        Assert.False(userCall.IsCompleted);

        // t=45 s: HEAppE answers.
        releaseFetch.SetResult();
        var warmupResult = await warmupCall.WaitAsync(TimeSpan.FromSeconds(5));
        var userResult = await userCall.WaitAsync(TimeSpan.FromSeconds(5));

        // Both callers share the single-flight build: same data, ONE HEAppE fetch, ONE DB read — the
        // user's call joined the warmer's build instead of starting a second 45 s fetch behind its back.
        Assert.NotEmpty(warmupResult.Responses);
        Assert.Equal(warmupResult.Responses.Count, userResult.Responses.Count);
        Assert.Equal(1, fetchCount());
        mockUnitOfWork.Verify(
            u => u.ProjectRepository.GetHeappeLinks(_projectShortName, It.IsAny<CancellationToken>()),
            Times.Once);

        // The populated build was cached — the next call is a pure cache hit.
        var later = await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);
        Assert.NotEmpty(later.Responses);
        Assert.Equal(1, fetchCount());
    }

    [Fact]
    public async Task Should_recover_after_login_race_When_slow_build_snapshots_unpropagated_data()
    {
        var (projectService, mockUnitOfWork, _, heappe) = CreateService();
        mockUnitOfWork.Setup(u => u.ProjectRepository.GetHeappeLinks(_projectShortName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IProjectHEAppELinks> { ActiveLinkOn(new HeappeIdentifier("heappe-a")) });

        // First fetch: HEAppE still serves a catalog whose allocation does NOT carry this project's
        // accounting string (the data is mid-propagation). Every later fetch sees the propagated state.
        var (fetchStarted, releaseFetch, fetchCount) = SetupGatedHeappeFetch(heappe,
            (call, resourceName, names) => Result<IList<ClusterInfoModel>, Error>.Success(
                names.Select(n => BuildCluster(n, id: 1, templateId: null,
                    accountingString: call == 1 ? "NOT-YET-PROPAGATED" : resourceName)).ToList<ClusterInfoModel>()));

        // t=0: warm-up starts the slow build; t=10 s: the user's call joins it in flight.
        var warmupCall = projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var userCall = projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);

        // t=45 s: HEAppE answers with the not-yet-propagated snapshot.
        releaseFetch.SetResult();
        var warmupResult = await warmupCall.WaitAsync(TimeSpan.FromSeconds(5));
        var userResult = await userCall.WaitAsync(TimeSpan.FromSeconds(5));

        // Both callers honestly observe the emptiness of that moment (one shared fetch)...
        Assert.Empty(warmupResult.Responses);
        Assert.Empty(userResult.Responses);
        Assert.Equal(1, fetchCount());

        // ...but the empty snapshot was NOT cached: once the allocation propagates, the very next call
        // rebuilds and sees the data — no 5-minute [] window, no manual sync needed.
        var afterPropagation = await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);
        Assert.NotEmpty(afterPropagation.Responses);
        Assert.Equal(2, fetchCount());

        // And the now-populated build is cached again.
        var cached = await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);
        Assert.NotEmpty(cached.Responses);
        Assert.Equal(2, fetchCount());
    }

    [Fact]
    public async Task Should_surface_503_to_both_racing_callers_When_slow_Heappe_fetch_fails()
    {
        var (projectService, mockUnitOfWork, _, heappe) = CreateService();
        mockUnitOfWork.Setup(u => u.ProjectRepository.GetHeappeLinks(_projectShortName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IProjectHEAppELinks> { ActiveLinkOn(new HeappeIdentifier("heappe-a")) });

        // First fetch fails outright (null response = degraded, as in the existing failure test);
        // the instance has recovered by the time of any later fetch.
        var (fetchStarted, releaseFetch, fetchCount) = SetupGatedHeappeFetch(heappe,
            (call, resourceName, names) => call == 1
                ? null!
                : Result<IList<ClusterInfoModel>, Error>.Success(
                    names.Select(n => BuildCluster(n, id: 1, templateId: null, accountingString: resourceName)).ToList<ClusterInfoModel>()));

        var warmupCall = projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var userCall = projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);
        releaseFetch.SetResult();

        // The single-flight degraded signal reaches every joined caller: the warmer logs-and-swallows
        // its copy (SafeFireAndForget), the user's request surfaces as 503 — never a cached empty 200.
        await Assert.ThrowsAsync<HeappeUnavailableException>(() => warmupCall.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAsync<HeappeUnavailableException>(() => userCall.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, fetchCount());

        // Nothing was cached, so the next call rebuilds against the recovered instance.
        var recovered = await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);
        Assert.NotEmpty(recovered.Responses);
        Assert.Equal(2, fetchCount());
    }

    [Fact]
    public async Task Should_throw_ServiceUnavailable_immediately_When_all_instances_fail()
    {
        var (projectService, _, _, heappe) = CreateService();

        heappe
            .Setup(s => s.GetListAvailableClusters(
                It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(),
                It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Returns(ValueTask.FromResult<Result<IList<ClusterInfoModel>, Error>>(null!));

        // Total degradation: every HEAppE query failed. Instead of a misleading empty/false 200, the caller gets
        // a 503 (HeappeUnavailableException) so it can tell "HEAppE is down" from "no templates" (ADR 0073) —
        // and it gets it after a SINGLE build attempt, with no wait-and-rebuild loop.
        await Assert.ThrowsAsync<HeappeUnavailableException>(
            () => projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None));

        heappe.Verify(s => s.GetListAvailableClusters(
            It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(),
            It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public async Task Should_report_failure_cause_per_instance_without_leaking_url_When_all_instances_fail()
    {
        var (projectService, _, _, heappe) = CreateService();

        // The active link fails with an Unauthorized cause. The underlying Error description carries the HEAppE
        // URL (exactly as the real adapter's does) — that URL must NOT reach the surfaced failure (ADR 0074).
        var unauthorized = new ErrorBuilder()
            .WithCode("unauthorized")
            .WithDescription("Failed to retrieve response from 'https://secret-heappe.example/api'. Status code: 'Unauthorized'.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

        heappe
            .Setup(s => s.GetListAvailableClusters(
                It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(),
                It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Returns(ValueTask.FromResult(Result<IList<ClusterInfoModel>, Error>.Failure(unauthorized, State.Error)));

        // Total degradation is still 503, but the cause is now discoverable from the response: each failed
        // instance carries its sanitized descriptor (provider/resource/locations, no URL) and its cause code.
        var ex = await Assert.ThrowsAsync<HeappeUnavailableException>(
            () => projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None));

        Assert.NotEmpty(ex.Failures);
        Assert.All(ex.Failures, f => Assert.Equal("unauthorized", f.Cause));
        Assert.All(ex.Failures, f => Assert.DoesNotContain("secret-heappe", f.Instance));
    }

    [Fact]
    public async Task Should_fail_fast_as_internal_error_and_skip_HEAppE_When_command_templates_unavailable()
    {
        var (projectService, _, _, heappe) = CreateService();

        // The active command templates precondition (our Heappe-module DB) fails. This is an internal fault,
        // not HEAppE degradation: the build must fast-fail (500, not 503) BEFORE any HEAppE instance is queried,
        // so it surfaces as TemplatesInfoPreconditionException, not HeappeUnavailableException (ADR 0074).
        heappe
            .Setup(s => s.GetActiveCommandTemplatesAsync(It.IsAny<ProjectId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<HashSet<CommandTemplatePartitionResponse>, Error>.Failure(
                new ErrorBuilder().WithDescription("command-template DB unavailable").WithSeverity(ErrorSeverity.Error).Build(),
                State.Error));

        await Assert.ThrowsAsync<TemplatesInfoPreconditionException>(
            () => projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None));

        // The precondition gate runs before the fan-out: no HEAppE instance is queried.
        heappe.Verify(s => s.GetListAvailableClusters(
            It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(),
            It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_return_200_with_unavailable_header_and_not_cache_When_adapter_reports_allocation_missing()
    {
        var (projectService, mockUnitOfWork, _, heappe) = CreateService();

        // The adapter found the cluster but HEAppE's catalog carried no active allocation for the resource
        // (#291): it fails the group with the allocation-missing cause.
        heappe.Setup(s => s.GetListAvailableClusters(
                It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(),
                It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Returns(ValueTask.FromResult(Result<IList<ClusterInfoModel>, Error>.Failure(
                new ErrorBuilder().WithCode(HeappeFailureCause.AllocationMissing).WithDescription("no allocation").Build())));

        // The only active link is on that instance, yet HEAppE is up and merely disagrees with the link: this
        // is a 200 carrying the unavailable-instance header (never a 503), and it is NOT cached — the second
        // call rebuilds (re-reads the links) instead of serving the snapshot back.
        var first = await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);
        var second = await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);

        Assert.All(first.Responses, r => Assert.False(r.IsAvailable));
        Assert.Equal("TestProvider / DD-24-115 (Barbora)", Assert.Single(first.UnavailableInstances));
        Assert.Single(second.UnavailableInstances);
        mockUnitOfWork.Verify(
            u => u.ProjectRepository.GetHeappeLinks(_projectShortName, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Should_return_empty_without_degrading_When_only_link_is_expired_in_UserOrg()
    {
        var (projectService, mockUnitOfWork, _, heappe) = CreateService();

        // A link whose resource validity ended in UserOrg is genuinely "no allocation": HEAppE is never asked,
        // the result is an empty 200 and no instance is reported as unavailable.
        var expired = ActiveLinkOn(new HeappeIdentifier("heappe-a"));
        expired.ResourceEndDate = DateTime.Now.AddDays(-2);
        mockUnitOfWork.Setup(u => u.ProjectRepository.GetHeappeLinks(_projectShortName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IProjectHEAppELinks> { expired });

        var result = await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);

        Assert.Empty(result.Responses);
        Assert.Empty(result.UnavailableInstances);
        heappe.Verify(s => s.GetListAvailableClusters(
                It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(),
                It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_not_escalate_missing_cluster_to_degraded_When_noCache_true()
    {
        var (projectService, mockUnitOfWork, _, heappe) = CreateService();

        // One active link pointing at cluster "Loc1".
        mockUnitOfWork.Setup(u => u.ProjectRepository.GetHeappeLinks(_projectShortName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IProjectHEAppELinks> { ActiveLinkOn(new HeappeIdentifier("heappe-a")) });

        // The live catalog is populated but missing the link's cluster ("Loc1"). A noCache=true call is a pure
        // pass-through: it must return the live (partial) result as-is — IsAvailable=false placeholder, NOT a
        // degraded retry/503 — and must never touch the shared cache. (With the default zero retry budget, an
        // erroneous degraded escalation here would surface as HeappeUnavailableException.)
        heappe.Setup(s => s.GetListAvailableClusters(
                It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(),
                It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Returns(ValueTask.FromResult(Result<IList<ClusterInfoModel>, Error>.Success(
                new List<ClusterInfoModel> { ClusterNamed("SomeOtherCluster") })));

        var result = await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: true, CancellationToken.None);

        Assert.Single(result.Responses);
        Assert.False(result.Responses[0].IsAvailable);
    }

    [Fact]
    public async Task Should_return_partial_snapshot_immediately_without_retrying_When_only_some_instances_fail()
    {
        // Partial degradation must not retry: the dead instance is queried exactly once and the available
        // snapshot is returned at once (ADR 0073). The old behaviour retried the dead instance for the whole budget.
        var (projectService, mockUnitOfWork, _, heappe) = CreateService();

        var idGood = new HeappeIdentifier("heappe-good");
        var idBad = new HeappeIdentifier("heappe-bad");

        mockUnitOfWork.Setup(u => u.ProjectRepository.GetHeappeLinks(_projectShortName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IProjectHEAppELinks> { ActiveLinkOn(idGood), ActiveLinkOn(idBad) });

        // One instance answers with usable data, the other keeps failing (degraded).
        heappe.Setup(s => s.GetListAvailableClusters(
                It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(),
                It.Is<HeappeIdentifier>(i => i == idGood), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Returns(ClusterResult(clusterId: 1, templateId: 10));
        heappe.Setup(s => s.GetListAvailableClusters(
                It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(),
                It.Is<HeappeIdentifier>(i => i == idBad), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Returns(ValueTask.FromResult<Result<IList<ClusterInfoModel>, Error>>(null!));

        // Partial degradation is NOT a total outage: the available snapshot is returned with 200 (no throw),
        // and the healthy instance's templates are present.
        var result = await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);

        Assert.Contains(result.Responses, r => r.IsAvailable);

        // The failed instance is reported (provider/resource) for the X-Unavailable-HEAppE-Instances header,
        // so a caller can tell why a cluster shows up unavailable.
        Assert.Single(result.UnavailableInstances);
        Assert.Contains("R1", result.UnavailableInstances[0]);

        // Crucially: no wait-and-rebuild. Each instance was queried exactly once despite the configured budget.
        heappe.Verify(s => s.GetListAvailableClusters(
            It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(),
            It.Is<HeappeIdentifier>(i => i == idBad), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_query_both_instances_When_same_resource_location_on_different_HEAppE_urls()
    {
        var (projectService, mockUnitOfWork, _, heappe) = CreateService();

        var idA = new HeappeIdentifier("heappe-a");
        var idB = new HeappeIdentifier("heappe-b");

        // Same (ResourceName, LocationName), two distinct HEAppE instances. These must NOT be collapsed by
        // the dedup — otherwise the second instance's templates silently disappear.
        mockUnitOfWork.Setup(u => u.ProjectRepository.GetHeappeLinks(_projectShortName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IProjectHEAppELinks>
            {
                ActiveLinkOn(idA),
                ActiveLinkOn(idB),
            });

        await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);

        // Both instances must be queried exactly once. Before the fix, dedup collapsed them and only one ran.
        heappe.Verify(s => s.GetListAvailableClusters(
            It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(),
            It.Is<HeappeIdentifier>(i => i == idA), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()),
            Times.Once);
        heappe.Verify(s => s.GetListAvailableClusters(
            It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(),
            It.Is<HeappeIdentifier>(i => i == idB), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_merge_templates_When_same_resource_location_on_different_instances()
    {
        var (projectService, mockUnitOfWork, _, heappe) = CreateService();

        var idA = new HeappeIdentifier("heappe-a");
        var idB = new HeappeIdentifier("heappe-b");

        mockUnitOfWork.Setup(u => u.ProjectRepository.GetHeappeLinks(_projectShortName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IProjectHEAppELinks> { ActiveLinkOn(idA), ActiveLinkOn(idB) });

        // Each instance returns the same cluster/location but a different command template (10 vs 20).
        heappe.Setup(s => s.GetListAvailableClusters(
            It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(),
            It.Is<HeappeIdentifier>(i => i == idA), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Returns(ClusterResult(clusterId: 1, templateId: 10));
        heappe.Setup(s => s.GetListAvailableClusters(
            It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(),
            It.Is<HeappeIdentifier>(i => i == idB), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Returns(ClusterResult(clusterId: 2, templateId: 20));

        var result = await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);

        // Format unchanged: still a flat List<TemplateInfoResponse>. The two instances collapse into ONE
        // cluster entry (same LocationId/ClusterName/IsAvailable)...
        Assert.Single(result.Responses);
        var cluster = result.Responses[0];
        Assert.Equal("Loc1", cluster.ClusterName);
        Assert.Single(cluster.Nodes);

        // ...and BOTH instances' templates survive the merge — nothing is dropped.
        var templateIds = cluster.Nodes
            .SelectMany(n => n.Allocations)
            .SelectMany(a => a.CommandTemplates)
            .Select(c => c.Id)
            .ToList();
        Assert.Contains(10, templateIds);
        Assert.Contains(20, templateIds);
    }

    private static ValueTask<Result<IList<ClusterInfoModel>, Error>> ClusterResult(long clusterId, long templateId)
        => ValueTask.FromResult(Result<IList<ClusterInfoModel>, Error>.Success(
            new List<ClusterInfoModel> { BuildCluster("Loc1", clusterId, templateId) }));

    // A usable cluster (has node types with active projects, so it survives HasUsableClusterData semantics)
    // but under an arbitrary name — used to model a populated catalog that does not contain the link's cluster.
    private static ClusterInfoModel ClusterNamed(string name) => BuildCluster(name, id: 99, templateId: null);

    // Shared builder for a single usable cluster. Pass templateId to attach one command template; pass null
    // for a cluster whose project has no command templates.
    [Fact]
    public async Task Should_return_templates_When_HEAppE_allocation_starts_later_today()
    {
        var (projectService, mockUnitOfWork, _, heappe) = CreateService();

        mockUnitOfWork.Setup(u => u.ProjectRepository.GetHeappeLinks(_projectShortName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IProjectHEAppELinks> { ActiveLinkOn(new HeappeIdentifier("heappe-a")) });

        // HEAppE reports the allocation's real creation time; UserOrg truncates its own copy to midnight. An
        // allocation created earlier TODAY (here: the last minute of today, so the case holds whenever the test
        // runs) is active — it must not be filtered out just because its time-of-day is after DateTime.Today.
        // Comparing raw DateTime hid such a project until the next midnight rolled over.
        var startsLaterToday = DateTime.Today.AddHours(23).AddMinutes(59);

        heappe.Setup(s => s.GetListAvailableClusters(
                It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(),
                It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Returns((ProjectShortName _, ProjectId _, string _, HeappeIdentifier _, string _, IReadOnlyCollection<string> names, IReadOnlyCollection<CommandTemplatePartitionResponse> _, CancellationToken _, bool _) =>
                ValueTask.FromResult(Result<IList<ClusterInfoModel>, Error>.Success(
                    names.Select(n => BuildCluster(n, id: 1, templateId: 42, projectStart: startsLaterToday, projectEnd: DateTime.Today.AddYears(1)))
                        .ToList<ClusterInfoModel>())));

        var result = await projectService.ProjectTemplateInfo(_projectShortName, BearerToken, noCache: false, CancellationToken.None);

        var templates = result.Responses
            .SelectMany(r => r.Nodes)
            .SelectMany(n => n.Allocations)
            .SelectMany(a => a.CommandTemplates)
            .ToList();

        Assert.Single(templates);
        Assert.Equal(42, templates[0].Id);
    }

    /// <summary>
    /// Replaces the HEAppE cluster fetch with a gated one: the fetch blocks on <c>ReleaseFetch</c>
    /// (standing in for slow HEAppE latency), signals <c>FetchStarted</c> when entered, and builds its
    /// result via <paramref name="resultForCall"/> from the 1-based call number, the requested resource
    /// name and the requested location names. Once released, later fetches pass through unblocked.
    /// </summary>
    private static (TaskCompletionSource FetchStarted, TaskCompletionSource ReleaseFetch, Func<int> FetchCount) SetupGatedHeappeFetch(
        Mock<IHeappeConnectionService> heappe,
        Func<int, string, IReadOnlyCollection<string>, Result<IList<ClusterInfoModel>, Error>> resultForCall)
    {
        var fetchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetchCount = 0;

        heappe
            .Setup(s => s.GetListAvailableClusters(
                It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(),
                It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Returns((ProjectShortName _, ProjectId _, string resourceName, HeappeIdentifier _, string _, IReadOnlyCollection<string> names, IReadOnlyCollection<CommandTemplatePartitionResponse> _, CancellationToken _, bool _) =>
                new ValueTask<Result<IList<ClusterInfoModel>, Error>>(FetchAsync(resourceName, names)));

        return (fetchStarted, releaseFetch, () => Volatile.Read(ref fetchCount));

        async Task<Result<IList<ClusterInfoModel>, Error>> FetchAsync(string resourceName, IReadOnlyCollection<string> names)
        {
            var call = Interlocked.Increment(ref fetchCount);
            fetchStarted.TrySetResult();
            await releaseFetch.Task;
            return resultForCall(call, resourceName, names);
        }
    }

    private static ClusterInfoModel BuildCluster(string name, long id, long? templateId, DateTime? projectStart = null, DateTime? projectEnd = null, string accountingString = "R1") => new()
    {
        Id = id,
        Name = name,
        LocationId = name,
        NodeTypes = new List<NodeTypeModel>
        {
            new()
            {
                Id = 1,
                Name = "cpu",
                NumberOfNodes = 4,
                CoresPerNode = 128,
                MaxWalltime = 48,
                FileTransferMethodId = 2,
                Projects = new List<HEAppEProjectModel>
                {
                    new()
                    {
                        Id = 1,
                        Name = accountingString,
                        AccountingString = accountingString,
                        StartDate = projectStart ?? DateTime.Now.AddMonths(-1),
                        EndDate = projectEnd ?? DateTime.Now.AddMonths(1),
                        CommandTemplates = templateId is { } t
                            ? new List<CommandtemplateModel>
                            {
                                new() { Id = t, Name = $"tmpl{t}", IsGeneric = false, TemplateParameters = new List<TemplateParameterModel>() }
                            }
                            : new List<CommandtemplateModel>()
                    }
                }
            }
        }
    };

    private static ProjectHEAppELinks ActiveLinkOn(HeappeIdentifier identifier) => new()
    {
        ResourceName = "R1",
        HEAppEApiIdentifier = identifier,
        HEAppEApiVersion = new Version("5.0.0"),
        ProviderName = "TestProvider",
        LocationId = "Loc1",
        LocationName = "Loc1",
        ProjectStartDate = DateTime.Now.AddMonths(-1),
        ProjectEndDate = DateTime.Now.AddMonths(1),
        ResourceStartDate = DateTime.Now.AddDays(-1),
        ResourceEndDate = DateTime.Now.AddDays(1)
    };

    private static (ProjectService Service, Mock<IUserOrgUnitOfWork> UnitOfWork, HybridCache Cache, Mock<IHeappeConnectionService> Heappe) CreateService()
    {
        var mockUnitOfWork = new Mock<IUserOrgUnitOfWork>();
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        var mockfutureManager = new Mock<IFeatureManager>();
        var mockHeappeService = new Mock<IHeappeConnectionService>();
        var logger = new Mock<ILogger<ProjectService>>();
        var mockIRoleList = new Mock<IRoleListService>();
        var cache = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider().GetRequiredService<HybridCache>();

        var projectDetails = new List<IProjectHEAppELinks>
        {
            new ProjectHEAppELinks
            {
                ResourceName = "FTA-24-49",
                HEAppEApiIdentifier = new HeappeIdentifier("heappe-it4i"),
                HEAppEApiVersion = new Version("5.0.0"),
                ProviderName = "TestProvider",
                LocationId = "Karolina",
                LocationName = "Karolina",
                ProjectStartDate = DateTime.Now.AddMonths(-1),
                ProjectEndDate = DateTime.Now,
                ResourceStartDate = DateTime.Now.AddDays(-100),
                ResourceEndDate = DateTime.Now.AddDays(-50)
            },
            new ProjectHEAppELinks
            {
                ResourceName = "DD-24-115",
                HEAppEApiIdentifier = new HeappeIdentifier("heappe-it4i"),
                HEAppEApiVersion = new Version("5.0.0"),
                ProviderName = "TestProvider",
                LocationId = "Barbora",
                LocationName = "Barbora",
                ProjectStartDate = DateTime.Now.AddMonths(-1),
                ProjectEndDate = DateTime.Now,
                ResourceStartDate = DateTime.Now.AddDays(-1),
                ResourceEndDate = DateTime.Now.AddDays(1)
            }
        };

        mockUnitOfWork.Setup(u => u.ProjectRepository.GetHeappeLinks(_projectShortName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projectDetails);

        mockUnitOfWork.Setup(u => u.ProjectRepository.GetByShortNameAsync(_projectShortName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserOrganizationManagement.Domain.Entities.Project
            {
                ShortName = _projectShortName,
                Id = ProjectId.Create(Guid.NewGuid())
            });

        mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        // Healthy default: the project-level command-template precondition read succeeds (empty set is fine).
        // Tests that exercise a precondition failure override this.
        mockHeappeService
            .Setup(s => s.GetActiveCommandTemplatesAsync(It.IsAny<ProjectId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<HashSet<CommandTemplatePartitionResponse>, Error>.Success([], State.Ok));

        // Healthy default: HEAppE returns a cluster for every requested location whose allocation matches the
        // requested resource, so the build is NOT degraded, NOT empty, and gets cached. Tests that need a
        // missing cluster / failure / empty build override this.
        mockHeappeService
            .Setup(s => s.GetListAvailableClusters(
                It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(),
                It.IsAny<HeappeIdentifier>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Returns((ProjectShortName _, ProjectId _, string resourceName, HeappeIdentifier _, string _, IReadOnlyCollection<string> names, IReadOnlyCollection<CommandTemplatePartitionResponse> _, CancellationToken _, bool _) =>
                ValueTask.FromResult(Result<IList<ClusterInfoModel>, Error>.Success(
                    names.Select(n => BuildCluster(n, id: 1, templateId: null, accountingString: resourceName)).ToList<ClusterInfoModel>())));

        // Zero budget by default: degraded builds return their snapshot immediately, so tests that
        // don't exercise the retry loop keep deterministic call counts.
        var projectService = new ProjectService(mockUnitOfWork.Object, mockHttpClientFactory.Object, mockfutureManager.Object, cache, mockHeappeService.Object, logger.Object, mockIRoleList.Object);

        return (projectService, mockUnitOfWork, cache, mockHeappeService);
    }
}
