using Core.Domain.Provider;
using Core.Features.Request.Notifications;
using Core.Features.Resource.Workflow.Handlers;
using Core.Features.Resource.Workflow.Steps;
using Core.Shared;

using Moq;

using Xunit;

namespace Core.UnitTests.Features.Resource.Workflow.Handlers;

/// <summary>
/// Unit tests for <see cref="SendEmailHandler"/>. Pins that the handler sends the resource-created
/// notification to the REAL primary-investigator recipient threaded through the workflow — not the
/// empty <c>Email.Create("")</c> the step used to send to (plan item E1). The email template/body stays
/// deferred to #239; only the recipient is asserted here.
/// </summary>
public sealed class SendEmailHandlerTests
{
    private const string ProviderEmail = "pa@example.com";
    private const string ResourceNameValue = "test-resource";

    [Fact(DisplayName = "Should_send_notification_to_primary_investigator_email_When_step_carries_it")]
    public async Task Should_send_notification_to_primary_investigator_email_When_step_carries_it()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var notificationSender = new Mock<INotificationSender>();
        var providerRepository = new Mock<IProviderRepository>();
        providerRepository.Setup(p => p.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Provider.Create(ProviderName.From("test-provider"), 1, null, Email.Create(ProviderEmail)));
        var handler = new SendEmailHandler(notificationSender.Object, providerRepository.Object);

        var step = new SendEmailStep(
            RequestId: requestId,
            Name: ResourceNameValue,
            ProviderName: ProviderName.From("test-provider"),
            Data: null);

        // Act
        await handler.HandleAsync(step, CancellationToken.None);

        // Assert — the real PI recipient reaches the sender, not an empty address.
        notificationSender.Verify(
            s => s.SendRequestCreatedToRequesterAsync(
                Email.Create(ProviderEmail),
                requestId,
                It.IsAny<string>(),
                ResourceName.Create(ResourceNameValue),
                It.IsAny<CancellationToken>()));
    }
}
