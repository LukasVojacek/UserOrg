namespace Core.Features.Request.Notifications;

/// <summary>
/// Action-link settings for the resource-assignment request flow (ADR-0064). Mirrors
/// <see cref="ActionLinkOptions"/>: <see cref="DurationInHours"/> is the emailed link's validity window and
/// <see cref="BaseUrl"/> is the host of the UI page that resolves a single-use token. The PI approval email
/// carries <c>{BaseUrl}/{token}</c>; the token itself is minted and stored (hashed) by the flow, not here.
/// </summary>
internal sealed class ResourceRequestActionLinkOptions
{
    public const string ConfigurationPath = "ResourceRequestActionLink";

    public string? BaseUrl { get; set; }

    public int DurationInHours { get; set; } = 120;

    public string BuildActionLink(string token) => $"{BaseUrl?.TrimEnd('/')}/{Uri.EscapeDataString(token)}";
}