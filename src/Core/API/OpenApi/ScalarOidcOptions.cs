using System.Diagnostics.CodeAnalysis;

namespace UserOrganizationManagement.API.OpenApi;

internal sealed class ScalarOidcOptions
{
    public const string SectionName = "Scalar:Oidc";

    public const string SchemeName = "Oidc";

    public Uri? AuthorizationUrl { get; set; }

    public Uri? TokenUrl { get; set; }

    public string ClientId { get; set; } = "";

    public IReadOnlyList<string> Scopes { get; } = ["openid", "profile"];

    [MemberNotNullWhen(true, nameof(AuthorizationUrl), nameof(TokenUrl))]
    public bool IsConfigured => AuthorizationUrl is not null && TokenUrl is not null && !string.IsNullOrWhiteSpace(ClientId);
}