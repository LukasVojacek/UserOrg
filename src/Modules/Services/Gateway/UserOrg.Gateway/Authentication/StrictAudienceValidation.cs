using Microsoft.IdentityModel.Tokens;

namespace UserOrg.Gateway.Authentication;

/// <summary>
/// The strict token audience rule of ADR 0121: a user token is accepted only when it carries at least one
/// audience and every audience it carries is configured, compared exactly and ordinally.
/// </summary>
internal static class StrictAudienceValidation
{
    private static readonly AudienceValidator Strict = Validate;

    /// <summary>The strict audience validator when <paramref name="strict"/> is set; null keeps the IdentityModel default.</summary>
    internal static AudienceValidator? For(bool strict) => strict ? Strict : null;

    /// <summary>True when <paramref name="audiences"/> is non-empty and each one is in <paramref name="configured"/>.</summary>
    internal static bool AllAudiencesConfigured(IEnumerable<string> audiences, IEnumerable<string>? configured)
    {
        var tokenAudiences = audiences.ToArray();
        var configuredAudiences = new HashSet<string>(configured ?? [], StringComparer.Ordinal);
        return tokenAudiences.Length > 0 && tokenAudiences.All(configuredAudiences.Contains);
    }

    private static bool Validate(IEnumerable<string> audiences, SecurityToken securityToken, TokenValidationParameters validationParameters) =>
        AllAudiencesConfigured(audiences, validationParameters.ValidAudiences);
}