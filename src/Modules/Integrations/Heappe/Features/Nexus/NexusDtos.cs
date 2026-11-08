using System.Text.Json.Serialization;

namespace Heappe.Features.Nexus;

/// <summary>Request body of <c>POST /api/v1/auth/token</c>.</summary>
internal sealed record NexusTokenRequest(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("client_secret")] string ClientSecret);

/// <summary>Response of <c>POST /api/v1/auth/token</c>.</summary>
internal sealed record NexusTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

/// <summary>Item of <c>GET /api/v1/heappe</c>; disabled instances are listed but not routed by the proxy.</summary>
internal sealed record NexusHeappeInstance(string Name, string Url, bool Enabled);

/// <summary>Item of <c>GET /api/v1/clients</c>: a Keycloak client the calling service account may use.</summary>
internal sealed record NexusKeycloakClient(string Name, string ClientId, IReadOnlyList<string> Scopes);

/// <summary>The registry as one cacheable value: what was listed and when.</summary>
internal sealed record NexusDirectorySnapshot(
    IReadOnlyList<NexusHeappeInstance> Instances,
    IReadOnlyList<NexusKeycloakClient> Clients,
    DateTimeOffset FetchedAt);