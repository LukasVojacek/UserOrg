namespace UserOrg.Gateway.Features.InternalToken;

/// <summary>
/// Specifies the cryptographic algorithm used for signing internal JWT tokens.
/// </summary>
internal enum KeyAlgorithm
{
    /// <summary>
    /// RSA with 2048-bit key length. Standard algorithm, good compatibility.
    /// </summary>
    RSA2048,

    /// <summary>
    /// RSA with 4096-bit key length. Higher security, slower key generation and signing.
    /// </summary>
    RSA4096,

    /// <summary>
    /// ECDSA (Elliptic Curve) with P-256 curve, SHA-256 hash (ES256). Modern, faster, smaller keys.
    /// </summary>
    ECDSA256,

    /// <summary>
    /// ECDSA (Elliptic Curve) with P-521 curve, SHA-512 hash (ES512). Highest security in this set.
    /// </summary>
    ECDSA512
}