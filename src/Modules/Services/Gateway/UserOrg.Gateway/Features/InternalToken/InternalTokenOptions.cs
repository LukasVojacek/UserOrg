namespace UserOrg.Gateway.Features.InternalToken;

/// <summary>
/// Configuration options for the Internal Token feature.
/// Manage JWT generation, key rotation, and token validation settings.
/// </summary>
internal sealed class InternalTokenOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "InternalToken";

    /// <summary>
    /// Gets or sets the JWT issuer.
    /// Default: "https://localhost:5001"
    /// </summary>
    public string Issuer { get; set; } = "https://gateway-aspire.dev.localhost:5800";

    /// <summary>
    /// Gets or sets the JWT audience.
    /// Default: "https://localhost:5002"
    /// </summary>
    public string Audience { get; set; } = "gateway-internal";

    /// <summary>
    /// Gets or sets the JWT token expiration in minutes.
    /// Default: 60
    /// </summary>
    public int ExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// Gets or sets the key rotation interval in hours.
    /// Default: 24
    /// </summary>
    public int KeyRotationIntervalHours { get; set; } = 24;

    /// <summary>
    /// Gets or sets the key grace period in hours.
    /// Keys are kept available for this duration after rotation.
    /// Default: 48
    /// </summary>
    public int KeyGracePeriodHours { get; set; } = 48;

    /// <summary>
    /// Gets or sets the JWKS cache expiration in seconds.
    /// Default: 30
    /// </summary>
    public int JwksCacheExpirationSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the key algorithm used for signing tokens.
    /// Options: RSA2048, RSA4096, ECDSA256, ECDSA512.
    /// Default: ECDSA256 (P-256 — faster signing than P-521 while still secure).
    /// </summary>
    public KeyAlgorithm KeyAlgorithm { get; set; } = KeyAlgorithm.ECDSA256;

    /// <summary>
    /// Gets or sets how many seconds a generated internal token may be cached per unique
    /// (authMethod, sub, role, providerId, projectRoles) combination before a new one is signed.
    /// Must be less than <see cref="ExpirationMinutes"/> × 60.
    /// Default: 30
    /// </summary>
    public int TokenCacheSeconds { get; set; } = 30;

    /// <summary>
    /// Validates the configuration options.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when configuration is invalid.</exception>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Issuer, nameof(Issuer));
        ArgumentException.ThrowIfNullOrWhiteSpace(Audience, nameof(Audience));

        if (ExpirationMinutes <= 0)
            throw new ArgumentException("ExpirationMinutes must be greater than 0", nameof(ExpirationMinutes));

        if (KeyRotationIntervalHours <= 0)
            throw new ArgumentException("KeyRotationIntervalHours must be greater than 0", nameof(KeyRotationIntervalHours));

        if (KeyGracePeriodHours < 0)
            throw new ArgumentException("KeyGracePeriodHours cannot be negative", nameof(KeyGracePeriodHours));

        if (JwksCacheExpirationSeconds <= 0)
            throw new ArgumentException("JwksCacheExpirationSeconds must be greater than 0", nameof(JwksCacheExpirationSeconds));

        if (!Enum.IsDefined(typeof(KeyAlgorithm), KeyAlgorithm))
            throw new ArgumentException($"Invalid KeyAlgorithm: {KeyAlgorithm}", nameof(KeyAlgorithm));

        if (TokenCacheSeconds <= 0 || TokenCacheSeconds >= ExpirationMinutes * 60)
            throw new ArgumentException($"TokenCacheSeconds must be between 1 and {ExpirationMinutes * 60 - 1}", nameof(TokenCacheSeconds));
    }
}