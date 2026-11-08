using Core.Shared.OutputCache;

using Microsoft.AspNetCore.Http;

namespace Core.Features.Country;

/// <summary>
/// Output-cache strategy for the countries endpoint. Country data is seeded reference data with no v2
/// write path, so it is cached with a long TTL and never invalidated. The cache key varies only by the
/// <c>countryCode</c> filter; there is no caller scope (reference data is the same for everyone).
/// </summary>
internal static class CountryCache
{
    internal const string PolicyName = "Country";

    internal static ValueTask<CacheDescriptor?> Describe(HttpContext ctx, CancellationToken ct)
    {
        var countryCode = ctx.Request.Query["countryCode"].ToString();

        return ValueTask.FromResult<CacheDescriptor?>(new CacheDescriptor(
            TimeSpan.FromHours(12),
            new Dictionary<string, string> { ["countryCode"] = countryCode },
            []));
    }
}