using System.Collections.Frozen;
using System.Security.Claims;

namespace UserOrg.Shared.Logging;

/// <summary>The one claim-dump format both hosts use: allow-listed claim types print their value, every other type prints [REDACTED].</summary>
public static class ClaimLogFormatter
{
    public static IReadOnlySet<string> ReadableClaimTypes { get; } = new[]
    {
        "sub", "jti", "original_jti", "role", ClaimTypes.Role, "projectRoles", "am", "provider_id", "scope", "aud", "iss", "exp",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static string Format(IEnumerable<Claim> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        return string.Join(", ", claims.Select(static claim =>
            $"{claim.Type}={(ReadableClaimTypes.Contains(claim.Type) ? claim.Value : "[REDACTED]")}"));
    }
}