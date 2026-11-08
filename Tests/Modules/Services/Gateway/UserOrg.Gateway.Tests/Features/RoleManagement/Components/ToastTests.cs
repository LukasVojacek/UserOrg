using Bunit;

using UserOrg.Gateway.Features.RoleManagement.Components;

namespace UserOrg.Gateway.Tests.Features.RoleManagement.Components;

/// <summary>
/// Unit tests for Toast component.
/// </summary>
public sealed class ToastTests : Bunit.TestContext
{
    [Fact]
    public void Toast_DoesNotRender_WhenMessageIsNull()
    {
        // Act
        using var cut = Render<Toast>(parameters => parameters
            .Add(p => p.Message, null));

        // Assert
        Assert.Empty(cut.Markup);
    }

    [Fact]
    public void Toast_DoesNotRender_WhenMessageIsEmpty()
    {
        // Act
        using var cut = Render<Toast>(parameters => parameters
            .Add(p => p.Message, string.Empty));

        // Assert
        Assert.Empty(cut.Markup);
    }

    [Fact]
    public void Toast_RendersWithMessage_WhenMessageProvided()
    {
        // Arrange
        var message = "Test notification message";

        // Act
        using var cut = Render<Toast>(parameters => parameters
            .Add(p => p.Message, message));

        // Assert
        Assert.Contains("rm-toast", cut.Markup);
        Assert.Contains(message, cut.Markup);
    }

    [Fact]
    public void Toast_RendersWithCorrectType_WhenTypeIsSuccess()
    {
        // Act
        using var cut = Render<Toast>(parameters => parameters
            .Add(p => p.Message, "Success!")
            .Add(p => p.Type, "success"));

        // Assert
        Assert.Contains("rm-toast--success", cut.Markup);
    }

    [Fact]
    public void Toast_RendersWithCorrectType_WhenTypeIsError()
    {
        // Act
        using var cut = Render<Toast>(parameters => parameters
            .Add(p => p.Message, "Error occurred")
            .Add(p => p.Type, "error"));

        // Assert
        Assert.Contains("rm-toast--error", cut.Markup);
    }

    [Fact]
    public void Toast_RendersWithDefaultType_WhenTypeNotSpecified()
    {
        // Act
        using var cut = Render<Toast>(parameters => parameters
            .Add(p => p.Message, "Info message"));

        // Assert
        Assert.Contains("rm-toast--info", cut.Markup);
    }

    [Fact]
    public void Toast_HasCloseButton_WhenRendered()
    {
        // Act
        using var cut = Render<Toast>(parameters => parameters
            .Add(p => p.Message, "Test message"));

        // Assert
        var closeButton = cut.Find("button.rm-toast-close");
        Assert.NotNull(closeButton);
        Assert.Contains("×", closeButton.TextContent);
    }

    [Fact]
    public void Toast_InvokesOnDismiss_WhenCloseButtonClicked()
    {
        // Arrange
        var dismissCalled = false;
        using var cut = Render<Toast>(parameters => parameters
            .Add(p => p.Message, "Test message")
            .Add(p => p.OnDismiss, () => dismissCalled = true));

        // Act
        var closeButton = cut.Find("button.rm-toast-close");
        closeButton.Click();

        // Assert
        Assert.True(dismissCalled);
    }

    [Fact]
    public void Toast_ClearsMessage_WhenDismissed()
    {
        // Arrange
        using var cut = Render<Toast>(parameters => parameters
            .Add(p => p.Message, "Test message"));

        // Act
        var closeButton = cut.Find("button.rm-toast-close");
        closeButton.Click();

        // Assert - component should re-render and be empty
        cut.WaitForAssertion(() => Assert.Empty(cut.Markup));
    }

    [Fact]
    public void Toast_HasAccessibilityAttributes()
    {
        // Act
        using var cut = Render<Toast>(parameters => parameters
            .Add(p => p.Message, "Accessible message"));

        // Assert
        var toast = cut.Find(".rm-toast");
        Assert.Equal("alert", toast.GetAttribute("role"));
        Assert.Equal("polite", toast.GetAttribute("aria-live"));

        var closeButton = cut.Find("button.rm-toast-close");
        Assert.Equal("Dismiss notification", closeButton.GetAttribute("aria-label"));
    }
}