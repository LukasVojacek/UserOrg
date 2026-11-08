using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Sample.DataPipelineFlow.Steps;

namespace WorkflowEngine.Sample.DataPipelineFlow.Handlers;

/// <summary>
/// Handles step 1: validates pipeline configuration and allocates necessary resources.
/// </summary>
internal sealed class InitializePipelineHandler : IStepHandler<InitializePipelineStep>
{
    public async Task<StepResult> HandleAsync(InitializePipelineStep request, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("┌─────────────────────────────────────────────────┐");
        Console.WriteLine("│  Step 1/5 — Initialize Pipeline                 │");
        Console.WriteLine("└─────────────────────────────────────────────────┘");
        Console.WriteLine($"  Pipeline ID : {request.PipelineId}");
        Console.WriteLine($"  Source      : {request.SourcePath}");
        Console.WriteLine($"  Target      : {request.TargetPath}");
        Console.WriteLine("  Validating configuration and allocating resources...");

        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        Console.WriteLine("  ✓ Configuration valid. Resources allocated.");

        return new StepResult(null);
    }
}