using Heappe.Features.Nexus;

using UserOrg.Shared.Extensions;

namespace Heappe.Features.Connection;

/// <summary>
/// The client that exchanges the caller's token for one per HEAppE instance (ADR-0118): the client pair
/// <c>TokenExchange:ClientId</c> and <c>TokenExchange:ClientSecret</c> against the token endpoint of
/// <c>TokenExchange:Authority</c>, and the NexusApi settings under <c>TokenExchange:Nexus</c> (ADR-0119).
/// </summary>
internal sealed class TokenExchangeOptions
{ 
    /// <summary>
    /// The TokenDelegation client the exchange runs through; its HttpClient is <c>tokendelegation:heappe-token-exchange</c>.
    /// </summary>
    public const string DelegationClientName = "heappe-token-exchange";

    /// <summary>
    /// The configuration section the options are bound from.
    /// </summary>
    public const string SectionName = "TokenExchange";

    /// <summary>
    /// The path of the token endpoint relative to the realm <see cref="Authority"/>.
    /// </summary>
    public Uri TokenEndpointPath { get; set; } = new("/protocol/openid-connect/token", UriKind.Relative);

    /// <summary>
    /// The confidential client that performs the exchange.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// The secret of <see cref="ClientId"/>.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// The realm the exchange runs against, for example <c>https://keycloak/realms/lexis/</c>.
    /// </summary>
    public Uri? Authority
    {
        get;
        set => field = value?.ToNormalizeUrl();
    } = new Uri("https://nexus.invalid/", UriKind.Absolute);

    /// <summary>
    /// The NexusApi settings, bound from <c>TokenExchange:Nexus</c>; separate from the exchange client.
    /// </summary>
    public NexusOptions Nexus { get; set; } = new();

    /// <summary>
    /// The resilience settings of the delegation client, bound from <c>TokenExchange:Resilience</c>.
    /// </summary>
    public TokenExchangeResilienceOptions Resilience { get; set; } = new();

    /// <summary>
    /// The realm token endpoint <c>{Authority}{TokenEndpointPath}</c>.
    /// </summary>
    public Uri? TokenAuthorityEndpoint =>
        Authority is { IsAbsoluteUri: true }
            ? new Uri(Authority, TokenEndpointPath.OriginalString.TrimStart('/'))
            : default;
}