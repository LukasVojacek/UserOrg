using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UserOrg.Gateway.Features.RoleManagement;
using UserOrg.Gateway.Features.RoleManagement.Components;
using UserOrg.Gateway.Features.RoleManagement.Services;

namespace UserOrg.Gateway.Tests.Features.RoleManagement.Components;

/// <summary>
/// Unit tests for PermissionSelector component.
/// </summary>
public sealed class PermissionSelectorTests : Bunit.TestContext
{
    private readonly Mock<IRoleManagementService> _mockService;
    private readonly RoleManagementState _state;

    public PermissionSelectorTests()
    {
        _mockService = new Mock<IRoleManagementService>();
        var mockLogger = new Mock<ILogger<RoleManagementState>>();
        _state = new RoleManagementState(_mockService.Object, mockLogger.Object);

        Services.AddSingleton(_state);
    }

    [Fact]
    public void PermissionSelector_RendersTitle()
    {
        // Act
        using var cut = Render<PermissionSelector>();

        // Assert
        Assert.Contains("Permissions", cut.Markup);
    }

    [Fact]
    public void PermissionSelector_ShowsEmptyMessage_WhenNoPermissions()
    {
        // Act
        using var cut = Render<PermissionSelector>();

        // Assert
        Assert.Contains("No permissions available.", cut.Markup);
    }

