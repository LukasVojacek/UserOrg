using System.Security.Cryptography;
using System.Text;

namespace Core.Features.ResourceRequest.Flow;

/// <summary>
/// Mints and verifies the single-use PI action-link token (ADR-0064). The plaintext token is emailed to the
/// PI and never stored; only its SHA-256 hash lives on the projection row (hash-at-rest). Shared with B06's
/// public token endpoints, which recompute <see cref="Hash"/> to look the request up and <see cref="Verify"/>
/// to authorise the resume.
/// </summary>
internal static class ResourceRequestToken
{
    private const int TokenByteLength = 32;

    /// <summary>Mints a cryptographically random, URL-safe (base64url, unpadded) token.</summary>
    internal static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>Lower-hex SHA-256 of the token — the value persisted on the projection row.</summary>
    internal static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexStringLower(digest);
    }

    /// <summary>
    /// Constant-time check that <paramref name="token"/> hashes to the stored <paramref name="expectedHash"/> and
    /// that the token is still live (ADR-0064 single-use): an absent hash (already invalidated / used) or an
    /// absent-or-past <paramref name="expiresAt"/> (relative to <paramref name="asOf"/>) fails. B06 calls exactly
    /// this helper to authorise a public action-link resume.
    /// </summary>
    internal static bool Verify(string? token, string? expectedHash, DateTimeOffset? expiresAt, DateTimeOffset asOf)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(expectedHash) || expiresAt is null || expiresAt <= asOf)
        {
            return false;
        }

        var actual = Hash(token);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual),
            Encoding.UTF8.GetBytes(expectedHash));
    }
}