using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Ports;

namespace WorkflowEngine.Sample.Infrastructure;

/// <summary>
/// Demo callback adapter that prints the terminal event to the console with detailed formatting.
/// Shows how the callback mechanism works — this is called when a workflow reaches Completed or Failed.
/// In production, replace with an HTTP POST adapter, a message bus publisher, gRPC endpoint, etc.
/// </summary>
internal sealed class ConsoleCallbackAdapter : ICallbackPort
{
    public Task SendAsync(RequestAggregate aggregate, CancellationToken ct = default)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("┌─ CALLBACK NOTIFICATION ─────────────────────────────");
        Console.WriteLine($"│ Request ID:      {aggregate.RequestId}");
        Console.WriteLine($"│ Status:          {aggregate.Status}");
        Console.WriteLine($"│ Strategy:        {aggregate.Strategy}");
        Console.WriteLine($"│ Client ID:       {aggregate.ClientId ?? "(anonymous)"}");
        Console.WriteLine($"│ Callback URL:    {aggregate.CallbackUrl ?? "(not set)"}");
        if (!string.IsNullOrEmpty(aggregate.ErrorCode))
        {
            Console.WriteLine($"│ Error Code:      {aggregate.ErrorCode}");
            Console.WriteLine($"│ Error Message:   {aggregate.ErrorMessage}");
        }
        Console.WriteLine("└──────────────────────────────────────────────────────");
        Console.ResetColor();
        Console.WriteLine();
        return Task.CompletedTask;
    }
}