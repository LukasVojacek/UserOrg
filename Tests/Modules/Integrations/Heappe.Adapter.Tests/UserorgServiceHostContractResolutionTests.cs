using Heappe.HostContracts;

using Moq;

using Userorg.Adapter.Heappe.Userorg;

using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Contracts;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Provider;
using UserOrganizationManagement.Domain.ValueObjects;

namespace Heappe.Adapter.Tests;

// B10 — host-contract resolution the KillSwitch endpoints (B09) rely on: providerName -> ProviderId,
// ProviderId -> its live LocationIds, and (projectShortName, resourceName) -> their internal ids.
// All resolvers are Result-returning (no throw) so the endpoint can fail closed (409/400) on error.
public sealed class UserorgServiceHostContractResolutionTests
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
    public async Task ResolveProviderIdByNameAsync_Should_return_ProviderId_When_name_matches_exactly_case_insensitive()
    {
        var expectedId = Guid.NewGuid();
        _providerService
            .Setup(s => s.GetProvidersAsync(null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProviderModel> { new() { Id = expectedId, Name = "IT4Innovations" } });

        var result = await CreateSut().ResolveProviderIdByNameAsync("it4innovations", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedId, result.Value);
    }

    [Fact]
    public async Task ResolveProviderIdByNameAsync_Should_fail_When_name_is_ambiguous()
    {
        _providerService
            .Setup(s => s.GetProvidersAsync(null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProviderModel>
            {
                new() { Id = Guid.NewGuid(), Name = "Shared" },
                new() { Id = Guid.NewGuid(), Name = "shared" },
            });

        var result = await CreateSut().ResolveProviderIdByNameAsync("Shared", CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ResolveProviderIdByNameAsync_Should_fail_When_name_is_missing()
    {
        _providerService
            .Setup(s => s.GetProvidersAsync(null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProviderModel> { new() { Id = Guid.NewGuid(), Name = "Other" } });

        var result = await CreateSut().ResolveProviderIdByNameAsync("Unknown", CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task GetLocationIdsForProviderAsync_Should_return_only_that_providers_live_location_ids()
    {
        var providerId = Guid.NewGuid();
        var locationIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        _locationService
            .Setup(s => s.GetLocationIdsForProviderIdAsync(providerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationIds);

        var result = await CreateSut().GetLocationIdsForProviderAsync(providerId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(locationIds, result.Value);
    }

    // The singular ResolveProjectResourceIdsAsync(projectShortName, resourceName) overload these tests covered
    // was deleted with its last caller (#258): the kill-switch whitelist now resolves its items through the
    // batch overload, whose equivalent cases live in UserorgServiceKillSwitchResolutionTests.
}
