using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace UserOrganizationManagement.Domain.ExtModels.OpenId;

public sealed record OpenIdConfigurationModel()
{
    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }

    [JsonPropertyName("authorization_endpoint")]
    public string? AuthorizationEndpoint { get; set; }

    [JsonPropertyName("token_endpoint")]
    public string? TokenEndpoint { get; set; }

    [JsonPropertyName("userinfo_endpoint")]
    public string? UserinfoEndpoint { get; set; }

    [JsonPropertyName("jwks_uri")]
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "OpenID Connect specification requires string format")]
    public string? JwksUri { get; set; }

    [JsonPropertyName("end_session_endpoint")]
    public string? EndSessionEndpoint { get; set; }
}