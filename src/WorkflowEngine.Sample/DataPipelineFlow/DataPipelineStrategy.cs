using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Sample.DataPipelineFlow.Steps;

namespace WorkflowEngine.Sample.DataPipelineFlow;

/// <summary>
/// Defines the 5-step data pipeline flow as a typed state machine.
/// <list type="number">
///   <item><see cref="InitializePipelineStep"/> — validate config &amp; allocate resources</item>
///   <item><see cref="FetchDataStep"/> — stream records from the source</item>
///   <item><see cref="TransformDataStep"/> — apply rules, produce quality report</item>
///   <item><see cref="AwaitOperatorApprovalStep"/> — human gate: operator must approve or reject</item>
///   <item><see cref="LoadDataStep"/> — write approved data to the target (terminal)</item>
/// </list>
/// </summary>
internal sealed class DataPipelineStrategy : IRequestStrategy
{
    public const string StrategyName = "data-pipeline";

    public string Name => StrategyName;
    public Type InitialStepType => typeof(InitializePipelineStep);

    /// <summary>
    /// Pure, side-effect-free transition function.
    /// Returns the next step to enqueue, or <c>null</c> when the flow is done.
    /// </summary>
    public StepRequest? Transition(StepRequest completedStep, StepResult? stepResult, IRequestSnapshot request)
    {
        return completedStep switch
        {
            InitializePipelineStep s => new FetchDataStep(s.RequestId, s.PipelineId, s.SourcePath),

            // Step 2 → Step 3: forward the record count so the transform handler can report progress
            FetchDataStep s => new TransformDataStep(
                s.RequestId,
                s.PipelineId,
                stepResult?.Data?.RootElement.GetProperty("recordCount").GetInt32() ?? 0),

            TransformDataStep s => new AwaitOperatorApprovalStep(
                s.RequestId,
                s.PipelineId,
                stepResult?.Data?.RootElement.GetProperty("summary").GetString() ?? "(no summary)"),

            AwaitOperatorApprovalStep s => new LoadDataStep(
                s.RequestId,
                s.PipelineId,
                request.Payload.RootElement.GetProperty("TargetPath").GetString()!),

            LoadDataStep => null,

            _ => throw new InvalidOperationException(
                $"Unrecognised step type '{completedStep.GetType().Name}' in {nameof(DataPipelineStrategy)}.")
        };
    }
}