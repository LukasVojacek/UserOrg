namespace Core.Features.ResourceRequest.Expiry;

/// <summary>
/// Expiry settings for parked resource-assignment requests (DEC-7). Mirrors
/// <see cref="Core.Features.Request.Notifications.ResourceRequestActionLinkOptions"/>: <see cref="DurationInDays"/>
/// is how long a request may sit non-terminal in either gate (<c>PendingPiApproval</c> or
/// <c>AwaitingResourceCreation</c>), measured from <c>ProjectResourceRequest.UpdatedAt</c>, before the sweeper
/// expires it.
/// </summary>
/// <remarks>
/// Current behaviour, observed rather than specified — no decision record asks for it: resending the PI approval
/// e-mail (<c>ResendApprovalEmailEndpoint</c>) restarts this clock, because it writes the new token through
/// <c>IResourceRequestProjectionStore.UpdateBySlugAsync</c>, which bumps <c>UpdatedAt</c> on every mutation — not
/// because a resend is meant to grant extra time. A caller who keeps resending keeps a request parked
/// indefinitely. Treat it as a consequence to revisit, not as a guarantee to depend on.
/// </remarks>
internal sealed class ResourceRequestExpiryOptions
{
    public const string ConfigurationPath = "ResourceRequestExpiry";

    public int DurationInDays { get; set; } = 30;
}