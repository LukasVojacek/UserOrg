using Core.Features.Request.Notifications;
using Core.Features.Resource.Workflow.Steps;
using Core.Features.ResourceRequest.Flow;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;

namespace Core.Features.Resource.Workflow.Handlers;

internal sealed class SendEmailHandler : IStepHandler<SendEmailStep>
{
    private readonly INotificationSender _notificationSender;
    private readonly IProviderRepository _providerRepository;

    public SendEmailHandler(INotificationSender notificationSender, IProviderRepository providerRepository)
    {
        _notificationSender = notificationSender ?? throw new ArgumentNullException(nameof(notificationSender));
        _providerRepository = providerRepository ?? throw new ArgumentNullException(nameof(providerRepository));
    }

    public async Task<StepResult> HandleAsync(SendEmailStep request, CancellationToken ct)
    {
        var token = ResourceRequestToken.Generate();

        var provider = await _providerRepository.FindAsync(request.ProviderName, ct)
            ?? throw new PermanentException("PROVIDER_NOT_FOUND", $"Provider {request.ProviderName} was not found.");

        if (provider.SupportEmail.HasValue)
            await _notificationSender.SendRequestCreatedToRequesterAsync(provider.SupportEmail.Value,
                request.RequestId,
                token,
                ResourceName.Create(request.Name), ct);

        return new StepResult(request.Data);
    }
}