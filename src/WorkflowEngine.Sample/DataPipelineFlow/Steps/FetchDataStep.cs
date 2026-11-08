using WorkflowEngine.Contracts.Abstractions;

namespace WorkflowEngine.Sample.DataPipelineFlow.Steps;

/// <summary>Step 2 — reads raw records from the configured source path.</summary>
internal sealed record FetchDataStep(
    Guid RequestId,
    string PipelineId,
    string SourcePath)
    : StepRequest(RequestId, "FetchData");