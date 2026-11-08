namespace Core.Domain.OnboardingRequest;

/// <summary>
/// Stable error codes for <see cref="OnboardingRequestErrors"/>. Public so the Core module can map a
/// domain <c>Error</c> to an HTTP status without string-matching the description. Code strings are
/// reused from the generic request flow (ADR-0046).
/// </summary>
public static class OnboardingRequestErrorCodes
{
    public const string AlreadyActiveMember = "onboarding_request.already_active_member";
    public const string DuplicatePendingRequest = "onboarding_request.duplicate_pending_request";
    public const string InvalidState = "onboarding_request.invalid_state";
    public const string InvalidDirection = "onboarding_request.invalid_direction";
    public const string NotFound = "onboarding_request.not_found";
    public const string AlreadyDeleted = "onboarding_request.already_deleted";
    public const string NotDeleted = "onboarding_request.not_deleted";
}