using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;
using WorkflowEngine.Sample.DataPipelineFlow.Steps;

namespace WorkflowEngine.Sample.DataPipelineFlow.Handlers;

/// <summary>
/// Handles step 4: blocks execution until a human operator reviews the transformation
/// report and types a decision.
/// <para>
/// In production you would implement this as a long-poll or callback rather than
/// blocking a thread — the handler here demonstrates the pattern in a console context.
/// </para>
/// </summary>
internal sealed class AwaitOperatorApprovalHandler : IStepHandler<AwaitOperatorApprovalStep>
{
    public async Task<StepResult> HandleAsync(AwaitOperatorApprovalStep request, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("┌─────────────────────────────────────────────────┐");
        Console.WriteLine("│  Step 4/5 — Awaiting Operator Approval  ⏸       │");
        Console.WriteLine("└─────────────────────────────────────────────────┘");
        Console.WriteLine($"  Pipeline   : {request.PipelineId}");
        Console.WriteLine();
        Console.WriteLine("  TRANSFORMATION REPORT");
        Console.WriteLine("  ─────────────────────────────────────────────────");
        Console.WriteLine($"  {request.TransformationSummary}");
        Console.WriteLine("  ─────────────────────────────────────────────────");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("  ► Review the report above and decide:");
        Console.WriteLine("    Type  'approve'  to proceed with the load.");
        Console.WriteLine("    Type  'reject'   to abort the pipeline.");
        Console.ResetColor();
        Console.WriteLine();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            Console.Write("  Your decision: ");
            var input = Console.ReadLine()?.Trim().ToLowerInvariant();

            switch (input)
            {
                case "approve":
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  ✓ Pipeline approved by operator.");
                Console.ResetColor();
                // A short pause to let the operator see the confirmation.
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                return new StepResult(null);

                case "reject":
                throw new PermanentException(
                    "OPERATOR_REJECTED",
                    $"Pipeline '{request.PipelineId}' was rejected by the operator after transformation review.");

                default:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  ✗ Invalid input — please type 'approve' or 'reject'.");
                Console.ResetColor();
                break;
            }
        }
    }
}