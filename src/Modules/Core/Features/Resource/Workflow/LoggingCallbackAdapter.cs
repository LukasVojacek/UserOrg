using Microsoft.Extensions.Logging;

using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Ports;

namespace Core.Features.Resource.Workflow;

/// <summary>
/// Platform <see cref="ICallbackPort"/> MVP (ADR-0056, item A4): logs the terminal
/// <see cref="RequestAggregate"/> — its id and flow status — when a request reaches Completed or Failed.
/// <para>
/// Real outbound notification transport (HTTP POST, message bus, …) is deferred to issue #239; this
/// adapter is the single seam to swap when it lands, and the engine contract
/// (<see cref="ICallbackPort.SendAsync"/>) does not change.
/// </para>
/// </summary>
internal sealed class LoggingCallbackAdapter : ICallbackPort
{
    private readonly ILogger<LoggingCallbackAdapter> _logger;

    public LoggingCallbackAdapter(ILogger<LoggingCallbackAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public Task SendAsync(RequestAggregate aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        _logger.LogInformation(
            "Workflow request {RequestId} reached terminal state {Status}.",
            aggregate.RequestId,
            aggregate.Status);

        return Task.CompletedTask;
    }
}