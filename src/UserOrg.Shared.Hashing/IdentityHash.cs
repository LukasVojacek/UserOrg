using System.Security.Cryptography;
using System.Text;

namespace UserOrg.Shared.Hashing;

/// <summary>SHA-256 digest helper shared by every generated <see cref="IIdentityHashed"/> implementation.</summary>
public static class IdentityHash
{
    /// <summary>Returns the lowercase hex SHA-256 digest of <paramref name="canonical"/>.</summary>
    public static string Sha256Hex(string canonical)
    {
        // SHA256.Create() (not the static HashData helper) — this type ships on netstandard2.0
        // so IdentityHashGenerator's own compilation (targeting an older Roslyn host) can load it.
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical));
#pragma warning disable CA1308 // hex digests are conventionally lowercase for a byte-stable identity hash; this is not security normalization.
        return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
#pragma warning restore CA1308
    }
}