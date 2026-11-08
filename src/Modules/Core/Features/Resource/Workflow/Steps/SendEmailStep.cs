using System.Text.Json;

using WorkflowEngine.Contracts.Abstractions;

using static Core.Features.Resource.CreateResourceOnLocation.CreateResourceOnLocationEndpoint;

namespace Core.Features.Resource.Workflow.Steps;

internal sealed record SendEmailStep(
    Guid RequestId,
    string Name,
    ProviderName ProviderName,
    JsonDocument? Data
    ) : StepRequest(RequestId, "SendEmail");