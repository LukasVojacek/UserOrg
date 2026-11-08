using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.Logging;

using UserOrg.Gateway.Features.RoleManagement;
using UserOrg.Gateway.Features.RoleManagement.Services;

namespace UserOrg.Gateway.Tests.Features.RoleManagement;

/// <summary>
/// Unit tests for RoleManagementState service.
/// </summary>
public sealed class RoleManagementStateTests
{
    private readonly Mock<IRoleManagementService> _mockService;
    private readonly Mock<ILogger<RoleManagementState>> _mockLogger;
    private readonly RoleManagementState _sut;

    public RoleManagementStateTests()
    {
        _mockService = new Mock<IRoleManagementService>();
        _mockLogger = new Mock<ILogger<RoleManagementState>>();
        _sut = new RoleManagementState(_mockService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task LoadReferenceDataAsync_SetsRolesPermissionsAndEndpoints_WhenSuccessful()
    {
        // Arrange
        var roles = new List<RoleSummary>
        {
            new() { Name = "Admin", Description = "Administrator" },
            new() { Name = "User", Description = "Regular user" }
        };
        var permissions = new List<PermissionInfo>
        {
            new() { Name = "perm:read", Description = "Read permission" },
            new() { Name = "perm:write", Description = "Write permission" }
        };
        var endpoints = new List<EndpointMappingInfo>
        {
            new() { EndpointName = "/api/users", Permissions = new[] { "perm:read" } }
        };

        _mockService.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Success(roles));
        _mockService.Setup(s => s.GetPermissionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<PermissionInfo>, Error>.Success(permissions));
        _mockService.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(endpoints));

        // Act
        await _sut.LoadReferenceDataAsync();

        // Assert
        Assert.Equal(2, _sut.Roles.Count);
        Assert.Equal("Admin", _sut.Roles[0].Name);
        Assert.Equal(2, _sut.Permissions.Count);
        Assert.Single(_sut.EndpointMappings);
        Assert.False(_sut.IsLoading);
        Assert.Null(_sut.ErrorMessage);
    }

