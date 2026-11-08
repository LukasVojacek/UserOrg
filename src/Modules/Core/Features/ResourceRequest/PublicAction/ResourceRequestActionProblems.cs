using Microsoft.AspNetCore.Http;

namespace Core.Features.ResourceRequest.PublicAction;

/// <summary>
/// The single, byte-identical 404 emitted by both public action-link endpoints for every non-resolvable token —
/// unknown, expired, already-used (hash cleared), or pointing at a request the workflow no longer awaits. ADR-0064
/// anti-leak: the cause must never be distinguishable, so all four collapse to one code and one detail string.
/// </summary>
internal static class ResourceRequestActionProblems
{
    internal const string ActionNotFoundCode = "resource_request.action_not_found";
    internal const string InvalidDecisionCode = "resource_request.invalid_decision";
    internal const string ReasonTooLongCode = "resource_request.reason_too_long";

    /// <summary>Upper bound on the anonymous reject reason — it is persisted and emailed, so it must be bounded.</summary>
    internal const int MaxReasonLength = 2000;

    private const string ActionNotFoundDetail = "This action link is invalid, expired, or already used.";

    /// <summary>The uniform 404 for any unresolvable/settled action token (identical payload across all causes).</summary>
    internal static IResult ActionNotFound() =>
        Problems.NotFound(ActionNotFoundCode, ActionNotFoundDetail);
}