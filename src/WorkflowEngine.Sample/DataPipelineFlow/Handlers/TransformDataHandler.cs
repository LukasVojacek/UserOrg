using System.Text.Json;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Sample.DataPipelineFlow.Steps;

namespace WorkflowEngine.Sample.DataPipelineFlow.Handlers;

/// <summary>
/// Handles step 3: applies business transformation rules, validates data quality,
/// and produces a summary report for the operator to review.
/// </summary>
internal sealed class TransformDataHandler : IStepHandler<TransformDataStep>
{
    public async Task<StepResult> HandleAsync(TransformDataStep request, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("┌─────────────────────────────────────────────────┐");
        Console.WriteLine("│  Step 3/5 — Transform Data                      │");
        Console.WriteLine("└─────────────────────────────────────────────────┘");
        Console.WriteLine($"  Processing {request.FetchedRecordCount:N0} records for pipeline '{request.PipelineId}'...");

        Console.WriteLine("  [░░░░░░░░░░] Normalising fields...");
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        Console.WriteLine("  [████░░░░░░] Applying business rules...");
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        Console.WriteLine("  [████████░░] Validating data quality...");
        await Task.Delay(TimeSpan.FromSeconds(1), ct);

        var validRecords = (int)(request.FetchedRecordCount * 0.98);
        var rejectedRecords = request.FetchedRecordCount - validRecords;

        Console.WriteLine("  [██████████] Done.");
        Console.WriteLine($"  ✓ Transform complete: {validRecords:N0} valid, {rejectedRecords} rejected.");

        var summary = $"{validRecords}/{request.FetchedRecordCount} records passed quality checks ({rejectedRecords} rejected as duplicates). All business rules satisfied. Output ready for load.";

        var data = JsonDocument.Parse($$"""
            {
                "validRecords": {{validRecords}},
                "rejectedRecords": {{rejectedRecords}},
                "summary": {{JsonSerializer.Serialize(summary)}}
            }
            """);

        return new StepResult(data);
    }
}