using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Sample.DataPipelineFlow.Steps;

namespace WorkflowEngine.Sample.DataPipelineFlow.Handlers;

/// <summary>
/// Handles step 5: writes the transformed data to the configured target path.
/// </summary>
internal sealed class LoadDataHandler : IStepHandler<LoadDataStep>
{
    public async Task<StepResult> HandleAsync(LoadDataStep request, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("┌─────────────────────────────────────────────────┐");
        Console.WriteLine("│  Step 5/5 — Load Data                           │");
        Console.WriteLine("└─────────────────────────────────────────────────┘");
        Console.WriteLine($"  Loading data for pipeline '{request.PipelineId}'...");
        Console.WriteLine($"  Target: {request.TargetPath}");

        Console.WriteLine("  [░░░░░░░░░░] Opening target connection...");
        await Task.Delay(TimeSpan.FromSeconds(1), ct);

        Console.WriteLine("  [████░░░░░░] Writing batches...");
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        Console.WriteLine("  [████████░░] Flushing and committing...");
        await Task.Delay(TimeSpan.FromSeconds(1), ct);

        Console.WriteLine("  [██████████] Done.");
        Console.WriteLine("  ✓ Data successfully loaded to target.");

        return new StepResult(null);
    }
}