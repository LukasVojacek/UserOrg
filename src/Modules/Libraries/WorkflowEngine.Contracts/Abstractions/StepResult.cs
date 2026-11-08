using System.Text.Json;

namespace WorkflowEngine.Contracts.Abstractions;

/// <summary>
/// Data produced by a step handler after successfully executing one domain state.
/// Serialized to <c>RequestSteps.Result</c> and forwarded to the strategy's
/// <c>Transition</c> method to produce the next typed message.
/// </summary>
/// <param name="Data">
/// Optional JSON payload. A <c>null</c> value is valid — it signals that the step
/// produced no data (e.g. a notification step with fire-and-forget side effects).
/// </param>
public sealed record StepResult(JsonDocument? Data, bool hasContinue = true);