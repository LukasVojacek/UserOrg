namespace WorkflowEngine.Contracts.Exceptions;

/// <summary>
/// Structured error information attached to workflow exceptions.
/// Persisted to <c>RequestSteps.ErrorCode</c> and <c>RequestSteps.ErrorMessage</c>.
/// </summary>
/// <param name="ErrorCode">Short, machine-readable code (e.g. <c>"HEAPPE_TIMEOUT"</c>).</param>
/// <param name="ErrorMessage">Human-readable description of the failure.</param>
public sealed record ErrorInfo(string ErrorCode, string ErrorMessage);