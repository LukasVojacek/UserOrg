namespace Core.Domain.Provider;

/// <summary>
/// Generates a v2 allocation slug — the GUID-free public identifier a resource allocation
/// (<c>AssignedLocation</c>) is addressed by (ADR-0024). Format <c>asg-{resourcename}-{r6}</c>:
/// <c>resourcename</c> is the resource name lower-cased and reduced to <c>[a-z0-9-]</c> (capped),
/// and <c>r6</c> six random base36 characters that disambiguate repeat allocations of the same
/// resource and supply the global uniqueness — so the caller regenerate-and-retries on the
/// (rare) unique-index collision.
/// </summary>
public static class AllocationSlug
{
    private const string Base36 = "abcdefghijklmnopqrstuvwxyz0123456789";

    public static string New(ResourceName resourceName)
    {
        var name = new string(resourceName.Value.ToLowerInvariant()
            .Where(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
            .Take(40).ToArray()).Trim('-');
        return name.Length > 0 ? $"asg-{name}-{Random6()}" : $"asg-{Random6()}";
    }

    private static string Random6()
    {
        var chars = new char[6];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Base36[Random.Shared.Next(Base36.Length)];
        return new string(chars);
    }
}