using Bunit;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UserOrg.Gateway.Features.RoleManagement;
using UserOrg.Gateway.Features.RoleManagement.Components;
using UserOrg.Gateway.Features.RoleManagement.Services;

namespace UserOrg.Gateway.Tests.Features.RoleManagement.Components;

/// <summary>
/// Unit tests for EndpointMapper component.
/// </summary>
public sealed class EndpointMapperTests : Bunit.TestContext
{
    private readonly Mock<IRoleManagementService> _mockService;
    private readonly RoleManagementState _state;

    public EndpointMapperTests()
    {
        _mockService = new Mock<IRoleManagementService>();
        var mockLogger = new Mock<ILogger<RoleManagementState>>();
        _state = new RoleManagementState(_mockService.Object, mockLogger.Object);

        // Default mock behavior: return whatever the state currently contains so LoadReferenceDataAsync doesn't overwrite test-controlled state.
        _mockService.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(_state.EndpointMappings));
        _mockService.Setup(s => s.GetPermissionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result<IReadOnlyCollection<PermissionInfo>, Error>.Success(_state.Permissions));
        _mockService.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result<IReadOnlyCollection<RoleSummary>, Error>.Success(_state.Roles));

        Services.AddSingleton(_state);
    }

    [Fact]
    public void EndpointMapper_RendersTitle()
    {
        // Act
        using var cut = Render<EndpointMapper>();

        // Assert
        Assert.Contains("Select an endpoint", cut.Markup);
    }

    [Fact]
    public void EndpointMapper_RendersEndpointDropdown()
    {
        // Act
        using var cut = Render<EndpointMapper>();

        // Assert - component now shows an info paragraph when no endpoint selected
        var info = cut.Find("p.rm-info");
        Assert.NotNull(info);
        Assert.Contains("Select an endpoint", info.TextContent);
    }

    [Fact]
    public void EndpointMapper_ShowsDefaultOption_InDropdown()
    {
        // Act
        using var cut = Render<EndpointMapper>();

        // Assert - default info paragraph is present when no endpoint is selected
        Assert.Contains("Select an endpoint", cut.Markup);
    }

    [Fact]
    public void EndpointMapper_PopulatesDropdown_WithEndpoints()
    {
        // Arrange
        SetupEndpoints(new[]
        {
            new EndpointMappingInfo { EndpointName = "/api/users", Permissions = Array.Empty<string>() },
            new EndpointMappingInfo { EndpointName = "/api/roles", Permissions = Array.Empty<string>() }
        });

        // Act - render and select an endpoint by parameter
        using var cut = Render<EndpointMapper>(parameters => parameters.Add(p => p.SelectedEndpointName, "/api/users"));

        // Assert - selected endpoint heading is rendered
        Assert.Contains("Edit Endpoint: /api/users", cut.Markup);
    }

    [Fact]
    public void EndpointMapper_DoesNotShowPermissions_WhenNoEndpointSelected()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        // Act
        using var cut = Render<EndpointMapper>();

        // Assert
        var checkboxes = cut.FindAll("input[type='checkbox']");
        Assert.Empty(checkboxes);
    }

    [Fact]
    public void EndpointMapper_ShowsPermissions_WhenEndpointSelected()
    {
        // Arrange
        SetupEndpoints(new[]
        {
            new EndpointMappingInfo { EndpointName = "/api/users", Permissions = Array.Empty<string>() }
        });

        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" },
            new PermissionInfo { Name = "perm:users:write", Description = "Write users" }
        });

        // Act - render with selected endpoint
        using var cut = Render<EndpointMapper>(parameters => parameters.Add(p => p.SelectedEndpointName, "/api/users"));

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Edit Endpoint: /api/users", cut.Markup);
            var checkboxes = cut.FindAll("input[type='checkbox']");
            Assert.Equal(2, checkboxes.Count);
        });
    }

    [Fact]
    public void EndpointMapper_LoadsExistingPermissions_WhenEndpointSelected()
    {
        // Arrange
        SetupEndpoints(new[]
        {
            new EndpointMappingInfo
            {
                EndpointName = "/api/users",
                Permissions = new[] { "perm:users:read" }
            }
        });

        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" },
            new PermissionInfo { Name = "perm:users:write", Description = "Write users" }
        });

        // Act - render with selected endpoint
        using var cut = Render<EndpointMapper>(parameters => parameters.Add(p => p.SelectedEndpointName, "/api/users"));

        // Assert
        cut.WaitForAssertion(() =>
        {
            var checkboxes = cut.FindAll("input[type='checkbox']");
            var readCheckbox = checkboxes.First(c => c.GetAttribute("value") == "perm:users:read");
            var writeCheckbox = checkboxes.First(c => c.GetAttribute("value") == "perm:users:write");

            Assert.True(readCheckbox.HasAttribute("checked"));
            Assert.False(writeCheckbox.HasAttribute("checked"));
        });
    }

    [Fact]
    public void EndpointMapper_ClearsPermissions_WhenSwitchingToEmptySelection()
    {
        // Arrange
        SetupEndpoints(new[]
        {
            new EndpointMappingInfo { EndpointName = "/api/users", Permissions = Array.Empty<string>() }
        });

        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        using var cut = Render<EndpointMapper>(parameters => parameters.Add(p => p.SelectedEndpointName, "/api/users"));
        cut.WaitForAssertion(() => cut.FindAll("input[type='checkbox']").Any());

        // Act - deselect by updating parameters
        cut.Render(parameters => parameters.Add(p => p.SelectedEndpointName, ""));

        // Assert
        var checkboxes = cut.FindAll("input[type='checkbox']");
        Assert.Empty(checkboxes);
    }

    [Fact]
    public void EndpointMapper_TogglePermission_UpdatesSelection()
    {
        // Arrange
        SetupEndpoints(new[]
        {
            new EndpointMappingInfo { EndpointName = "/api/users", Permissions = Array.Empty<string>() }
        });

        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        using var cut = Render<EndpointMapper>(parameters => parameters.Add(p => p.SelectedEndpointName, "/api/users"));
        cut.WaitForAssertion(() => cut.Find("input[type='checkbox']"));

        // Act
        var checkbox = cut.Find("input[type='checkbox'][value='perm:users:read']");
        checkbox.Change(true);

        // Assert
        Assert.True(checkbox.HasAttribute("checked"));
    }

    [Fact]
    public void EndpointMapper_ShowsSaveButton_WhenEndpointSelected()
    {
        // Arrange
        SetupEndpoints(new[]
        {
            new EndpointMappingInfo { EndpointName = "/api/users", Permissions = Array.Empty<string>() }
        });

        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        using var cut = Render<EndpointMapper>(parameters => parameters.Add(p => p.SelectedEndpointName, "/api/users"));

        // Assert
        cut.WaitForAssertion(() =>
        {
            var saveButton = cut.Find("button.rm-button");
            Assert.NotNull(saveButton);
            Assert.Contains("Save Permissions", saveButton.TextContent);
        });
    }

    [Fact]
    public void EndpointMapper_DoesNotShowSaveButton_WhenNoEndpointSelected()
    {
        // Act
        using var cut = Render<EndpointMapper>();

        // Assert
        var buttons = cut.FindAll("button.rm-button");
        Assert.Empty(buttons);
    }

    [Fact]
    public void EndpointMapper_SaveButton_ShowsBusyState_WhenStateisBusy()
    {
        // Arrange
        SetupEndpoints(new[]
        {
            new EndpointMappingInfo { EndpointName = "/api/users", Permissions = Array.Empty<string>() }
        });

        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        SetupBusyState(true);

        using var cut = Render<EndpointMapper>(parameters => parameters.Add(p => p.SelectedEndpointName, "/api/users"));

        // Assert
        cut.WaitForAssertion(() =>
        {
            var saveButton = cut.Find("button.rm-button");
            Assert.Contains("Saving...", saveButton.TextContent);
            Assert.True(saveButton.HasAttribute("disabled"));
        });
    }

    [Fact]
    public void EndpointMapper_SaveButton_InvokesSetEndpointPermissions()
    {
        // Arrange
        SetupEndpoints(new[]
        {
            new EndpointMappingInfo { EndpointName = "/api/users", Permissions = Array.Empty<string>() }
        });

        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" },
            new PermissionInfo { Name = "perm:users:write", Description = "Write users" }
        });

        _mockService.Setup(s => s.SetEndpointPermissionsAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));

        _mockService.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Success(Array.Empty<RoleSummary>()));
        _mockService.Setup(s => s.GetPermissionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<PermissionInfo>, Error>.Success(Array.Empty<PermissionInfo>()));
        _mockService.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(Array.Empty<EndpointMappingInfo>()));

        using var cut = Render<EndpointMapper>(parameters => parameters.Add(p => p.SelectedEndpointName, "/api/users"));

        cut.WaitForAssertion(() => cut.Find("button.rm-button"));

        // Act - Select permission and save
        var checkbox = cut.Find("input[type='checkbox'][value='perm:users:read']");
        checkbox.Change(true);

        var saveButton = cut.Find("button.rm-button");
        saveButton.Click();

        // Assert
        cut.WaitForAssertion(() =>
        {
            _mockService.Verify(s => s.SetEndpointPermissionsAsync(
                "/api/users",
                It.Is<IReadOnlyCollection<string>>(p => p.Contains("perm:users:read")),
                It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Fact]
    public void EndpointMapper_DoesNotSave_WhenNoEndpointSelected()
    {
        // Arrange
        using var cut = Render<EndpointMapper>();

        // Act & Assert - No save button should exist
        var buttons = cut.FindAll("button.rm-button");
        Assert.Empty(buttons);
    }

    [Fact]
    public void EndpointMapper_SwitchingEndpoints_LoadsCorrectPermissions()
    {
        // Arrange
        SetupEndpoints(new[]
        {
            new EndpointMappingInfo
            {
                EndpointName = "/api/users",
                Permissions = new[] { "perm:users:read" }
            },
            new EndpointMappingInfo
            {
                EndpointName = "/api/roles",
                Permissions = new[] { "perm:roles:write" }
            }
        });

        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" },
            new PermissionInfo { Name = "perm:roles:write", Description = "Write roles" }
        });

        using var cut = Render<EndpointMapper>(parameters => parameters.Add(p => p.SelectedEndpointName, "/api/users"));
        // Act - first endpoint selected
        cut.WaitForAssertion(() =>
        {
            var checkboxes = cut.FindAll("input[type='checkbox']");
            var userReadCheckbox = checkboxes.First(c => c.GetAttribute("value") == "perm:users:read");
            Assert.True(userReadCheckbox.HasAttribute("checked"));
        });

        // Act - Switch to second endpoint
        cut.Render(parameters => parameters.Add(p => p.SelectedEndpointName, "/api/roles"));

        // Assert - Second endpoint's permissions are loaded
        cut.WaitForAssertion(() =>
        {
            var checkboxes = cut.FindAll("input[type='checkbox']");
            var roleWriteCheckbox = checkboxes.First(c => c.GetAttribute("value") == "perm:roles:write");
            Assert.True(roleWriteCheckbox.HasAttribute("checked"));
        });
    }

    [Fact]
    public void EndpointMapper_HandlesEndpointWithNoPreexistingPermissions()
    {
        // Arrange
        SetupEndpoints(new[]
        {
            new EndpointMappingInfo
            {
                EndpointName = "/api/new-endpoint",
                Permissions = Array.Empty<string>()
            }
        });

        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        using var cut = Render<EndpointMapper>(parameters => parameters.Add(p => p.SelectedEndpointName, "/api/new-endpoint"));

        // Assert
        cut.WaitForAssertion(() =>
        {
            var checkboxes = cut.FindAll("input[type='checkbox']");
            Assert.All(checkboxes, c => Assert.False(c.HasAttribute("checked")));
        });
    }

    private void SetupEndpoints(EndpointMappingInfo[] endpoints)
    {
        // Use reflection to set the EndpointMappings property since it's read-only
        var endpointsProperty = typeof(RoleManagementState).GetProperty("EndpointMappings");
        endpointsProperty?.SetValue(_state, endpoints.ToList().AsReadOnly());
    }

    private void SetupPermissions(PermissionInfo[] permissions)
    {
        // Use reflection to set the Permissions property since it's read-only
        var permissionsProperty = typeof(RoleManagementState).GetProperty("Permissions");
        permissionsProperty?.SetValue(_state, permissions.ToList().AsReadOnly());
    }

    private void SetupBusyState(bool isBusy)
    {
        var isBusyProperty = typeof(RoleManagementState).GetProperty("IsBusy");
        isBusyProperty?.SetValue(_state, isBusy);
    }
}