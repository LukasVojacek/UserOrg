using System.Diagnostics;

namespace UserOrg.Gateway.Startup;

/// <summary>
/// OpenTelemetry ActivitySource for authentication events.
/// </summary>
internal static class AuthTelemetry
{
    public const string ActivitySourceName = "UserOrg.Gateway.Auth";
    public const string Version = "1.0.0";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    public const string ActivityMessageReceived = "auth.message_received";
    public const string ActivityTokenValidated = "auth.token_validated";
    public const string ActivityAuthenticationFailed = "auth.authentication_failed";
    public const string ActivityChallenge = "auth.challenge";
    public const string ActivityForbidden = "auth.forbidden";

    public const string TagAuthority = "auth.authority";
    public const string TagScheme = "auth.scheme";
    public const string TagHasAuthorization = "auth.has_authorization";
    public const string TagUserId = "auth.user_id";
    /// <summary>
    /// Activity tag for the JWT ID (jti) claim: the incoming token's <c>original_jti</c> if present,
    /// otherwise the token's own <c>jti</c>. Enables tracing the authentication chain across services.
    /// </summary>
    public const string TagJti = "auth.jti";
    public const string TagError = "auth.error";
    public const string TagException = "auth.exception";
}