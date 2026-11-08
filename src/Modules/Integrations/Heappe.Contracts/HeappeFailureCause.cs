namespace Heappe.Contracts;

/// <summary>
/// Stable, URL-free cause codes for a failed HEAppE query, shared between the producer (the HEAppE
/// integration, which stamps them on <c>Error.Code</c>) and the consumer (Core, which surfaces them in the
/// TemplatesInfo failure response). See ADR 0074. The codes carry no URL or response body, so they are safe
/// to expose to API callers.
/// </summary>
public static class HeappeFailureCause
{
    /// <summary>The instance rejected the credential (HTTP 401/403) — a token/permission problem.</summary>
    public const string Unauthorized = "unauthorized";

    /// <summary>The instance returned a non-success status that is not an auth failure.</summary>
    public const string UpstreamError = "upstream-error";

    /// <summary>The request to the instance timed out.</summary>
    public const string Timeout = "timeout";

    /// <summary>The instance answered but the payload could not be parsed (e.g. a mid-hydration skeleton).</summary>
    public const string InvalidResponse = "invalid-response";

    /// <summary>The instance failed its liveness probe — unreachable within the gate timeout.</summary>
    public const string Unreachable = "unreachable";

    /// <summary>An active link's cluster is absent from the instance's catalog (misconfiguration or hydration).</summary>
    public const string Misconfigured = "misconfigured";

    /// <summary>
    /// The instance answered with the link's cluster, but its catalog carries no active allocation for the
    /// resource UserOrg holds an active link for — an incomplete catalog (mid-refresh), never cached.
    /// </summary>
    public const string AllocationMissing = "allocation-missing";

    /// <summary>Fallback for a failure on our side, or any unrecognized <c>Error.Code</c>.</summary>
    public const string Internal = "internal";

    /// <summary>NexusApi, the registry and proxy in front of every HEAppE instance, is unreachable or its registry is down.</summary>
    public const string NexusUnavailable = "nexus-unavailable";

    /// <summary>NexusApi knows no enabled HEAppE instance under the identifier.</summary>
    public const string HeappeInstanceNotFound = "heappe-instance-not-found";

    /// <summary>NexusApi routed the call, but the HEAppE instance behind the proxy did not answer.</summary>
    public const string HeappeUpstreamUnavailable = "heappe-upstream-unavailable";

    /// <summary>
    /// The token exchange is on, but NexusApi lists no Keycloak client or no scopes for the instance, or an invalid
    /// scope or client identifier (ADR-0118).
    /// </summary>
    public const string TokenExchangeUnavailable = "token-exchange-unavailable";

    /// <summary>
    /// The identity provider refused or timed out the token exchange, its circuit breaker is open, or the exchange
    /// client is not configured (ADR-0118).
    /// </summary>
    public const string TokenExchangeFailed = "token-exchange-failed";
}