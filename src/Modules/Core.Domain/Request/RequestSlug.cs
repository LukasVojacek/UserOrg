namespace Core.Domain.Request;

/// <summary>
/// Generates a v2 request slug — the GUID-free public identifier a request is addressed by (ADR-0024).
/// Format <c>req-{e3}-{shortname}-{r6}</c>: <c>e3</c> is the first three <c>[a-z0-9]</c> of the requester's
/// email local part (lower-cased), <c>shortname</c> the project short name (lower-cased), and <c>r6</c> six
/// random base36 characters disambiguating repeat requests for the same requester+project. The random
/// suffix means the caller must regenerate-and-retry on the (rare) unique-index collision.
/// </summary>
public static class RequestSlug
{
    private const string Base36 = "abcdefghijklmnopqrstuvwxyz0123456789";

    public static string New(Email email, ProjectShortName projectShortName)
    {
        var local = email.Value.Split('@', 2)[0].ToLowerInvariant();
        var e3 = new string(local.Where(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9')).Take(3).ToArray());
        var shortName = projectShortName.Value.ToLowerInvariant();
        return $"req-{e3}-{shortName}-{Random6()}";
    }

    private static string Random6()
    {
        var chars = new char[6];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Base36[Random.Shared.Next(Base36.Length)];
        return new string(chars);
    }
}