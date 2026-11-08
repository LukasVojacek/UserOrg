using Microsoft.AspNetCore.Mvc;

namespace Core.Shared.Paging;

/// <summary>
/// Offset paging parameters bound from the query string on growable v2 list endpoints
/// (<c>?page=&amp;limit=</c>). Out-of-range or missing values are normalised: page defaults to 1, limit to the
/// configured default, and any limit above the configured maximum is capped so a client cannot pull an
/// unbounded result set. Bound via <c>[AsParameters]</c>; the limit bounds are resolved against the
/// injected <see cref="PaginationOptions"/> in the use-case (see <see cref="ResolveLimit"/>).
/// </summary>
internal sealed record PageRequest(
    [property: FromQuery] int? Page,
    [property: FromQuery] int? Limit)
{
    /// <summary>Default page size when <c>?limit=</c> is absent/invalid; the configurable <see cref="PaginationOptions"/> default.</summary>
    public const int DefaultLimit = 100;

    /// <summary>Hard cap on page size; the configurable <see cref="PaginationOptions"/> maximum.</summary>
    public const int MaxLimit = 300;

    /// <summary>The requested page, clamped to a minimum of 1.</summary>
    public int EffectivePage => Page is { } p && p > 0 ? p : 1;

    /// <summary>
    /// The requested page size, defaulted when absent/invalid and capped, using the configured
    /// <paramref name="options"/> bounds rather than the compile-time fallbacks.
    /// </summary>
    public int ResolveLimit(PaginationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Limit is { } l && l > 0 ? Math.Min(l, options.MaxLimit) : options.DefaultLimit;
    }
}