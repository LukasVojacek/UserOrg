using WorkflowEngine.Contracts.Abstractions;

namespace WorkflowEngine.Sample.DataPipelineFlow.Steps;

/// <summary>Step 3 — applies transformation rules and produces an operator-visible summary report.</summary>
internal sealed record TransformDataStep(
    Guid RequestId,
    string PipelineId,
    int FetchedRecordCount)
    : StepRequest(RequestId, "TransformData");