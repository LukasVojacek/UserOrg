using System.Text;

namespace Core.DataAccess.ResourceRequests;

/// <summary>
/// Pure generator for a resource-request public identifier (ADR-0066): the lower-kebab slug
/// <c>{projectShortName}-{resourceName}</c>, with an ordinal suffix (<c>-2</c>, <c>-3</c>, …) appended on ANY
/// collision with an already-existing base slug. Two distinct providers can offer resources with the same
/// name, so a project may legitimately hold several requests that share the base slug while the 3-part
/// single-active invariant (project, provider, resource) stays satisfied — the suffix disambiguates the slug
/// PK for all of them, not just post-terminal repeats. The single-active invariant is enforced by the
/// filtered unique index, not here; this class only formats the identifier deterministically.
/// </summary>
public static class ResourceRequestSlug
{
    /// <summary>
    /// Builds the slug for a new request. <paramref name="existingBaseSlugCount"/> is the number of rows that
    /// already carry the same base slug — regardless of provider or state (terminal or not): 0 yields the bare
    /// base slug, 1 yields <c>base-2</c>, 2 yields <c>base-3</c>, and so on. This keeps the slug PK unique even
    /// when the 3-part invariant permits several coexisting rows for one <c>{project}-{resource}</c> pair
    /// (e.g. the same resource name offered by two providers).
    /// </summary>
    public static string Create(string projectShortName, string resourceName, int existingBaseSlugCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(existingBaseSlugCount);

        var baseSlug = BaseSlug(projectShortName, resourceName);
        return existingBaseSlugCount == 0 ? baseSlug : $"{baseSlug}-{existingBaseSlugCount + 1}";
    }

    /// <summary>
    /// Picks the lowest collision-free slug for a new request given the <paramref name="existingSlugs"/> already
    /// present for the same base (ADR-0066): the bare base <c>{project}-{resource}</c> when free, otherwise the
    /// first free ordinal form <c>{base}-2</c>, <c>{base}-3</c>, … This is collision-proof by construction — it
    /// tests actual slug availability rather than counting, so a distinct resource whose OWN base happens to look
    /// like an ordinal form (e.g. resource <c>gpu-2</c> → <c>{project}-gpu-2</c>) never pushes this resource's
    /// ordinal, and a genuine cross-provider/terminal collision is skipped over rather than reused. The DB filtered
    /// unique index + single-retry on the write path remain the concurrency backstop for a race after this read.
    /// </summary>
    public static string NextAvailable(string projectShortName, string resourceName, IReadOnlyCollection<string> existingSlugs)
    {
        ArgumentNullException.ThrowIfNull(existingSlugs);

        var baseSlug = BaseSlug(projectShortName, resourceName);
        if (!existingSlugs.Contains(baseSlug))
        {
            return baseSlug;
        }

        for (var ordinal = 2; ; ordinal++)
        {
            var candidate = $"{baseSlug}-{ordinal}";
            if (!existingSlugs.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Generates the bare, suffix-free base slug <c>{projectShortName}-{resourceName}</c> (lower-kebab).</summary>
    public static string BaseSlug(string projectShortName, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(projectShortName);
        ArgumentNullException.ThrowIfNull(resourceName);

        var baseSlug = $"{Normalize(projectShortName)}-{Normalize(resourceName)}";
        if (baseSlug is "-")
        {
            throw new ArgumentException("Slug components normalize to an empty identifier.");
        }

        return baseSlug;
    }

    /// <summary>
    /// Lower-kebab normalization: lowercases, collapses every run of non-alphanumeric characters into a single
    /// hyphen, and trims leading/trailing hyphens.
    /// </summary>
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length);
        var pendingHyphen = false;

        foreach (var ch in value)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                if (pendingHyphen && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(ch));
                pendingHyphen = false;
            }
            else
            {
                pendingHyphen = true;
            }
        }

        return builder.ToString();
    }
}