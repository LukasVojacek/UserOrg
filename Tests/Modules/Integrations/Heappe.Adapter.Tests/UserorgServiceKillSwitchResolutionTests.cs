using Heappe.Contracts.Entries;
using Heappe.HostContracts;

using Moq;

using Userorg.Adapter.Heappe.Userorg;

using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Contracts;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Location;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectResource;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Provider;
using UserOrganizationManagement.Domain.ValueObjects;

namespace Heappe.Adapter.Tests;

// Host-contract resolution the KillSwitch admin endpoints (B09) rely on beyond B10's three resolvers:
// the module speaks provider/location/project/resource NAMES on its API but stores GUIDs, and Core is the
// only side that can bridge the two. All are Result-returning so the endpoint can fail closed.
public sealed class UserorgServiceKillSwitchResolutionTests
{
    private readonly Mock<IHeappeIntegrationService> _heappeIntegrationService = new();
    private readonly Mock<ILocationService> _locationService = new();
    private readonly Mock<IProviderService> _providerService = new();
    private readonly Mock<IUserService> _userService = new();
    private readonly Mock<IProjectService> _projectService = new();
    private readonly Mock<IProjectResourceService> _projectResourceService = new();

    private IUserorgService CreateSut() =>
        new UserorgService(
            _heappeIntegrationService.Object,
            _locationService.Object,
            _providerService.Object,
            _userService.Object,
            _projectService.Object,
            _projectResourceService.Object);

    [Fact]
    public async Task GetProviderLocationsAsync_Should_return_every_location_with_its_owning_provider()
    {
        var locationId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        _locationService
            .Setup(s => s.GetLocationsAsync(null, null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LocationModel>
            {
                new()
                {
                    Id = locationId,
                    Name = "Karolina",
                    Provider = new ProviderModel { Id = providerId, Name = "IT4Innovations" }
                }
            });

        var result = await CreateSut().GetProviderLocationsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new ProviderLocationEntry(locationId, "Karolina", providerId, "IT4Innovations"), Assert.Single(result.Value));
    }

