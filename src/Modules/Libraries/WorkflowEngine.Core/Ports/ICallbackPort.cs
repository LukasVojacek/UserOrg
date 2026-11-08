using WorkflowEngine.Core.Domain.Request;

namespace WorkflowEngine.Core.Ports;

/// <summary>
/// Driven port — abstraction over the callback delivery mechanism.
/// Invoked once after the flow completes. A request that transitions to <c>Failed</c>
/// (permanent exception or exhausted retries) produces no callback; a consumer that
/// needs to observe failure must poll <see cref="WorkflowEngine.Contracts.Ports.IWorkflowClient.GetStatusAsync"/>.
/// Tracked as a gap: https://opencode.it4i.eu/lexis-platform/backend/userorg/-/work_items/271
/// Implemented by the consuming project (e.g. HTTP POST, Azure Service Bus, etc.).
/// </summary>
public interface ICallbackPort
{
    /// <summary>
    /// Sends a terminal-state notification to the callback target recorded on the aggregate.
    /// Implementations are expected to be idempotent — the worker may call this more than once
    /// if the process restarts after delivery but before the next <c>SaveAsync</c>.
    /// </summary>
    Task SendAsync(RequestAggregate aggregate, CancellationToken ct = default);
}