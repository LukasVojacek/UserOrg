using Heappe.Contracts.Connection;
using Heappe.Features.InitializationProjectWithClusters;
using Heappe.Features.KillSwitch;
using Heappe.Features.VersionResolver;
using Heappe.Persistence;

using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;

using Moq;

using UserOrg.Shared;

using Xunit;

namespace Heappe.UnitTests;

public sealed class HeappeServiceVersionTests
{
    private static readonly HeappeIdentifier Identifier = new("heappe-mock");

    private static HeappeIntegrationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new HeappeIntegrationDbContext(options);
    }

    private readonly Mock<ILogger<HeappeService>> _mockLogger;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<IFeatureManager> _mockFeatureManager;
    private readonly Mock<IDbContextFactory<HeappeIntegrationDbContext>> _mockDbContextFactory;
    private readonly Mock<IKillSwitchStateService> _mockKillSwitchStateService;
    private readonly Mock<IHeappeVersionResolver> _mockVersionResolver;
    private readonly Mock<IOptions<ClusterAuthTypeOptions>> _mockAuthTypeOptions;

    public HeappeServiceVersionTests()
    {
        _mockLogger = new Mock<ILogger<HeappeService>>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockFeatureManager = new Mock<IFeatureManager>();
        _mockDbContextFactory = new Mock<IDbContextFactory<HeappeIntegrationDbContext>>();
        _mockDbContextFactory
            .Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateInMemoryDbContext);
        _mockKillSwitchStateService = new Mock<IKillSwitchStateService>();
        _mockVersionResolver = new Mock<IHeappeVersionResolver>();
        _mockAuthTypeOptions = new Mock<IOptions<ClusterAuthTypeOptions>>();
        _mockAuthTypeOptions.Setup(x => x.Value).Returns(new ClusterAuthTypeOptions());
    }

    private HeappeService CreateService() => new(
        _mockLogger.Object,
        _mockHttpClientFactory.Object,
        _mockFeatureManager.Object,
        _mockDbContextFactory.Object,
        _mockKillSwitchStateService.Object,
        _mockVersionResolver.Object,
        _mockAuthTypeOptions.Object);

    [Fact(DisplayName = "GetClusterAuthenticationCredentialsAuthTypesAsync should return VersionNotSupported when version is below 6.4.0")]
    public async Task Should_ReturnVersionNotSupported_When_VersionBelow640()
    {
        // Arrange
        var heappeUri = new Uri("http://localhost:8080");
        var connInfo = new PublicHeappeConnectionInfo(heappeUri, Identifier);

        _mockVersionResolver
            .Setup(x => x.GetVersion(Identifier, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Version(6, 3, 0));

        var service = CreateService();

        // Act
        var result = await service.GetClusterAuthenticationCredentialsAuthTypesAsync(connInfo);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(State.Invalid, result.State);
        Assert.NotNull(result.Error);
        Assert.Contains("does not support required API version", result.Error.Description);
    }

    [Fact(DisplayName = "GetClusterAuthenticationCredentialsAuthTypesAsync should proceed when version is exactly 6.4.0")]
    public async Task Should_Proceed_When_VersionIs640()
    {
        // Arrange
        var heappeUri = new Uri("http://localhost:8080");
        var connInfo = new PublicHeappeConnectionInfo(heappeUri, Identifier);

        _mockVersionResolver
            .Setup(x => x.GetVersion(Identifier, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Version(6, 4, 0));

        using var mockHttpClient = new HttpClient(new MockHttpMessageHandler(System.Net.HttpStatusCode.OK, "[]"))
        {
            BaseAddress = heappeUri
        };
        _mockHttpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(mockHttpClient);

        // Act
        var result = await CreateService().GetClusterAuthenticationCredentialsAuthTypesAsync(connInfo);

        // Assert
        _mockVersionResolver.Verify(x => x.GetVersion(Identifier, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "GetClusterAuthenticationCredentialsAuthTypesAsync should proceed when version is above 6.4.0")]
    public async Task Should_Proceed_When_VersionAbove640()
    {
        // Arrange
        var heappeUri = new Uri("http://localhost:8080");
        var connInfo = new PublicHeappeConnectionInfo(heappeUri, Identifier);

        _mockVersionResolver
            .Setup(x => x.GetVersion(Identifier, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Version(6, 5, 0));

        using var mockHttpClient = new HttpClient(new MockHttpMessageHandler(System.Net.HttpStatusCode.OK, "[]"))
        {
            BaseAddress = heappeUri
        };
        _mockHttpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(mockHttpClient);

        // Act
        var result = await CreateService().GetClusterAuthenticationCredentialsAuthTypesAsync(connInfo);

        // Assert
        _mockVersionResolver.Verify(x => x.GetVersion(Identifier, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Minimal version constant should be 6.4.0")]
    public void MinimalVersionForPreferredAuthTypeProperty_Should_Be_640()
    {
        Assert.Equal(new Version(6, 4, 0), ProjectCreationService.MinimalVersionForPreferredAuthTypeProperty);
    }
}

internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly System.Net.HttpStatusCode _statusCode;
    private readonly string _content;

    public MockHttpMessageHandler(System.Net.HttpStatusCode statusCode, string content)
    {
        _statusCode = statusCode;
        _content = content;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_content, System.Text.Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
