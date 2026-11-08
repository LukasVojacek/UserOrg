using Microsoft.AspNetCore.Http;

namespace UserOrg.Shared;

/// <summary>
/// The original end-user bearer token forwarded by the Gateway in the
/// <see cref="HeaderName"/> (<c>X-Original-Authorization</c>) header. Bound as a
/// minimal-API endpoint parameter via <see cref="BindAsync(HttpContext)"/>.
/// </summary>
/// <remarks>
/// Carries a live credential: it is a request-scoped bound value and must never be
/// logged, persisted, or placed in any DTO. A <c>null</c> bound value means the header
/// was missing or malformed and the endpoint maps it to <c>401 Unauthorized</c>.
/// </remarks>
public sealed record OriginalBearerToken(string Value)
{
    /// <summary>Name of the header the Gateway forwards the original end-user token in.</summary>
    public const string HeaderName = "X-Original-Authorization";

    private const string BearerPrefix = "Bearer ";

    /// <summary>
    /// Reads <see cref="HeaderName"/>, requires a case-insensitive <c>Bearer </c> prefix,
    /// strips and trims it, and returns the raw token. Returns <c>null</c> when the header is
    /// absent, uses a different scheme, or carries an empty/whitespace token.
    /// </summary>
    public static ValueTask<OriginalBearerToken?> BindAsync(HttpContext context)
    {
        var raw = context.Request.Headers[HeaderName].ToString();
        if (!raw.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return ValueTask.FromResult<OriginalBearerToken?>(null);

        var token = raw[BearerPrefix.Length..].Trim();
        return ValueTask.FromResult(string.IsNullOrEmpty(token) ? null : new OriginalBearerToken(token));
    }

    /// <summary>
    /// Redacts <see cref="Value"/> so the live credential can never leak through the
    /// compiler-synthesized record <c>ToString()</c> (e.g. in logs or exception messages).
    /// </summary>
    public override string ToString() => "OriginalBearerToken { Value = *** }";
}