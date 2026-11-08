using WorkflowEngine.Contracts.Abstractions;

namespace WorkflowEngine.Sample.DataPipelineFlow.Steps;

/// <summary>
/// Step 4 — suspends execution until a human operator reviews the transformation
/// report and explicitly approves or rejects the load.
/// </summary>
internal sealed record AwaitOperatorApprovalStep(
    Guid RequestId,
    string PipelineId,
    string TransformationSummary)
    : StepRequest(RequestId, "AwaitOperatorApproval");