    [Fact]
    public void PermissionSelector_RendersPermissionGroups_WhenPermissionsExist()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" },
            new PermissionInfo { Name = "perm:users:write", Description = "Write users" },
            new PermissionInfo { Name = "perm:roles:read", Description = "Read roles" }
        });

        // Act
        using var cut = Render<PermissionSelector>();

        // Assert
        // Should group by resource (second segment after "perm:")
        Assert.Contains("users", cut.Markup);
        Assert.Contains("roles", cut.Markup);
    }

    [Fact]
    public void PermissionSelector_GroupsPermissionsByResource()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" },
            new PermissionInfo { Name = "perm:users:write", Description = "Write users" },
            new PermissionInfo { Name = "perm:roles:read", Description = "Read roles" },
            new PermissionInfo { Name = "perm:roles:write", Description = "Write roles" }
        });

        // Act
        using var cut = Render<PermissionSelector>();

        // Assert
        var detailsElements = cut.FindAll("details");
        Assert.Equal(2, detailsElements.Count); // Two groups: users, roles

        var summaries = cut.FindAll("summary");
        Assert.Contains(summaries, s => s.TextContent.Contains("users"));
        Assert.Contains(summaries, s => s.TextContent.Contains("roles"));
    }

    [Fact]
    public void PermissionSelector_RendersCheckboxForEachPermission()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" },
            new PermissionInfo { Name = "perm:users:write", Description = "Write users" }
        });

        // Act
        using var cut = Render<PermissionSelector>();

        // Assert
        var checkboxes = cut.FindAll("input[type='checkbox']");
        Assert.Equal(2, checkboxes.Count);

        var readCheckbox = checkboxes.FirstOrDefault(c => c.GetAttribute("value") == "perm:users:read");
        var writeCheckbox = checkboxes.FirstOrDefault(c => c.GetAttribute("value") == "perm:users:write");

        Assert.NotNull(readCheckbox);
        Assert.NotNull(writeCheckbox);
    }

    [Fact]
    public void PermissionSelector_DisplaysPermissionNames()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" },
            new PermissionInfo { Name = "perm:users:write", Description = "Write users" }
        });

        // Act
        using var cut = Render<PermissionSelector>();

        // Assert
        Assert.Contains("perm:users:read", cut.Markup);
        Assert.Contains("perm:users:write", cut.Markup);
    }

    [Fact]
    public void PermissionSelector_DisplaysPermissionDescriptions()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" },
            new PermissionInfo { Name = "perm:users:write", Description = "Write users" }
        });

        // Act
        using var cut = Render<PermissionSelector>();

        // Assert
        Assert.Contains("Read users", cut.Markup);
        Assert.Contains("Write users", cut.Markup);

        var helpTexts = cut.FindAll("small.rm-help-text");
        Assert.Equal(2, helpTexts.Count);
    }

    [Fact]
    public void PermissionSelector_ChecksSelectedPermissions()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" },
            new PermissionInfo { Name = "perm:users:write", Description = "Write users" }
        });

        var selectedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "perm:users:read"
        };

        // Act
        using var cut = Render<PermissionSelector>(parameters => parameters
            .Add(p => p.SelectedPermissions, selectedPermissions));

        // Assert
        var checkboxes = cut.FindAll("input[type='checkbox']");
        var readCheckbox = checkboxes.First(c => c.GetAttribute("value") == "perm:users:read");
        var writeCheckbox = checkboxes.First(c => c.GetAttribute("value") == "perm:users:write");

        Assert.True(readCheckbox.HasAttribute("checked"));
        Assert.False(writeCheckbox.HasAttribute("checked"));
    }

    [Fact]
    public void PermissionSelector_InvokesCallback_WhenPermissionSelected()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        var selectedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string>? callbackValue = null;

        using var cut = Render<PermissionSelector>(parameters => parameters
            .Add(p => p.SelectedPermissions, selectedPermissions)
            .Add(p => p.SelectedPermissionsChanged, value => callbackValue = value));

        // Act
        var checkbox = cut.Find("input[type='checkbox'][value='perm:users:read']");
        checkbox.Change(true);

        // Assert
        Assert.NotNull(callbackValue);
        Assert.Contains("perm:users:read", callbackValue);
    }

    [Fact]
    public void PermissionSelector_InvokesCallback_WhenPermissionDeselected()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        var selectedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "perm:users:read"
        };
        HashSet<string>? callbackValue = null;

        using var cut = Render<PermissionSelector>(parameters => parameters
            .Add(p => p.SelectedPermissions, selectedPermissions)
            .Add(p => p.SelectedPermissionsChanged, value => callbackValue = value));

        // Act
        var checkbox = cut.Find("input[type='checkbox'][value='perm:users:read']");
        checkbox.Change(false);

        // Assert
        Assert.NotNull(callbackValue);
        Assert.DoesNotContain("perm:users:read", callbackValue);
    }

    [Fact]
    public void PermissionSelector_HandlesPermissionsWithoutResourceGroup()
    {
        // Arrange - Permissions without standard "perm:resource:action" format
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "admin", Description = "Admin permission" }
        });

        // Act
        using var cut = Render<PermissionSelector>();

        // Assert
        // Should fall back to "Other" group
        Assert.Contains("Other", cut.Markup);
        Assert.Contains("admin", cut.Markup);
    }

    [Fact]
    public void PermissionSelector_GroupsAreOpenByDefault()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        // Act
        using var cut = Render<PermissionSelector>();

        // Assert
        var detailsElement = cut.Find("details");
        Assert.True(detailsElement.HasAttribute("open"));
    }

    [Fact]
    public void PermissionSelector_HasAccessibilityAttributes()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" }
        });

        // Act
        using var cut = Render<PermissionSelector>();

        // Assert
        var checkbox = cut.Find("input[type='checkbox']");
        Assert.Equal("perm:users:read", checkbox.GetAttribute("aria-label"));
    }

    [Fact]
    public void PermissionSelector_HandlesMultipleGroupsCorrectly()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:users:read", Description = "Read users" },
            new PermissionInfo { Name = "perm:users:write", Description = "Write users" },
            new PermissionInfo { Name = "perm:roles:read", Description = "Read roles" },
            new PermissionInfo { Name = "perm:endpoints:read", Description = "Read endpoints" }
        });

        // Act
        using var cut = Render<PermissionSelector>();

        // Assert
        var groups = cut.FindAll("details");
        Assert.Equal(3, groups.Count); // users, roles, endpoints

        var checkboxes = cut.FindAll("input[type='checkbox']");
        Assert.Equal(4, checkboxes.Count);
    }

    [Fact]
    public void PermissionSelector_SortsGroupsAlphabetically()
    {
        // Arrange
        SetupPermissions(new[]
        {
            new PermissionInfo { Name = "perm:zebra:read", Description = "Zebra" },
            new PermissionInfo { Name = "perm:alpha:read", Description = "Alpha" },
            new PermissionInfo { Name = "perm:beta:read", Description = "Beta" }
        });

        // Act
        using var cut = Render<PermissionSelector>();

        // Assert
        // ponytail: ToList() sidesteps bunit 1.40's indexer, bound to an AngleSharp 1.2 signature
        // that no longer exists in the security-pinned 1.5.x; drop when bunit ships against 1.5.
        var summaries = cut.FindAll("summary").ToList();
        Assert.Equal("alpha", summaries[0].TextContent);
        Assert.Equal("beta", summaries[1].TextContent);
        Assert.Equal("zebra", summaries[2].TextContent);
    }

    private void SetupPermissions(PermissionInfo[] permissions)
    {
        // Use reflection to set the Permissions property since it's read-only
        var permissionsProperty = typeof(RoleManagementState).GetProperty("Permissions");
        permissionsProperty?.SetValue(_state, permissions.ToList().AsReadOnly());
    }
}