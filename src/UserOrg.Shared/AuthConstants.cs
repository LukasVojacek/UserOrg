namespace UserOrg.Shared;

public static class AuthConstants
{
    /// <summary>
    /// Represents the claim type used to identify the service type in authentication contexts. e.g. ddi, service, provider
    /// </summary>
    public const string ServiceTypeClaimType = "service_type";
    /// <summary>
    /// Represents the claim type used to specify the identity type in claims-based authentication.
    /// </summary>
    /// <remarks>Use this constant to identify or create claims that indicate the type of identity associated
    /// with a claims principal. This is essential for interpreting or enforcing identity-related policies within
    /// authentication and authorization workflows.</remarks>
    public const string IdentityTypeClaimType = "identity_type";
    public const string CustomDataTypeClaimType = "custom_data";
    /// <summary>
    /// Represents the claim type used to identify the provider ID in authentication and authorization scenarios.
    /// </summary>
    public const string ProviderIdClaimType = "provider_id";

    /// <summary>
    /// Additive claim carrying the provider's natural identifier (<c>ProviderName</c>). Minted by the
    /// Gateway either for a provider service-key identity, by resolving <see cref="ProviderIdClaimType"/>
    /// (a Guid) to the provider name (ADR-0011), or for an interactive ProviderAdmin with a live
    /// provider assignment, resolved from the DB (ADR-0094). Holding this claim no longer implies a
    /// service-key identity — <see cref="EAuthorizationMethod"/> (the <c>am</c> claim) is what
    /// distinguishes the two. Core uses it to scope provider/location/resource reads for both identity
    /// kinds, and every write behind <c>ProviderWriteAuthorization</c> — provider, location, resource and
    /// both assignment surfaces (resource-to-location, resource-to-project) — for the service-key one
    /// only (ADR-0094).
    /// </summary>
    public const string ProviderNameClaimType = "provider_name";

    /// <summary>
    /// Represents the claim type carrying the <c>jti</c> of the token that authenticated the caller,
    /// relayed verbatim (unprefixed) onto the internal token so the chain stays joinable across
    /// components. Consumers resolve the chain ID as <c>original_jti ?? jti</c> (prefering the
    /// external token's ID, falling back to the internal token's own jti if no external token was present).
    /// </summary>
    public const string OriginalJtiClaimType = "original_jti";
}