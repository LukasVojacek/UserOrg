using Core.Features.Request.Notifications;

using Microsoft.Extensions.Options;

using UserOrg.Shared.Email;

namespace Core.UnitTests.Requests;

/// <summary>
/// Pins the template selection, recipients and template arguments the
/// <see cref="MailNotificationSender"/> hands to <see cref="IEmailSender"/> for the four
/// resource-assignment-request mail kinds added for issue #251 (B03). Content mirrors the legacy
/// <c>ProjectRequestService</c> templates.
/// </summary>
public sealed class MailNotificationSenderTests
{
    private const string LinkBase = "https://ui.example.com/resource-request-actions";
    private const string ProjectShort = "proj-1";
    private const string ResourceValue = "cluster-a";
    private const string RequestSlug = "proj-1-cluster-a";

    private static readonly ProjectShortName Project = new(ProjectShort);
    private static readonly ResourceName Resource = ResourceName.Create(ResourceValue);

    [Fact(DisplayName = "PiApprovalRequested sends the PI template with the token action link")]
    public async Task Should_send_pi_approval_template_with_action_link_When_PiApprovalRequested()
    {
        var (sender, emailSender) = CreateSender(durationHours: 48);
        // Token carries characters that must be URL-escaped so the link never depends on the token alphabet.
        const string token = "abc/123+xyz";

        await sender.SendPiApprovalRequestedAsync(
            Email.Create("pi@example.com"), RequestSlug, token, Project, Resource, CancellationToken.None);

        var message = emailSender.LastMessage!;
        Assert.Equal("NewProjectAssignment_PM", message.TemplateName);
        Assert.Equal("pi@example.com", Assert.Single(message.Receivers));
        // Legacy 4-arg contract [Id, link, ProjectShortName, validForHours]; resource folded into the project slot.
        Assert.Equal(
            [RequestSlug, $"{LinkBase}/{Uri.EscapeDataString(token)}", ResourceValue, "48"],
            message.TemplateArguments);
        Assert.Null(message.Cc);
    }

    [Fact(DisplayName = "RequesterAssigned sends the RequestState template with Assigned")]
    public async Task Should_send_request_state_assigned_When_RequesterAssigned()
    {
        var (sender, emailSender) = CreateSender();

        await sender.SendRequesterAssignedAsync(
            Email.Create("user@example.com"), RequestSlug, Project, Resource, CancellationToken.None);

        var message = emailSender.LastMessage!;
        Assert.Equal("RequestState", message.TemplateName);
        Assert.Equal("user@example.com", Assert.Single(message.Receivers));
        // Legacy 3-arg contract [Id, ProjectShortName, State]; resource folded into the project slot.
        Assert.Equal([RequestSlug, $"{ProjectShort} / {ResourceValue}", "Assigned"], message.TemplateArguments);
    }

    [Fact(DisplayName = "RequesterRejected sends the RequestState template with the reason")]
    public async Task Should_send_request_state_rejected_with_reason_When_RequesterRejected()
    {
        var (sender, emailSender) = CreateSender();

        await sender.SendRequesterRejectedAsync(
            Email.Create("user@example.com"), RequestSlug, Project, Resource, "not eligible", CancellationToken.None);

        var message = emailSender.LastMessage!;
        Assert.Equal("RequestState", message.TemplateName);
        // 3-arg contract; reason folded into the State slot.
        Assert.Equal([RequestSlug, $"{ProjectShort} / {ResourceValue}", "Rejected: not eligible"], message.TemplateArguments);
    }

    [Fact(DisplayName = "RequesterRejected sends an empty reason argument When reason is null")]
    public async Task Should_send_empty_reason_When_RequesterRejected_without_reason()
    {
        var (sender, emailSender) = CreateSender();

        await sender.SendRequesterRejectedAsync(
            Email.Create("user@example.com"), RequestSlug, Project, Resource, null, CancellationToken.None);

        var message = emailSender.LastMessage!;
        Assert.Equal([RequestSlug, $"{ProjectShort} / {ResourceValue}", "Rejected"], message.TemplateArguments);
    }

    [Fact(DisplayName = "ProviderResourceNeeded sends to the provider support email and CCs the requester")]
    public async Task Should_send_provider_template_and_cc_requester_When_ProviderResourceNeeded()
    {
        var (sender, emailSender) = CreateSender();

        await sender.SendProviderResourceNeededAsync(
            Email.Create("support@provider.example.com"), RequestSlug, Project, Resource,
            Email.Create("user@example.com"), CancellationToken.None);

        var message = emailSender.LastMessage!;
        Assert.Equal("NewProjectAssignment", message.TemplateName);
        Assert.Equal("support@provider.example.com", Assert.Single(message.Receivers));
        Assert.Equal([RequestSlug, ProjectShort, ResourceValue], message.TemplateArguments);
        Assert.Equal("user@example.com", Assert.Single(message.Cc!));
    }

    private static (MailNotificationSender Sender, FakeEmailSender EmailSender) CreateSender(int durationHours = 120)
    {
        var emailSender = new FakeEmailSender();

        var linkOptions = Options.Create(new ResourceRequestActionLinkOptions
        {
            BaseUrl = LinkBase,
            DurationInHours = durationHours,
        });

        var sender = new MailNotificationSender(emailSender, linkOptions);
        return (sender, emailSender);
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public EmailMessage? LastMessage { get; private set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            LastMessage = message;
            return Task.CompletedTask;
        }
    }
}
