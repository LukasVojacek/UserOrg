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

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Application.UserOrg.Services;
using UserOrganizationManagement.Domain.Contracts;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;
using UserOrganizationManagement.Domain.ValueObjects;

using IHeappeConnectionService = UserOrganizationManagement.Application.UserOrg.IServices.IHeappeConnectionService;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// A HEAppE link without a <c>HEAPPE_IDENTIFIER</c> cannot be routed through NexusApi (#349). TemplatesInfo skips it
/// and says so once per link, naming the location and resource, while the routable links still answer.
/// </summary>
public sealed class ProjectServiceSkippedLinksTests
{
    private static readonly ProjectShortName ShortName = new("SkippedLinks");

    [Fact]
    public async Task Should_WarnOncePerSkippedLink_And_StillQueryTheRoutableOne_When_LinksLackTheIdentifier()
    {
        var logger = new RecordingLogger();
        var unitOfWork = new Mock<IUserOrgUnitOfWork>();
        var heappe = new Mock<IHeappeConnectionService>();
        unitOfWork.Setup(u => u.ProjectRepository.GetHeappeLinks(ShortName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IProjectHEAppELinks>
            {
                LinkOn("Urlonly-1", identifier: null),
                LinkOn("Routable", new HeappeIdentifier("heappe-a")),
                LinkOn("Urlonly-2", identifier: null),
            });
        unitOfWork.Setup(u => u.ProjectRepository.GetByShortNameAsync(ShortName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserOrganizationManagement.Domain.Entities.Project { ShortName = ShortName, Id = ProjectId.Create(Guid.NewGuid()) });
        heappe.Setup(s => s.GetActiveCommandTemplatesAsync(It.IsAny<ProjectId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<HashSet<CommandTemplatePartitionResponse>, Error>.Success([], State.Ok));
        heappe.Setup(s => s.GetListAvailableClusters(
                It.IsAny<ProjectShortName>(), It.IsAny<ProjectId>(), It.IsAny<string>(), It.IsAny<HeappeIdentifier>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(Result<IList<ClusterInfoModel>, Error>.Success([], State.Ok));
        using var provider = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();
        var service = new ProjectService(
            unitOfWork.Object, new Mock<IHttpClientFactory>().Object, new Mock<IFeatureManager>().Object, cache, heappe.Object, logger, new Mock<IRoleListService>().Object);

        await service.ProjectTemplateInfo(ShortName, "<ACCES_TOKEN>", noCache: true, CancellationToken.None);

        var skipped = logger.Warnings.Where(w => w.Contains("has no HEAPPE_IDENTIFIER", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, skipped.Count);
        Assert.Contains(skipped, w => w.Contains("Urlonly-1", StringComparison.Ordinal));
        Assert.Contains(skipped, w => w.Contains("Urlonly-2", StringComparison.Ordinal));
        heappe.Verify(
            s => s.GetListAvailableClusters(
                ShortName, It.IsAny<ProjectId>(), It.IsAny<string>(), new HeappeIdentifier("heappe-a"), It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<CommandTemplatePartitionResponse>>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()),
            Times.Once);
    }

    private static ProjectHEAppELinks LinkOn(string locationName, HeappeIdentifier? identifier) => new()
    {
        ResourceName = "R1",
        HEAppEApiIdentifier = identifier,
        HEAppEApiVersion = new Version("5.0.0"),
        ProviderName = "TestProvider",
        LocationId = locationName,
        LocationName = locationName,
        ProjectStartDate = DateTime.Now.AddMonths(-1),
        ProjectEndDate = DateTime.Now.AddMonths(1),
        ResourceStartDate = DateTime.Now.AddDays(-1),
        ResourceEndDate = DateTime.Now.AddDays(1),
    };

    private sealed class RecordingLogger : ILogger<ProjectService>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
