using Forge.Core.Features.Role.Services;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

using Moq;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.Services;
using UserOrganizationManagement.Domain.ValueObjects;

using IHeappeConnectionService = UserOrganizationManagement.Application.UserOrg.IServices.IHeappeConnectionService;

namespace UserOrganizationManagement.Application.Tests;

public sealed class ProjectServiceTemplateInfoLoggerTests
{
    private static readonly ProjectShortName _projectShortName = new("OrphanedProject");

    private readonly Mock<IUserOrgUnitOfWork> _unitOfWork = new();
    private readonly Mock<ILogger<ProjectService>> _logger = new();

    [Fact]
    public async Task Should_LogTheOrphanedProjectWarningThroughTheInjectedLogger_When_TheProjectIsNotInTheDatabase()
    {
        _unitOfWork
            .Setup(u => u.ProjectRepository.GetByShortNameAsync(_projectShortName, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserOrganizationManagement.Domain.Entities.Project?)null);

        await CreateService().ProjectTemplateInfo(_projectShortName, "token", noCache: true, CancellationToken.None);

        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => IsOrphanedProjectRecord(state.ToString()!)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_LogTheBuildFailureThroughTheInjectedLogger_When_BuildingTemplateInfoThrows()
    {
        var failure = new InvalidOperationException("repository unavailable");
        _unitOfWork
            .Setup(u => u.ProjectRepository.GetByShortNameAsync(_projectShortName, It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().ProjectTemplateInfo(_projectShortName, "token", noCache: true, CancellationToken.None));

        Assert.Same(failure, thrown);
        _logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => IsBuildFailureRecord(state.ToString()!)),
                failure,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static bool IsOrphanedProjectRecord(string record) =>
        record.Contains("possible orphaned Keycloak role", StringComparison.Ordinal)
        && record.Contains(_projectShortName.Value, StringComparison.Ordinal);

    private static bool IsBuildFailureRecord(string record) =>
        record.Contains("Error while building ProjectTemplateInfo", StringComparison.Ordinal)
        && record.Contains(_projectShortName.Value, StringComparison.Ordinal);

    private ProjectService CreateService() => new(
        _unitOfWork.Object,
        new Mock<IHttpClientFactory>().Object,
        new Mock<IFeatureManager>().Object,
        new ServiceCollection().AddHybridCache().Services.BuildServiceProvider().GetRequiredService<HybridCache>(),
        new Mock<IHeappeConnectionService>().Object,
        _logger.Object,
        new Mock<IRoleListService>().Object);
}
