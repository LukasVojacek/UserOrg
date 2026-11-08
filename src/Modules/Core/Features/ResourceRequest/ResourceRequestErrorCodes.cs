namespace Core.Features.ResourceRequest;

/// <summary>
/// Stable machine-readable <c>code</c> values (ADR-0020) surfaced by the resource-request use-case endpoints.
/// The scope-anti-leak <see cref="NotFound"/> is shared by every by-slug write so a genuine miss, an
/// out-of-scope caller, and a wrong-project slug are indistinguishable (ADR-0018).
/// </summary>
internal static class ResourceRequestErrorCodes
{
    /// <summary>Request unknown, outside the caller's scope, or on a different project — deliberately identical.</summary>
    internal const string NotFound = "resource_request.not_found";

    /// <summary>Cancel refused: the request is already in a terminal state.</summary>
    internal const string NotCancellable = "resource_request.not_cancellable";

    /// <summary>Cancel raced the engine parking at its gate; retry shortly.</summary>
    internal const string NotCancellableYet = "resource_request.not_cancellable_yet";

    /// <summary>Resend refused: the request is not awaiting PI approval.</summary>
    internal const string NotResendable = "resource_request.not_resendable";

    /// <summary>Creation refused: the named provider has no support e-mail (required to notify for resources that do not yet exist; DEC-5).</summary>
    internal const string ProviderNoSupportEmail = "resource_request.provider_no_support_email";
}