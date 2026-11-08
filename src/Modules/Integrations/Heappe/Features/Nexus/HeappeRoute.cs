using UserOrg.Shared;

namespace Heappe.Features.Nexus;

/// <summary>Where to send the calls for one HEAppE instance, and which Keycloak client Nexus holds for it.</summary>
/// <param name="Identifier">The instance.</param>
/// <param name="ProxyBaseAddress">The Nexus proxy base <c>{TokenExchange:Nexus:BaseUrl}/api/heappe/{identifier}/</c>.</param>
/// <param name="ClientId">The Keycloak client of the instance; <c>null</c> when Nexus lists none (routing does not need it).</param>
/// <param name="Scopes">The scopes of that client; empty without a client.</param>
internal sealed record HeappeRoute(HeappeIdentifier Identifier, Uri ProxyBaseAddress, string? ClientId, IReadOnlyList<string> Scopes)
{
    /// <summary>The route under <paramref name="baseUrl"/> when Nexus lists an enabled <paramref name="instance"/>; <c>null</c> otherwise. A missing <paramref name="client"/> does not block routing.</summary>
    public static HeappeRoute? RouteFor(Uri baseUrl, HeappeIdentifier identifier, NexusHeappeInstance? instance, NexusKeycloakClient? client)
    {
        if (instance is not { Enabled: true })
        {
            return null;
        }

        return new HeappeRoute(identifier, NexusEndpoints.ProxyBaseFor(baseUrl, identifier), client?.ClientId, client?.Scopes ?? []);
    }
}