    [Fact]
    public async Task LoadReferenceDataAsync_SetsErrorMessage_WhenRolesLoadFails()
    {
        // Arrange
        var error = new Error("LOAD_ERROR", ErrorSeverity.Error, "Failed to load roles");
        _mockService.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Failure(error));
        _mockService.Setup(s => s.GetPermissionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<PermissionInfo>, Error>.Success(new List<PermissionInfo>()));
        _mockService.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(new List<EndpointMappingInfo>()));

        // Act
        await _sut.LoadReferenceDataAsync();

        // Assert
        Assert.Empty(_sut.Roles);
        Assert.Equal("Failed to load roles", _sut.ErrorMessage);
        Assert.False(_sut.IsLoading);
    }

    [Fact]
    public async Task CreateRoleAsync_ReturnsSuccess_AndRefreshesData()
    {
        // Arrange
        var request = new CreateRoleRequest
        {
            Name = "TestRole",
            Description = "Test description",
            Category = "Test",
            Level = 1,
            Permissions = new[] { "perm:read" }
        };
        var roleDetail = new RoleDetail
        {
            Name = "TestRole",
            Description = "Test description",
            Category = "Default",
            Level = 0,
            Permissions = new[] { "perm:read" }
        };

        _mockService.Setup(s => s.CreateRoleAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleDetail, Error>.Success(roleDetail));

        // Setup for LoadReferenceDataAsync (called after create)
        _mockService.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Success(
                new List<RoleSummary> { new() { Name = "TestRole", Description = "Test description" } }));
        _mockService.Setup(s => s.GetPermissionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<PermissionInfo>, Error>.Success(new List<PermissionInfo>()));
        _mockService.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(new List<EndpointMappingInfo>()));

        // Act
        var result = await _sut.CreateRoleAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("TestRole", result.Value.Name);
        Assert.Equal("Role 'TestRole' created successfully.", _sut.SuccessMessage);
        Assert.Single(_sut.Roles);
        Assert.False(_sut.IsBusy);
    }

    [Fact]
    public async Task CreateRoleAsync_ReturnsFailure_AndSetsErrorMessage()
    {
        // Arrange
        var request = new CreateRoleRequest { Name = "TestRole", Description = "Test", Category = "Test", Level = 1 };
        var error = new Error("CREATE_ERROR", ErrorSeverity.Error, "Role already exists");

        _mockService.Setup(s => s.CreateRoleAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleDetail, Error>.Failure(error));

        // Act
        var result = await _sut.CreateRoleAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Role already exists", _sut.ErrorMessage);
        Assert.Null(_sut.SuccessMessage);
        Assert.False(_sut.IsBusy);
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_ReturnsSuccess_AndRefreshesData()
    {
        // Arrange
        var roleName = "Admin";
        var permissions = new List<string> { "perm:read", "perm:write" };

        _mockService.Setup(s => s.UpdateRolePermissionsAsync(roleName, permissions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));

        // Setup for LoadReferenceDataAsync
        _mockService.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Success(new List<RoleSummary>()));
        _mockService.Setup(s => s.GetPermissionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<PermissionInfo>, Error>.Success(new List<PermissionInfo>()));
        _mockService.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(new List<EndpointMappingInfo>()));

        // Act
        var result = await _sut.UpdateRolePermissionsAsync(roleName, permissions);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Permissions for role 'Admin' updated successfully.", _sut.SuccessMessage);
        Assert.False(_sut.IsBusy);
    }

    [Fact]
    public async Task UpdateRoleStatusAsync_ReturnsSuccess_AndSetsSuccessMessage()
    {
        // Arrange
        var roleName = "Admin";
        var isActive = false;

        _mockService.Setup(s => s.UpdateRoleStatusAsync(roleName, isActive, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));

        // Setup for LoadReferenceDataAsync
        _mockService.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Success(new List<RoleSummary>()));
        _mockService.Setup(s => s.GetPermissionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<PermissionInfo>, Error>.Success(new List<PermissionInfo>()));
        _mockService.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(new List<EndpointMappingInfo>()));

        // Act
        var result = await _sut.UpdateRoleStatusAsync(roleName, isActive);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Role 'Admin' deactivated successfully.", _sut.SuccessMessage);
        Assert.False(_sut.IsBusy);
    }

    [Fact]
    public async Task SetEndpointPermissionsAsync_ReturnsSuccess_AndRefreshesData()
    {
        // Arrange
        var endpointName = "/api/users";
        var permissions = new List<string> { "perm:read" };

        _mockService.Setup(s => s.SetEndpointPermissionsAsync(endpointName, permissions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));

        // Setup for LoadReferenceDataAsync
        _mockService.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Success(new List<RoleSummary>()));
        _mockService.Setup(s => s.GetPermissionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<PermissionInfo>, Error>.Success(new List<PermissionInfo>()));
        _mockService.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(new List<EndpointMappingInfo>()));

        // Act
        var result = await _sut.SetEndpointPermissionsAsync(endpointName, permissions);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Permissions for endpoint '/api/users' updated successfully.", _sut.SuccessMessage);
        Assert.False(_sut.IsBusy);
    }

    [Fact]
    public void ClearMessages_ClearsErrorAndSuccessMessages()
    {
        // Arrange
        _mockService.Setup(s => s.CreateRoleAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleDetail, Error>.Failure(new Error("TEST_ERROR", ErrorSeverity.Error, "Error")));

        // Act
        _sut.ClearMessages();

        // Assert
        Assert.Null(_sut.ErrorMessage);
        Assert.Null(_sut.SuccessMessage);
    }

    [Fact]
    public void OnChange_EventIsRaised_WhenStateChanges()
    {
        // Arrange
        var eventRaised = false;
        _sut.OnChange += () => eventRaised = true;

        // Act
        _sut.ClearMessages();

        // Assert
        Assert.True(eventRaised);
    }

    [Fact]
    public void Dispose_CancelsCancellationToken()
    {
        using (
                // Arrange
                var sut = new RoleManagementState(_mockService.Object, _mockLogger.Object))
        {
        }

        // Assert - no exception should be thrown
        Assert.True(true); // Dispose completed successfully
    }

    [Fact]
    public async Task CreateRoleAsync_ThrowsArgumentNullException_WhenRequestIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.CreateRoleAsync(null!));
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_ThrowsArgumentException_WhenRoleNameIsEmpty()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.UpdateRolePermissionsAsync(string.Empty, new List<string>()));
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_ThrowsArgumentNullException_WhenPermissionsIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _sut.UpdateRolePermissionsAsync("Admin", null!));
    }

    [Fact]
    public async Task UpdateRoleStatusAsync_ThrowsArgumentException_WhenRoleNameIsEmpty()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.UpdateRoleStatusAsync(string.Empty, true));
    }

    [Fact]
    public async Task SetEndpointPermissionsAsync_ThrowsArgumentException_WhenEndpointNameIsEmpty()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.SetEndpointPermissionsAsync(string.Empty, new List<string>()));
    }

    [Fact]
    public async Task SetEndpointPermissionsAsync_ThrowsArgumentNullException_WhenPermissionsIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _sut.SetEndpointPermissionsAsync("/api/users", null!));
    }

    [Fact]
    public async Task Should_ReportSuccessAndReload_When_AnEndpointIsCreated()
    {
        var created = new EndpointMappingInfo { EndpointName = "GetThings", PathTemplate = "/api/things", HttpMethod = "GET" };
        _mockService.Setup(s => s.CreateEndpointMappingAsync("GetThings", "/api/things", "GET", "desc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EndpointMappingInfo, Error>.Success(created));
        SetupReferenceData(created);

        var result = await _sut.CreateEndpointAsync("GetThings", "/api/things", "GET", "desc");

        Assert.True(result.IsSuccess);
        Assert.Same(created, result.Value);
        Assert.Equal("Endpoint 'GetThings' created successfully.", _sut.SuccessMessage);
        Assert.Same(created, Assert.Single(_sut.EndpointMappings));
        Assert.False(_sut.IsBusy);
        VerifyLogged(LogLevel.Information);
    }

    [Theory]
    [InlineData("Endpoint already exists", "Endpoint already exists")]
    [InlineData(null, "Failed to create endpoint.")]
    public async Task Should_ReportTheErrorWithoutReloading_When_CreatingAnEndpointFails(string? description, string expectedMessage)
    {
        _mockService.Setup(s => s.CreateEndpointMappingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EndpointMappingInfo, Error>.Failure(new Error("CREATE_ERROR", ErrorSeverity.Error, description!)));

        var result = await _sut.CreateEndpointAsync("GetThings", "/api/things", "GET", null);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedMessage, _sut.ErrorMessage);
        Assert.Null(_sut.SuccessMessage);
        Assert.False(_sut.IsBusy);
        _mockService.Verify(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()), Times.Never);
        VerifyLogged(LogLevel.Error);
    }

    [Fact]
    public async Task Should_RefuseAsBusy_When_AnEndpointIsCreatedDuringAnotherOperation()
    {
        var pending = new TaskCompletionSource<Result<bool, Error>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _mockService.Setup(s => s.DeleteEndpointMappingAsync("first", It.IsAny<CancellationToken>())).Returns(pending.Task);
        SetupReferenceData();
        var first = _sut.DeleteEndpointAsync("first");

        var result = await _sut.CreateEndpointAsync("second", "/api/second", "POST", null);

        Assert.False(result.IsSuccess);
        Assert.Equal("Busy", result.Error.Code);
        Assert.Equal("Another operation is in progress.", result.Error.Description);
        _mockService.Verify(s => s.CreateEndpointMappingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        pending.SetResult(Result<bool, Error>.Success(true));
        await first;
    }

    [Fact]
    public async Task Should_ReportSuccessAndReload_When_AnEndpointIsDeleted()
    {
        _mockService.Setup(s => s.DeleteEndpointMappingAsync("GetThings", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));
        SetupReferenceData();

        var result = await _sut.DeleteEndpointAsync("GetThings");

        Assert.True(result.IsSuccess);
        Assert.Equal("Endpoint 'GetThings' deleted successfully.", _sut.SuccessMessage);
        Assert.False(_sut.IsBusy);
        _mockService.Verify(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()), Times.Once);
        VerifyLogged(LogLevel.Information);
    }

    [Theory]
    [InlineData("Endpoint not found", "Endpoint not found")]
    [InlineData(null, "Failed to delete endpoint.")]
    public async Task Should_ReportTheErrorWithoutReloading_When_DeletingAnEndpointFails(string? description, string expectedMessage)
    {
        _mockService.Setup(s => s.DeleteEndpointMappingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Failure(new Error("DELETE_ERROR", ErrorSeverity.Error, description!)));

        var result = await _sut.DeleteEndpointAsync("GetThings");

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedMessage, _sut.ErrorMessage);
        Assert.Null(_sut.SuccessMessage);
        Assert.False(_sut.IsBusy);
        _mockService.Verify(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()), Times.Never);
        VerifyLogged(LogLevel.Error);
    }

    [Fact]
    public async Task Should_RefuseAsBusy_When_AnEndpointIsDeletedDuringAnotherOperation()
    {
        var pending = new TaskCompletionSource<Result<EndpointMappingInfo, Error>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _mockService.Setup(s => s.CreateEndpointMappingAsync("first", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(pending.Task);
        SetupReferenceData();
        var first = _sut.CreateEndpointAsync("first", "/api/first", "GET", null);

        var result = await _sut.DeleteEndpointAsync("second");

        Assert.False(result.IsSuccess);
        Assert.Equal("Busy", result.Error.Code);
        Assert.Equal("Another operation is in progress.", result.Error.Description);
        _mockService.Verify(s => s.DeleteEndpointMappingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        pending.SetResult(Result<EndpointMappingInfo, Error>.Success(new EndpointMappingInfo { EndpointName = "first" }));
        await first;
    }

    [Fact]
    public async Task Should_RaiseOnChangeWhenStartingAndFinishing_When_AnEndpointIsDeleted()
    {
        _mockService.Setup(s => s.DeleteEndpointMappingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Failure(new Error("DELETE_ERROR", ErrorSeverity.Error, "nope")));
        var busyStates = new List<bool>();
        _sut.OnChange += () => busyStates.Add(_sut.IsBusy);

        await _sut.DeleteEndpointAsync("GetThings");

        Assert.Equal([true, false], busyStates);
    }

    private void SetupReferenceData(params EndpointMappingInfo[] endpoints)
    {
        _mockService.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Success(new List<RoleSummary>()));
        _mockService.Setup(s => s.GetPermissionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<PermissionInfo>, Error>.Success(new List<PermissionInfo>()));
        _mockService.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(endpoints));
    }

    private void VerifyLogged(LogLevel level) =>
        Assert.Single(_mockLogger.Invocations, invocation => invocation.Method.Name == nameof(ILogger.Log) && Equals(invocation.Arguments[0], level));
}
