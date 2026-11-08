using WorkflowEngine.Contracts.Exceptions;

namespace WorkflowEngine.Contracts.Exceptions;

/// <summary>
/// Thrown by a step handler to signal a transient, retryable failure.
/// The worker records <c>RetryScheduled</c> and re-enqueues the step after the configured back-off.
/// </summary>
/// <example>
/// <code>
/// throw new RetryableException("HEAPPE_TIMEOUT", "HEAppE returned 503 — will retry");
/// </code>
/// </example>
public sealed class RetryableException : Exception
{
    /// <summary>Structured error information persisted to the step record.</summary>
    public ErrorInfo ErrorInfo { get; }

    /// <param name="errorCode">Short machine-readable code (e.g. <c>"HEAPPE_TIMEOUT"</c>).</param>
    /// <param name="message">Human-readable description of the transient failure.</param>
    public RetryableException(string errorCode, string message)
        : base(message)
    {
        ErrorInfo = new ErrorInfo(errorCode, message);
    }

    /// <param name="errorCode">Short machine-readable code.</param>
    /// <param name="message">Human-readable description.</param>
    /// <param name="innerException">Original exception that caused the transient failure.</param>
    public RetryableException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorInfo = new ErrorInfo(errorCode, message);
    }
}