using WorkflowEngine.Contracts.Abstractions;

namespace WorkflowEngine.Sample.DataPipelineFlow.Steps;

/// <summary>Step 1 — validates configuration and initialises the pipeline context.</summary>
internal sealed record InitializePipelineStep(
    Guid RequestId,
    string PipelineId,
    string SourcePath,
    string TargetPath)
    : StepRequest(RequestId, "InitializePipeline");