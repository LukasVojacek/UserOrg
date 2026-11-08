using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

using UserOrg.Gateway.Configuration;
using UserOrg.Shared;

namespace UserOrg.Gateway.Authentication;

/// <summary>
/// Develop-only authentication handler (ADR-0045). When enabled it skips the IdP entirely and treats the
/// bearer string itself as the caller's idpid (<c>sub</c>), producing a principal that impersonates that
/// user. The principal is stamped with <c>am = JwtBearerToken</c> so the downstream YARP transform
/// resolves the user's real role and project roles from the database; this handler never fabricates roles
/// and never contacts the IdP.
/// </summary>
/// <remarks>
/// Registered only when the environment is Development (see <c>ServicesExtensions.AddAuthenticationServices</c>)
/// and only acts when <see cref="DevAuthOverrideOptions.Enabled"/> is set, so it cannot run in any other
/// environment regardless of configuration.
/// </remarks>
internal sealed class DevOverrideAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private const string UserActorType = "user";

    private readonly IOptionsMonitor<DevAuthOverrideOptions> _overrideOptions;
    private readonly IAuditLogger<DevOverrideAuthenticationHandler> _auditLogger;

    public DevOverrideAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        IOptionsMonitor<DevAuthOverrideOptions> overrideOptions,
        IAuditLogger<DevOverrideAuthenticationHandler> auditLogger)
        : base(options, loggerFactory, encoder)
    {
        _overrideOptions = overrideOptions;
        _auditLogger = auditLogger;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_overrideOptions.CurrentValue.Enabled)
        {
            // Override off — defer to the real IdP path.
            return AuthenticateResult.NoResult();
        }

        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader) ||
            !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
            authHeader.Length <= "Bearer ".Length)
        {
            // Require a non-empty bearer string.
            return AuthenticateResult.NoResult();
        }

        // The bearer string IS the idpid — every request carries the user to impersonate.
        var sub = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(sub))
        {
            // No identity to impersonate — treat as unauthenticated (request will 401).
            return AuthenticateResult.NoResult();
        }

        // idpid is stored without the '@' suffix (see UserInitializationEndpointFilter.cs:36) — normalize
        // before synthesizing the address, or an '@'-suffixed sub produces a string Email.Create rejects.
        var normalizedSub = sub.Split('@')[0];

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, sub),
            // Forward the impersonated idpid as the username too, so the legacy user-init filter
            // (which looks up by preferred_username) resolves the same user that everything else
            // keys by sub/idpid — otherwise it tries to re-create an existing idpid and hits the
            // UX_User_IdentityProviderUserId unique index.
            new Claim(JwtRegisteredClaimNames.PreferredUsername, sub),
            // Synthesized so v2 JIT provisioning (ADR-0068 §7) is exercisable locally — the real IdP
            // token carries an email claim that this dev-only override otherwise has no source for.
            new Claim(JwtRegisteredClaimNames.Email, $"{normalizedSub}@dev.local"),
            new Claim(EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.JwtBearerToken))
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name, JwtRegisteredClaimNames.Sub, ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        Logger.LogWarning("DEV auth override active — impersonating sub {Sub}. Never enable outside Development.", sub);
        await _auditLogger.AuditBestEffortAsync(
            AuditEvent.Success(GatewayAuditActions.DevOverrideAuthenticated, normalizedSub, UserActorType),
            Logger);
        return AuthenticateResult.Success(ticket);
    }
}