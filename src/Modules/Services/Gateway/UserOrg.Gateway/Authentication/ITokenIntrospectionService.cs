namespace UserOrg.Gateway.Authentication;

/// <summary>
/// Represents the result of a token introspection request.
/// </summary>
/// <param name="Active">Indicates whether the token is currently active.</param>
/// <param name="Exp">Token expiration time (Unix timestamp).</param>
/// <param name="Claims">Additional claims returned by the introspection endpoint.</param>
internal sealed record IntrospectionResult(
    bool Active,
    DateTimeOffset? Exp,
    IReadOnlyDictionary<string, string[]> Claims);

/// <summary>
/// Service for performing OAuth 2.0 token introspection (RFC 7662).
/// </summary>
internal interface ITokenIntrospectionService
{
    /// <summary>
    /// Introspects the given token by calling the configured introspection endpoint.
    /// Results are cached based on token hash and expiration time.
    /// </summary>
    /// <param name="token">The access token to introspect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Introspection result containing active status and claims.</returns>
    Task<IntrospectionResult> IntrospectAsync(string token, CancellationToken cancellationToken = default);
}