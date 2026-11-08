using Core.Shared.OutputCache;

using Microsoft.AspNetCore.Http;

namespace Core.Features.Statistics;

/// <summary>
/// Output-cache strategy for the statistics endpoint. Statistics are platform-wide (not caller-scoped)
/// and change slowly, so they are cached with a short TTL. The cache key varies only by the
/// <c>fromDate</c>/<c>toDate</c> window; there are no tags (the figures recompute on TTL expiry).
/// </summary>
internal static class StatisticsCache
{
    internal const string PolicyName = "Statistics";

    internal static ValueTask<CacheDescriptor?> Describe(HttpContext ctx, CancellationToken ct)
    {
        var fromDate = ctx.Request.Query["fromDate"].ToString();
        var toDate = ctx.Request.Query["toDate"].ToString();

        return ValueTask.FromResult<CacheDescriptor?>(new CacheDescriptor(
            TimeSpan.FromMinutes(5),
            new Dictionary<string, string> { ["fromDate"] = fromDate, ["toDate"] = toDate },
            []));
    }
}