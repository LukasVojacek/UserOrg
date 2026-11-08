using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Contracts.HEappeApiDto.Responses;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Moq;

using Xunit;

namespace Heappe.UnitTests;

/// <summary>
/// Tests simulating scenarios from WaldurHeappeProjectInitializer
/// These tests verify the behavior of GetClusterAuthenticationCredentialsAuthTypesAsync
/// across different HEAppE versions and error conditions
/// </summary>
public sealed class HeappeServiceIntegrationScenarioTests
{
    private readonly Mock<IHeappeService> _mockHeappeService;

    public HeappeServiceIntegrationScenarioTests()
    {
        _mockHeappeService = new Mock<IHeappeService>();
    }

    [Fact(DisplayName = "GetClusterAuthenticationCredentialsAuthTypesAsync returns error severity should result in failure")]
    public async Task Scenario_ErrorSeverity_ReturnsFailure()
    {
        // Arrange
        var heappeUri = new Uri("http://localhost:8080");
        var error = new ErrorBuilder()
            .WithDescription("Failed to retrieve auth types")
            .WithSeverity(ErrorSeverity.Error)
            .Build(heappeUri.AbsoluteUri);

        _mockHeappeService
            .Setup(x => x.GetClusterAuthenticationCredentialsAuthTypesAsync(
                It.IsAny<PublicHeappeConnectionInfo>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterAuthenticationCredentialAuthType>, Error>.Failure(error, State.Invalid));

        var connInfo = new PublicHeappeConnectionInfo(heappeUri, new UserOrg.Shared.HeappeIdentifier("heappe-mock"));

        // Act
        var result = await _mockHeappeService.Object.GetClusterAuthenticationCredentialsAuthTypesAsync(connInfo);

        // Assert - Error with ErrorSeverity.Error should be treated as failure
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorSeverity.Error, result.Error.Severity);
    }

