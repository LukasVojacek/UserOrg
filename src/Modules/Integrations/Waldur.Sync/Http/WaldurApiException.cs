using System.Net;

namespace Waldur.Sync.Http;

/// <summary>
/// Thrown when a Waldur API request fails — including a failure on <b>any page</b> of a paginated
/// fetch. The legacy collector silently truncated (<c>yield break</c>) on a mid-pagination error,
/// which made a partial list indistinguishable from a complete one (ADR-0056/A4, D7). This client
/// fails loudly instead, so callers never mistake a partial snapshot for a complete one.
/// </summary>
public sealed class WaldurApiException : Exception
{
    public WaldurApiException(string message)
        : base(message)
    {
    }

    public WaldurApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public HttpStatusCode? StatusCode { get; init; }

    public int? Page { get; init; }

    public Uri? RequestUri { get; init; }
}