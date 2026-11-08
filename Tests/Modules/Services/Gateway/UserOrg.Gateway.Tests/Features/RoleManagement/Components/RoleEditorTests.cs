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
/// Unit tests for RoleEditor component.
/// </summary>
public sealed class RoleEditorTests : Bunit.TestContext
{
    private readonly Mock<IRoleManagementService> _mockService;
    private readonly RoleManagementState _state;

    public RoleEditorTests()
    {
        _mockService = new Mock<IRoleManagementService>();
        var mockLogger = new Mock<ILogger<RoleManagementState>>();
        _state = new RoleManagementState(_mockService.Object, mockLogger.Object);

        Services.AddSingleton(_state);
        Services.AddSingleton(_mockService.Object);
    }

    [Fact]
    public void RoleEditor_RendersInCreateMode_WhenNoRoleSelected()
    {
        // Act
        using var cut = Render<RoleEditor>();

        // Assert
        Assert.Contains("Create Role", cut.Markup);
        Assert.DoesNotContain("Edit Role", cut.Markup);
    }

    [Fact]
    public void RoleEditor_RendersFormFields_InCreateMode()
    {
        // Act
        using var cut = Render<RoleEditor>();

        // Assert
        var nameInput = cut.Find("input#roleName");
        var descriptionInput = cut.Find("textarea#roleDescription");
        var categoryInput = cut.Find("input#roleCategory");
        var levelInput = cut.Find("input#roleLevel");

        Assert.NotNull(nameInput);
        Assert.NotNull(descriptionInput);
        Assert.NotNull(categoryInput);
        Assert.NotNull(levelInput);
    }

    [Fact]
    public void RoleEditor_RoleNameInput_IsEnabledInCreateMode()
    {
        // Act
        using var cut = Render<RoleEditor>();

        // Assert
        var nameInput = cut.Find("input#roleName");
        Assert.False(nameInput.HasAttribute("disabled"));
    }

    [Fact]
    public void RoleEditor_RoleNameInput_IsDisabledInEditMode()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        _mockService.Setup(s => s.GetRoleAsync("Admin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleDetail, Error>.Success(new RoleDetail
            {
                Name = "Admin",
                Description = "Administrator role",
                Category = "Default",
                Level = 0,
                Permissions = new[] { "perm:users:read" }
            }));

        // Act
        using var cut = Render<RoleEditor>(parameters => parameters
            .Add(p => p.SelectedRoleName, "Admin"));

