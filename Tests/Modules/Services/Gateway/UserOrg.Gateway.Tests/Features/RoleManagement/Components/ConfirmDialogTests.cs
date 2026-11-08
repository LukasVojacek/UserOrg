using Bunit;

using UserOrg.Gateway.Features.RoleManagement.Components;

namespace UserOrg.Gateway.Tests.Features.RoleManagement.Components;

/// <summary>
/// Unit tests for ConfirmDialog component.
/// </summary>
public sealed class ConfirmDialogTests : Bunit.TestContext
{
    [Fact]
    public void ConfirmDialog_DoesNotRender_WhenIsOpenIsFalse()
    {
        // Act
        using var cut = Render<ConfirmDialog>(parameters => parameters
            .Add(p => p.IsOpen, false)
            .Add(p => p.Message, "Test message"));

        // Assert
        Assert.Empty(cut.Markup);
    }

    [Fact]
    public void ConfirmDialog_Renders_WhenIsOpenIsTrue()
    {
        // Act
        using var cut = Render<ConfirmDialog>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Message, "Test message"));

        // Assert
        Assert.Contains("rm-dialog-overlay", cut.Markup);
        Assert.Contains("rm-dialog", cut.Markup);
    }

    [Fact]
    public void ConfirmDialog_DisplaysTitle_WhenProvided()
    {
        // Arrange
        var title = "Delete Confirmation";

        // Act
        using var cut = Render<ConfirmDialog>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Title, title)
            .Add(p => p.Message, "Are you sure?"));

        // Assert
        var titleElement = cut.Find("#dialogTitle");
        Assert.Equal(title, titleElement.TextContent);
    }

    [Fact]
    public void ConfirmDialog_DisplaysDefaultTitle_WhenNotProvided()
    {
        // Act
        using var cut = Render<ConfirmDialog>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Message, "Are you sure?"));

        // Assert
        var titleElement = cut.Find("#dialogTitle");
        Assert.Equal("Confirm", titleElement.TextContent);
    }

    [Fact]
    public void ConfirmDialog_DisplaysMessage_WhenProvided()
    {
        // Arrange
        var message = "Are you sure you want to delete this role?";

        // Act
        using var cut = Render<ConfirmDialog>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Message, message));

        // Assert
        var messageElement = cut.Find("#dialogMessage");
        Assert.Equal(message, messageElement.TextContent);
    }

    [Fact]
    public void ConfirmDialog_HasConfirmButton()
    {
        // Act
        using var cut = Render<ConfirmDialog>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Message, "Test"));

        // Assert
        var buttons = cut.FindAll("button");
        var confirmButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Confirm"));
        Assert.NotNull(confirmButton);
    }

    [Fact]
    public void ConfirmDialog_HasCancelButton()
    {
        // Act
        using var cut = Render<ConfirmDialog>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Message, "Test"));

        // Assert
        var buttons = cut.FindAll("button");
        var cancelButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Cancel"));
        Assert.NotNull(cancelButton);
    }

    [Fact]
    public void ConfirmDialog_InvokesOnConfirm_WhenConfirmButtonClicked()
    {
        // Arrange
        var confirmCalled = false;
        using var cut = Render<ConfirmDialog>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Message, "Test")
            .Add(p => p.OnConfirm, () => confirmCalled = true));

        // Act
        var buttons = cut.FindAll("button");
        var confirmButton = buttons.First(b => b.TextContent.Contains("Confirm"));
        confirmButton.Click();

        // Assert
        Assert.True(confirmCalled);
    }

    [Fact]
    public void ConfirmDialog_InvokesOnCancel_WhenCancelButtonClicked()
    {
        // Arrange
        var cancelCalled = false;
        using var cut = Render<ConfirmDialog>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Message, "Test")
            .Add(p => p.OnCancel, () => cancelCalled = true));

        // Act
        var buttons = cut.FindAll("button");
        var cancelButton = buttons.First(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();

        // Assert
        Assert.True(cancelCalled);
    }

    [Fact]
    public void ConfirmDialog_InvokesOnCancel_WhenOverlayClicked()
    {
        // Arrange
        var cancelCalled = false;
        using var cut = Render<ConfirmDialog>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Message, "Test")
            .Add(p => p.OnCancel, () => cancelCalled = true));

        // Act
        var overlay = cut.Find(".rm-dialog-overlay");
        overlay.Click();

        // Assert
        Assert.True(cancelCalled);
    }

    [Fact]
    public void ConfirmDialog_HasCorrectStructure()
    {
        // Note: Testing @onclick:stopPropagation directly in bUnit is complex.
        // The attribute prevents event bubbling to the overlay, but bUnit doesn't
        // simulate bubbling in the same way the browser does.

        using var cut = Render<ConfirmDialog>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Message, "Test"));

        var overlay = cut.Find(".rm-dialog-overlay");
        var dialog = cut.Find(".rm-dialog");

        Assert.NotNull(overlay);
        Assert.NotNull(dialog);
    }

    [Fact]
    public void ConfirmDialog_HasAccessibilityAttributes()
    {
        // Act
        using var cut = Render<ConfirmDialog>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Title, "Test Title")
            .Add(p => p.Message, "Test Message"));

        // Assert
        var dialog = cut.Find(".rm-dialog");
        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("dialogTitle", dialog.GetAttribute("aria-labelledby"));
        Assert.Equal("dialogMessage", dialog.GetAttribute("aria-describedby"));
    }

    [Fact]
    public void ConfirmDialog_ConfirmButtonHasAutofocus()
    {
        // Act
        using var cut = Render<ConfirmDialog>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Message, "Test"));

        // Assert
        var buttons = cut.FindAll("button");
        var confirmButton = buttons.First(b => b.TextContent.Contains("Confirm"));
        Assert.True(confirmButton.HasAttribute("autofocus"));
    }
}