namespace Core.Domain.Request;

/// <summary>
/// Stable error codes for <see cref="RequestErrors"/>. Public so the Core module can map a
/// domain <c>Error</c> to an HTTP status without string-matching the description.
/// </summary>
public static class RequestErrorCodes
{
    public const string InvalidStateTransition = "request.invalid_state_transition";
    public const string AlreadyDeleted = "request.already_deleted";
    public const string NotDeleted = "request.not_deleted";
    public const string AlreadyActiveMember = "request.already_active_member";
    public const string DuplicatePendingRequest = "request.duplicate_pending_request";
    public const string UnsupportedType = "request.unsupported_type";

    // Validation (v2 contract — surfaced via Problems.Validation, ADR-0020)
    public const string InvalidType = "request.invalid_type";
    public const string InvalidState = "request.invalid_state";
    public const string NotFound = "request.not_found";
    public const string NotPending = "request.not_pending";
    public const string Expired = "request.expired";
    public const string InvalidActionLink = "request.invalid_action_link";
}