    [Fact]
    public async Task GetProviderLocationsAsync_Should_skip_locations_without_a_provider()
    {
        _locationService
            .Setup(s => s.GetLocationsAsync(null, null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LocationModel> { new() { Id = Guid.NewGuid(), Name = "Orphan", Provider = null } });

        var result = await CreateSut().GetProviderLocationsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task GetProviderLocationsAsync_Should_fail_When_the_location_service_throws()
    {
        _locationService
            .Setup(s => s.GetLocationsAsync(null, null, false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await CreateSut().GetProviderLocationsAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ResolveUserIdByIdentityProviderIdAsync_Should_return_the_internal_user_id()
    {
        var userId = Guid.NewGuid();
        _userService
            .Setup(s => s.GetUserBySidAsync("idp-sub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, IdentityProviderUserId = "idp-sub" });

        var result = await CreateSut().ResolveUserIdByIdentityProviderIdAsync("idp-sub", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(userId, result.Value);
    }

    [Fact]
    public async Task ResolveUserIdByIdentityProviderIdAsync_Should_fail_When_the_user_is_unknown()
    {
        _userService
            .Setup(s => s.GetUserBySidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await CreateSut().ResolveUserIdByIdentityProviderIdAsync("ghost", CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ResolveUserIdByIdentityProviderIdAsync_Should_fail_When_the_id_is_blank()
    {
        var result = await CreateSut().ResolveUserIdByIdentityProviderIdAsync("  ", CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ResolveProjectResourceNamesAsync_Should_resolve_ids_to_their_names()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        SetupProjectsAndResources(projectId, "PROJ-1", resourceId, "karolina-cpu");

        var result = await CreateSut().ResolveProjectResourceNamesAsync(
            [new ProjectResourceIdEntry(projectId, resourceId)],
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new ProjectResourceNameEntry(projectId, resourceId, "PROJ-1", "karolina-cpu"),
            Assert.Single(result.Value));
    }

    [Fact]
    public async Task ResolveProjectResourceNamesAsync_Should_omit_pairs_that_no_longer_resolve()
    {
        SetupProjectsAndResources(Guid.NewGuid(), "PROJ-1", Guid.NewGuid(), "karolina-cpu");

        var result = await CreateSut().ResolveProjectResourceNamesAsync(
            [new ProjectResourceIdEntry(Guid.NewGuid(), Guid.NewGuid())],
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task ResolveProjectResourceNamesAsync_Should_short_circuit_on_an_empty_request()
    {
        var result = await CreateSut().ResolveProjectResourceNamesAsync([], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        _projectService.Verify(
            s => s.GetProjectsAsync(It.IsAny<ProjectShortName?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveProjectResourceIdsAsync_Should_resolve_every_pair_in_one_round_trip()
    {
        var firstProjectId = Guid.NewGuid();
        var firstResourceId = Guid.NewGuid();
        var secondProjectId = Guid.NewGuid();
        var secondResourceId = Guid.NewGuid();
        SetupProjectsAndResources(
            [(firstProjectId, "PROJ-1"), (secondProjectId, "PROJ-2")],
            [(firstResourceId, "karolina-cpu"), (secondResourceId, "barbora-gpu")]);

        var result = await CreateSut().ResolveProjectResourceIdsAsync(
            [new ProjectResourceNamePair("PROJ-1", "karolina-cpu"), new ProjectResourceNamePair("PROJ-2", "barbora-gpu")],
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [
                new ProjectResourceNameEntry(firstProjectId, firstResourceId, "PROJ-1", "karolina-cpu"),
                new ProjectResourceNameEntry(secondProjectId, secondResourceId, "PROJ-2", "barbora-gpu")
            ],
            result.Value);

        _projectService.Verify(
            s => s.GetProjectsAsync(It.IsAny<ProjectShortName?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _projectResourceService.Verify(
            s => s.GetProjectResourcesAsync(It.IsAny<Guid?>(), It.IsAny<ProjectId?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // The caller (whitelist PUT) refuses the WHOLE request when any item is unknown: it can only do that if an
    // unresolved pair is visibly ABSENT from the result while the resolvable ones still come back to diff against.
    [Fact]
    public async Task ResolveProjectResourceIdsAsync_Should_omit_pairs_that_do_not_resolve()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        SetupProjectsAndResources([(projectId, "PROJ-1")], [(resourceId, "karolina-cpu")]);

        var result = await CreateSut().ResolveProjectResourceIdsAsync(
            [
                new ProjectResourceNamePair("PROJ-1", "karolina-cpu"),
                new ProjectResourceNamePair("GHOST", "karolina-cpu"),
                new ProjectResourceNamePair("PROJ-1", "ghost-resource")
            ],
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new ProjectResourceNameEntry(projectId, resourceId, "PROJ-1", "karolina-cpu"),
            Assert.Single(result.Value));
    }

    [Fact]
    public async Task ResolveProjectResourceIdsAsync_Should_match_names_case_insensitively()
    {
        var projectId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        SetupProjectsAndResources([(projectId, "PROJ-1")], [(resourceId, "karolina-cpu")]);

        var result = await CreateSut().ResolveProjectResourceIdsAsync(
            [new ProjectResourceNamePair("proj-1", "KAROLINA-CPU")],
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new ProjectResourceNameEntry(projectId, resourceId, "PROJ-1", "karolina-cpu"),
            Assert.Single(result.Value));
    }

    // Resource names are not globally unique, so a name can hit more than one row. Guessing which one was meant
    // would silently exempt the wrong template from a kill switch: an ambiguous name resolves to nothing instead,
    // which the caller reports as an unknown item and refuses the request over.
    [Fact]
    public async Task ResolveProjectResourceIdsAsync_Should_omit_pairs_whose_name_is_ambiguous()
    {
        var projectId = Guid.NewGuid();
        SetupProjectsAndResources(
            [(projectId, "PROJ-1")],
            [(Guid.NewGuid(), "karolina-cpu"), (Guid.NewGuid(), "karolina-cpu")]);

        var result = await CreateSut().ResolveProjectResourceIdsAsync(
            [new ProjectResourceNamePair("PROJ-1", "karolina-cpu")],
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    // The singular resolver this batch replaces checked ownership intrinsically, via an AssignedProjectResources
    // join: a resource that exists but belongs to ANOTHER project was refused. Resolving it here would let the
    // whitelist PUT write a kill-switch exemption that crosses projects, so the pair must be omitted instead.
    [Fact]
    public async Task ResolveProjectResourceIdsAsync_Should_omit_pairs_whose_resource_belongs_to_another_project()
    {
        var projectAId = Guid.NewGuid();
        var projectBId = Guid.NewGuid();
        var resourceAId = Guid.NewGuid();
        var resourceBId = Guid.NewGuid();
        SetupProjectsAndResources(
            [(projectAId, "PROJ-A", (IList<Guid>)[resourceAId]), (projectBId, "PROJ-B", (IList<Guid>)[resourceBId])],
            [(resourceAId, "karolina-cpu"), (resourceBId, "barbora-gpu")]);

        var result = await CreateSut().ResolveProjectResourceIdsAsync(
            [
                new ProjectResourceNamePair("PROJ-A", "karolina-cpu"),
                new ProjectResourceNamePair("PROJ-A", "barbora-gpu")
            ],
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new ProjectResourceNameEntry(projectAId, resourceAId, "PROJ-A", "karolina-cpu"),
            Assert.Single(result.Value));
    }

    [Fact]
    public async Task ResolveProjectResourceIdsAsync_Should_short_circuit_on_an_empty_request()
    {
        var result = await CreateSut().ResolveProjectResourceIdsAsync([], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        _projectService.Verify(
            s => s.GetProjectsAsync(It.IsAny<ProjectShortName?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveProjectResourceIdsAsync_Should_fail_the_whole_batch_When_the_project_service_throws()
    {
        _projectService
            .Setup(s => s.GetProjectsAsync(null, null, null, null, null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await CreateSut().ResolveProjectResourceIdsAsync(
            [new ProjectResourceNamePair("PROJ-1", "karolina-cpu")],
            CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    // Every resource is assigned to every project unless a test says otherwise, so ownership only gets in the way
    // of the tests that are actually about it.
    private void SetupProjectsAndResources(
        IReadOnlyCollection<(Guid Id, string ShortName)> projects,
        IReadOnlyCollection<(Guid Id, string Name)> resources)
        => SetupProjectsAndResources(
            projects.Select(p => (p.Id, p.ShortName, (IList<Guid>)resources.Select(r => r.Id).ToList())).ToList(),
            resources);

    private void SetupProjectsAndResources(
        IReadOnlyCollection<(Guid Id, string ShortName, IList<Guid> AssignedProjectResourceIds)> projects,
        IReadOnlyCollection<(Guid Id, string Name)> resources)
    {
        _projectService
            .Setup(s => s.GetProjectsAsync(null, null, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projects
                .Select(p => new ProjectModel
                {
                    Id = ProjectId.Create(p.Id),
                    ShortName = p.ShortName,
                    AssignedProjectResourceIds = p.AssignedProjectResourceIds
                })
                .ToList());

        _projectResourceService
            .Setup(s => s.GetProjectResourcesAsync(null, (ProjectId?)null, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resources
                .Select(r => new ProjectResourceModel { Id = r.Id, Name = r.Name })
                .ToList());
    }

    private void SetupProjectsAndResources(Guid projectId, string shortName, Guid resourceId, string resourceName)
        => SetupProjectsAndResources([(projectId, shortName)], [(resourceId, resourceName)]);
}
