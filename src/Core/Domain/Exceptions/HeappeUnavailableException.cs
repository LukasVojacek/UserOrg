namespace UserOrganizationManagement.Domain.Exceptions;

/// <summary>
/// Thrown when every HEAppE cluster query for a TemplatesInfo build fails (instance down or mid-rebuild)
/// and the retry budget is exhausted. Surfaced to API callers as HTTP 503 Service Unavailable so the
/// response is not a misleading empty 200. See <c>GlobalExceptionHandler</c>.
/// </summary>
public class HeappeUnavailableException : Exception
{
    /// <summary>
    /// The HEAppE instances whose queries failed, with a sanitized descriptor and a cause code each. Empty
    /// when the exception was constructed without failure detail. Surfaced in the 503 response (ADR 0074).
    /// </summary>
    public IReadOnlyList<HeappeFailureInfo> Failures { get; } = [];

    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public HeappeUnavailableException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor carrying the per-instance failures that caused the total degradation, surfaced to API
    /// callers in the 503 ProblemDetails so the cause is discoverable without reading logs (ADR 0074).
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="failures">One entry per failed HEAppE instance (sanitized descriptor + cause).</param>
    public HeappeUnavailableException(string message, IReadOnlyList<HeappeFailureInfo> failures) : base(message)
    {
        Failures = failures;
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public HeappeUnavailableException(string message, Exception innerException) : base(message, innerException)
    {

    }

    public HeappeUnavailableException()
    {
    }
    #endregion
}