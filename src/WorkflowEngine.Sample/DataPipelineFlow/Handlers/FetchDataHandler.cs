using System.Text.Json;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;
using WorkflowEngine.Sample.DataPipelineFlow.Steps;

namespace WorkflowEngine.Sample.DataPipelineFlow.Handlers;

/// <summary>
/// Handles step 2: connects to the source and reads raw records.
/// Throws <see cref="RetryableException"/> when the source is temporarily unavailable.
/// </summary>
internal sealed class FetchDataHandler : IStepHandler<FetchDataStep>
{
    public async Task<StepResult> HandleAsync(FetchDataStep request, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("┌─────────────────────────────────────────────────┐");
        Console.WriteLine("│  Step 2/5 — Fetch Data                          │");
        Console.WriteLine("└─────────────────────────────────────────────────┘");
        Console.WriteLine($"  Reading records from: {request.SourcePath}");
        Console.WriteLine("  Establishing source connection...");

        await Task.Delay(TimeSpan.FromSeconds(1), ct);
        Console.WriteLine("  Streaming records...");
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        const int recordCount = 1_250;
        Console.WriteLine($"  ✓ Fetched {recordCount:N0} records.");

        var data = JsonDocument.Parse($$"""{"recordCount":{{recordCount}}}""");
        return new StepResult(data);
    }
}