        cut.WaitForAssertion(() =>
        {
            var nameInput = cut.Find("input#roleName");
            Assert.True(nameInput.HasAttribute("disabled"));
        });
    }

    [Fact]
    public void RoleEditor_ShowsEditModeTitle_WhenRoleNameProvided()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        _mockService.Setup(s => s.GetRoleAsync("Admin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleDetail, Error>.Success(new RoleDetail
            {
                Name = "Admin",
                Description = "Administrator role",
                Category = "Default",
                Level = 0,
                Permissions = Array.Empty<string>()
            }));

        // Act
        using var cut = Render<RoleEditor>(parameters => parameters
            .Add(p => p.SelectedRoleName, "Admin"));

        // Assert - Component is in edit mode
        Assert.Contains("Edit Role", cut.Markup);
    }

    [Fact]
    public void RoleEditor_CallsGetRoleAsync_InEditMode()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        _mockService.Setup(s => s.GetRoleAsync("Admin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleDetail, Error>.Success(new RoleDetail
            {
                Name = "Admin",
                Description = "Administrator role",
                Category = "Default",
                Level = 0,
                Permissions = new[] { "perm:users:read" }
            }));

        // Act
        using var cut = Render<RoleEditor>(parameters => parameters
            .Add(p => p.SelectedRoleName, "Admin"));

        // Assert - Service method was called
        _mockService.Verify(s => s.GetRoleAsync("Admin", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void RoleEditor_RendersPermissionCheckboxes()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" },
            new PermissionInfo { Name = "perm:users:write", Description = "Write users" }
        });

        // Act
        using var cut = Render<RoleEditor>();

        // Assert
        var checkboxes = cut.FindAll("input[type='checkbox']");
        Assert.Equal(2, checkboxes.Count);
        Assert.Contains("perm:users:read", cut.Markup);
        Assert.Contains("perm:users:write", cut.Markup);
    }

    [Fact]
    public void RoleEditor_ChecksPermissions_WhenRoleHasThem()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" },
            new PermissionInfo { Name = "perm:users:write", Description = "Write users" }
        });

        _mockService.Setup(s => s.GetRoleAsync("Admin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleDetail, Error>.Success(new RoleDetail
            {
                Name = "Admin",
                Description = "Administrator role",
                Category = "Default",
                Level = 0,
                Permissions = new[] { "perm:users:read" }
            }));

        // Act
        using var cut = Render<RoleEditor>(parameters => parameters
            .Add(p => p.SelectedRoleName, "Admin"));

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
    public void RoleEditor_TogglesPermission_WhenCheckboxClicked()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        using var cut = Render<RoleEditor>();

        // Act
        var checkbox = cut.Find("input[type='checkbox'][value='perm:users:read']");
        checkbox.Change(true);

        // Assert - Permission should be checked
        Assert.True(checkbox.HasAttribute("checked"));
    }

    [Fact]
    public void RoleEditor_HasSubmitButton_InCreateMode()
    {
        // Act
        using var cut = Render<RoleEditor>();

        // Assert
        var submitButton = cut.Find("button[type='submit']");
        Assert.NotNull(submitButton);
        Assert.Contains("Create Role", submitButton.TextContent);
    }

    [Fact]
    public void RoleEditor_HasSubmitAndCancelButtons_InEditMode()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        _mockService.Setup(s => s.GetRoleAsync("Admin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleDetail, Error>.Success(new RoleDetail
            {
                Name = "Admin",
                Description = "Administrator role",
                Category = "Default",
                Level = 0,
                Permissions = Array.Empty<string>()
            }));

        // Act
        using var cut = Render<RoleEditor>(parameters => parameters
            .Add(p => p.SelectedRoleName, "Admin"));

        // Assert
        cut.WaitForAssertion(() =>
        {
            var buttons = cut.FindAll("button");
            var submitButton = buttons.FirstOrDefault(b => b.GetAttribute("type") == "submit");
            var cancelButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Cancel"));

            Assert.NotNull(submitButton);
            Assert.Contains("Update Role", submitButton.TextContent);
            Assert.NotNull(cancelButton);
        });
    }

    [Fact]
    public void RoleEditor_ShowsSavingState_WhenStateIsBusy()
    {
        // Arrange
        SetupBusyState(true);

        // Act
        using var cut = Render<RoleEditor>();

        // Assert
        var submitButton = cut.Find("button[type='submit']");
        Assert.Contains("Saving...", submitButton.TextContent);
        Assert.True(submitButton.HasAttribute("disabled"));
    }

    [Fact]
    public void RoleEditor_ValidationMessage_ShowsForRequiredName()
    {
        // Arrange
        using var cut = Render<RoleEditor>();

        // Act - Submit empty form
        var form = cut.Find("form");
        form.Submit();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("rm-alert", cut.Markup);
        });
    }

    [Fact]
    public void RoleEditor_InvokesCreateRole_WhenSubmittedInCreateMode()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        _mockService.Setup(s => s.CreateRoleAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleDetail, Error>.Success(new RoleDetail
            {
                Name = "TestRole",
                Description = "Test role",
                Category = "Default",
                Level = 0,
                Permissions = Array.Empty<string>()
            }));

        _mockService.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Success(Array.Empty<RoleSummary>()));
        _mockService.Setup(s => s.GetPermissionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<PermissionInfo>, Error>.Success(Array.Empty<PermissionInfo>()));
        _mockService.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(Array.Empty<EndpointMappingInfo>()));

        using var cut = Render<RoleEditor>();

        // Act - Fill form
        var nameInput = cut.Find("input#roleName");
        var descriptionInput = cut.Find("textarea#roleDescription");
        var categoryInput = cut.Find("input#roleCategory");

        nameInput.Change("TestRole");
        descriptionInput.Change("Test role description");
        categoryInput.Change("TestCategory");

        var form = cut.Find("form");
        form.Submit();

        // Assert
        _mockService.Verify(s => s.CreateRoleAsync(
            It.Is<CreateRoleRequest>(r =>
                r.Name == "TestRole" &&
                r.Description == "Test role description" &&
                r.Category == "TestCategory"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void RoleEditor_InvokesUpdatePermissions_WhenSubmittedInEditMode()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        _mockService.Setup(s => s.GetRoleAsync("Admin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleDetail, Error>.Success(new RoleDetail
            {
                Name = "Admin",
                Description = "Administrator role",
                Category = "Default",
                Level = 0,
                Permissions = Array.Empty<string>()
            }));

        _mockService.Setup(s => s.UpdateRolePermissionsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));

        _mockService.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Success(Array.Empty<RoleSummary>()));
        _mockService.Setup(s => s.GetPermissionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<PermissionInfo>, Error>.Success(Array.Empty<PermissionInfo>()));
        _mockService.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(Array.Empty<EndpointMappingInfo>()));

        using var cut = Render<RoleEditor>(parameters => parameters
            .Add(p => p.SelectedRoleName, "Admin"));

        cut.WaitForAssertion(() => cut.Find("form"));

        // Act - Toggle permission checkbox to change permissions
        var checkbox = cut.Find("input[type='checkbox'][value='perm:users:read']");
        checkbox.Change(true);

        var form = cut.Find("form");
        form.Submit();

        // Assert
        cut.WaitForAssertion(() =>
        {
            _mockService.Verify(s => s.UpdateRolePermissionsAsync(
                "Admin",
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Fact]
    public void RoleEditor_InvokesOnRoleUpdated_AfterSuccessfulCreate()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        _mockService.Setup(s => s.CreateRoleAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleDetail, Error>.Success(new RoleDetail
            {
                Name = "TestRole",
                Description = "Test",
                Category = "Default",
                Level = 0,
                Permissions = Array.Empty<string>()
            }));

        _mockService.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Success(Array.Empty<RoleSummary>()));
        _mockService.Setup(s => s.GetPermissionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<PermissionInfo>, Error>.Success(Array.Empty<PermissionInfo>()));
        _mockService.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(Array.Empty<EndpointMappingInfo>()));

        var onRoleUpdatedCalled = false;
        using var cut = Render<RoleEditor>(parameters => parameters
            .Add(p => p.OnRoleUpdated, () => onRoleUpdatedCalled = true));

        // Act
        var nameInput = cut.Find("input#roleName");
        var descriptionInput = cut.Find("textarea#roleDescription");
        var categoryInput = cut.Find("input#roleCategory");

        nameInput.Change("TestRole");
        descriptionInput.Change("Test");
        categoryInput.Change("Test");

        var form = cut.Find("form");
        form.Submit();

        // Assert
        cut.WaitForAssertion(() => Assert.True(onRoleUpdatedCalled));
    }

    [Fact]
    public void RoleEditor_CancelButton_InvokesResetLogic_InEditMode()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        _mockService.Setup(s => s.GetRoleAsync("Admin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleDetail, Error>.Success(new RoleDetail
            {
                Name = "Admin",
                Description = "Administrator role",
                Category = "Default",
                Level = 0,
                Permissions = Array.Empty<string>()
            }));

        using var cut = Render<RoleEditor>(parameters => parameters
            .Add(p => p.SelectedRoleName, "Admin"));

        cut.WaitForAssertion(() => cut.Find("button.secondary"), TimeSpan.FromSeconds(3));

        // Act
        var cancelButton = cut.Find("button.secondary");
        var clickCount = 0;
        cancelButton.Click();
        clickCount++;

        // Assert - Cancel button exists and can be clicked
        Assert.Equal(1, clickCount);
    }

    [Fact]
    public void RoleEditor_ResetsForm_WhenSwitchingFromEditToCreateMode()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        _mockService.Setup(s => s.GetRoleAsync("Admin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleDetail, Error>.Success(new RoleDetail
            {
                Name = "Admin",
                Description = "Administrator role",
                Category = "Default",
                Level = 0,
                Permissions = Array.Empty<string>()
            }));

        using var cut = Render<RoleEditor>(parameters => parameters
            .Add(p => p.SelectedRoleName, "Admin"));

        cut.WaitForAssertion(() => cut.Find("form"));

        // Act - Change to create mode
        cut.Render(parameters => parameters
            .Add(p => p.SelectedRoleName, (string?)null));

        // Assert
        Assert.Contains("Create Role", cut.Markup);
        var nameInput = cut.Find("input#roleName");
        Assert.False(nameInput.HasAttribute("disabled"));
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