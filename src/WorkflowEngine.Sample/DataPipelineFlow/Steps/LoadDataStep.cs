using WorkflowEngine.Contracts.Abstractions;

namespace WorkflowEngine.Sample.DataPipelineFlow.Steps;

/// <summary>Step 5 — loads the transformed data into the target destination.</summary>
internal sealed record LoadDataStep(
    Guid RequestId,
    string PipelineId,
    string TargetPath)
    : StepRequest(RequestId, "LoadData");