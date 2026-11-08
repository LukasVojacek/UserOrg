namespace WorkflowEngine.Contracts.Exceptions;

/// <summary>
/// Thrown by a step handler to signal a permanent, non-retryable failure.
/// The worker marks the step and the flow as <c>Failed</c> immediately, bypassing the retry budget.
/// </summary>
/// <example>
/// <code>
/// throw new PermanentException("INVALID_PAYLOAD", "Payload failed validation: missing required field 'projectId'");
/// </code>
/// </example>
public sealed class PermanentException : Exception
{
    /// <summary>Structured error information persisted to the step and flow records.</summary>
    public ErrorInfo ErrorInfo { get; }

    /// <param name="errorCode">Short machine-readable code (e.g. <c>"INVALID_PAYLOAD"</c>).</param>
    /// <param name="message">Human-readable description of the permanent failure.</param>
    public PermanentException(string errorCode, string message)
        : base(message)
    {
        ErrorInfo = new ErrorInfo(errorCode, message);
    }

    /// <param name="errorCode">Short machine-readable code.</param>
    /// <param name="message">Human-readable description.</param>
    /// <param name="innerException">Original exception that caused the permanent failure.</param>
    public PermanentException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorInfo = new ErrorInfo(errorCode, message);
    }
}