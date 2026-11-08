namespace Core.Shared.Auth;

/// <summary>
/// Claims-only authorization for provider/location write endpoints (ADR-0011, ADR-0105). Synchronous, no
/// database: the platform Admin role may write any provider; every other caller may write only the provider
/// its <c>providerName</c> claim names. Equality is the <see cref="ProviderName"/> value object's, which is
/// case-insensitive (ADR-0012), so no <see cref="StringComparison"/> is needed and a string comparison of the
/// inner values would be wrong. The authentication method does not enter the decision (ADR-0105 abandons
/// ADR-0094 D9): an interactive <c>ProviderAdmin</c> and a provider service key for the same provider carry
/// the same write authority, so <see cref="Caller.IsServiceKeyIdentity"/> is not read here. It still gates
/// reads through <c>ReadScope</c> (ADR-0094 D10).
/// </summary>
internal static class ProviderWriteAuthorization
{
    internal static bool Authorize(Caller caller, ProviderName target, WellKnownRoles roles)
    {
        if (caller.IsAdmin(roles))
            return true;

        return caller.ProviderName is { } owned && owned == target;
    }

    /// <summary>
    /// Authorizes a write against a not-yet-validated provider name. The name is canonicalized through
    /// <see cref="ProviderName.TryCreate"/> so ownership keeps ADR-0012 equality; a malformed name parses
    /// to the default value object, which the Admin branch still allows (they later see a 400 on validation)
    /// but which matches no owner's claim, so a non-Admin is denied (403) without the parse failure leaking
    /// as a 400.
    /// </summary>
    internal static bool Authorize(Caller caller, string rawName, WellKnownRoles roles)
    {
        ProviderName.TryCreate(rawName, out var target, out _);
        return Authorize(caller, target, roles);
    }
}