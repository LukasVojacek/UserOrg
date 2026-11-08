using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace UserOrg.Gateway.Authentication;

/// <summary>
/// Authentication handler for public endpoints.
/// Returns an anonymous principal with public_access claim to mark the request as public.
/// Internal token generation is handled by YARP transform based on this claim.
/// </summary>
internal sealed class PublicWithInternalTokenHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public PublicWithInternalTokenHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder)
        : base(options, loggerFactory, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Create anonymous principal with public_access claim
        // This signals to downstream YARP transforms that an internal S2S token should be generated
        var claims = new[]
        {
            new Claim(Constants.PublicAccessClaimType, "true"),
            new Claim(ClaimTypes.Anonymous, "true")
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);

        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}