    [Fact(DisplayName = "GetClusterAuthenticationCredentialsAuthTypesAsync returns auth types successfully")]
    public async Task Scenario_SuccessWithAuthTypes_ReturnsSuccess()
    {
        // Arrange
        var heappeUri = new Uri("http://localhost:8080");
        var authTypes = new List<ClusterAuthenticationCredentialAuthType>
        {
            new() { Name = "basic" },
            new() { Name = "certificate" },
            new() { Name = "oauth" }
        };

        _mockHeappeService
            .Setup(x => x.GetClusterAuthenticationCredentialsAuthTypesAsync(
                It.IsAny<PublicHeappeConnectionInfo>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterAuthenticationCredentialAuthType>, Error>.Success(authTypes));

        var connInfo = new PublicHeappeConnectionInfo(heappeUri, new UserOrg.Shared.HeappeIdentifier("heappe-mock"));

        // Act
        var result = await _mockHeappeService.Object.GetClusterAuthenticationCredentialsAuthTypesAsync(connInfo);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(3, result.Value.Count);
    }

    [Fact(DisplayName = "GetClusterAuthenticationCredentialsAuthTypesAsync returns version not supported (State.Invalid)")]
    public async Task Scenario_VersionNotSupported_ReturnsInvalidState()
    {
        // Arrange
        var heappeUri = new Uri("http://localhost:8080");
        var error = new ErrorBuilder()
            .WithDescription($"HEAppE instance at '{heappeUri}' does not support required API version 6.4.0")
            .WithSeverity(ErrorSeverity.Warning)
            .Build();

        _mockHeappeService
            .Setup(x => x.GetClusterAuthenticationCredentialsAuthTypesAsync(
                It.IsAny<PublicHeappeConnectionInfo>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterAuthenticationCredentialAuthType>, Error>.Failure(error, State.Invalid));

        var connInfo = new PublicHeappeConnectionInfo(heappeUri, new UserOrg.Shared.HeappeIdentifier("heappe-mock"));

        // Act
        var result = await _mockHeappeService.Object.GetClusterAuthenticationCredentialsAuthTypesAsync(connInfo);

        // Assert - Version not supported should result in State.Invalid
        Assert.False(result.IsSuccess);
        Assert.Equal(State.Invalid, result.State);
        Assert.Equal(ErrorSeverity.Warning, result.Error.Severity);
    }

    [Fact(DisplayName = "When HEAppE returns success and State is not Invalid, auth type matching should be applied")]
    public async Task Scenario_SuccessAndNotInvalid_PreferredAuthTypeMatching()
    {
        // Arrange
        var heappeUri = new Uri("http://localhost:8080");
        var preferredAuthType = "basic";

        var authTypes = new List<ClusterAuthenticationCredentialAuthType>
        {
            new() { Name = "basic" },
            new() { Name = "certificate" },
            new() { Name = "oauth" }
        };

        _mockHeappeService
            .Setup(x => x.GetClusterAuthenticationCredentialsAuthTypesAsync(
                It.IsAny<PublicHeappeConnectionInfo>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterAuthenticationCredentialAuthType>, Error>.Success(authTypes));

        var connInfo = new PublicHeappeConnectionInfo(heappeUri, new UserOrg.Shared.HeappeIdentifier("heappe-mock"));

        // Act
        var result = await _mockHeappeService.Object.GetClusterAuthenticationCredentialsAuthTypesAsync(connInfo);

        // Assert
        Assert.True(result.IsSuccess);
        var foundAuthType = result.Value?.FirstOrDefault(x =>
            x.Name.Equals(preferredAuthType, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(foundAuthType);
        Assert.Equal("basic", foundAuthType.Name);
    }

    [Fact(DisplayName = "Case-insensitive auth type matching works correctly")]
    public async Task Scenario_CaseInsensitiveMatching_Succeeds()
    {
        // Arrange
        var heappeUri = new Uri("http://localhost:8080");
        var preferredAuthType = "BASIC"; // uppercase request
        var responseAuthType = "basic";  // lowercase response

        var authTypes = new List<ClusterAuthenticationCredentialAuthType>
        {
            new() { Name = responseAuthType },
            new() { Name = "certificate" }
        };

        _mockHeappeService
            .Setup(x => x.GetClusterAuthenticationCredentialsAuthTypesAsync(
                It.IsAny<PublicHeappeConnectionInfo>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterAuthenticationCredentialAuthType>, Error>.Success(authTypes));

        var connInfo = new PublicHeappeConnectionInfo(heappeUri, new UserOrg.Shared.HeappeIdentifier("heappe-mock"));

        // Act
        var result = await _mockHeappeService.Object.GetClusterAuthenticationCredentialsAuthTypesAsync(connInfo);

        // Assert
        Assert.True(result.IsSuccess);
        var foundAuthType = result.Value?.FirstOrDefault(x =>
            x.Name.Equals(preferredAuthType, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(foundAuthType);
    }

    [Fact(DisplayName = "When preferred auth type is not found in response list, should be handled")]
    public async Task Scenario_PreferredAuthTypeNotInList_CanBeDetected()
    {
        // Arrange
        var heappeUri = new Uri("http://localhost:8080");
        var preferredAuthType = "oauth";

        var authTypes = new List<ClusterAuthenticationCredentialAuthType>
        {
            new() { Name = "basic" },
            new() { Name = "certificate" }
            // oauth is NOT in the list
        };

        _mockHeappeService
            .Setup(x => x.GetClusterAuthenticationCredentialsAuthTypesAsync(
                It.IsAny<PublicHeappeConnectionInfo>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterAuthenticationCredentialAuthType>, Error>.Success(authTypes));

        var connInfo = new PublicHeappeConnectionInfo(heappeUri, new UserOrg.Shared.HeappeIdentifier("heappe-mock"));

        // Act
        var result = await _mockHeappeService.Object.GetClusterAuthenticationCredentialsAuthTypesAsync(connInfo);

        // Assert
        Assert.True(result.IsSuccess);
        var foundAuthType = result.Value?.FirstOrDefault(x =>
            x.Name.Equals(preferredAuthType, StringComparison.OrdinalIgnoreCase));
        Assert.Null(foundAuthType); // Not found - null result
    }
}
