using Heappe.Features.Connection;

using UserOrg.Shared.Extensions;

namespace Heappe.Features.Nexus;

/// <summary>
/// Settings of the NexusApi client, bound to <c>TokenExchange:Nexus</c> and validated by
/// <see cref="NexusOptionsValidator"/>.
/// </summary>
internal sealed class NexusOptions
{
    /// <summary>
    /// Configuration section name: <c>Nexus</c>.
    /// </summary>
    public const string SectionName = $"{TokenExchangeOptions.SectionName}:Nexus";

    /// <summary>
    /// The NexusApi address, always required: an absolute http or https URI at the host root, without path, userinfo,
    /// query or fragment.
    /// Every HEAppE call goes to its proxy <c>{BaseUrl}/api/heappe/{identifier}/</c>.
    /// </summary>
    public Uri? BaseUrl
    {
        get;
        set => field = value is { IsAbsoluteUri: true } ? value.ToNormalizeUrl() : default;
    } = new Uri("https://nexus.invalid", UriKind.Absolute);

    /// <summary>
    /// How long the registry snapshot (HEAppE instances and Keycloak clients) is reused before it is fetched again.
    /// </summary>
    public TimeSpan CacheMapTtl { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The <c>client_id</c> of UserOrg's NexusApi service account; required only with the token exchange flag on.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// The secret of UserOrg's NexusApi service account; required only with the token exchange flag on.
    /// </summary>
    public string? ClientSecret { get; set; }
}