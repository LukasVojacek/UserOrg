using System.Collections.Frozen;
using System.Net;
using System.Text.Json;

using Heappe.Contracts;

namespace Heappe.Features.Nexus;

/// <summary>
/// Turns a failed answer into a URL-free <see cref="HeappeFailureCause"/> (ADR 0074). NexusApi stamps its own failures
/// with an RFC 9457 <c>type</c>; a HEAppE answer relayed through the proxy carries none and keeps the status mapping.
/// </summary>
internal static class NexusProblemMapper
{
    private const string ProblemJsonMediaType = "application/problem+json";

    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly FrozenDictionary<string, string> _causeByProblemType = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["urn:config-registry:heappe-instance-not-found"] = HeappeFailureCause.HeappeInstanceNotFound,
        ["urn:config-registry:heappe-upstream-unavailable"] = HeappeFailureCause.HeappeUpstreamUnavailable,
        ["urn:config-registry:registry-unavailable"] = HeappeFailureCause.NexusUnavailable,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// A Nexus problem type names its cause; otherwise 401/403 are an auth problem the caller must fix and any other
    /// non-success status is a generic upstream error.
    /// </summary>
    public static string CauseFor(HttpStatusCode statusCode, string? problemType) =>
        problemType is not null && _causeByProblemType.TryGetValue(problemType, out var cause)
            ? cause
            : statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? HeappeFailureCause.Unauthorized
                : HeappeFailureCause.UpstreamError;

    /// <summary>The <c>type</c> of a <c>application/problem+json</c> body, or <c>null</c> when the body is no typed problem.</summary>
    public static string? ProblemTypeOf(string? mediaType, string body)
    {
        if (!string.Equals(mediaType, ProblemJsonMediaType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Problem>(body, _jsonOptions)?.Type;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Problem(string? Type);
}