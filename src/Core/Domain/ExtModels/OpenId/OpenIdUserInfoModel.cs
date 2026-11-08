using System.Text.Json.Serialization;

namespace UserOrganizationManagement.Domain.ExtModels.OpenId;

public sealed record OpenIdUserInfoModel()
{
    public const string httpContextItemsKey = "keycloakInfo";
    [JsonPropertyName("sub")]
    public string Sub { get; set; } = string.Empty;
    [JsonPropertyName("email_verified")]
    public bool EmailVerified { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("preferred_username")]
    public string PreferredUsername { get; set; } = string.Empty;
    [JsonPropertyName("given_name")]
    public string GivenName { get; set; } = string.Empty;
    [JsonPropertyName("family_name")]
    public string FamilyName { get; set; } = string.Empty;
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;
    [JsonPropertyName("provider_id")]
    public Guid? ProviderId { get; set; }
}