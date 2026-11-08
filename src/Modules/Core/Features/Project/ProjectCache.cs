using Core.Shared.OutputCache;

using Microsoft.AspNetCore.Http;

namespace Core.Features.Project;

/// <summary>
/// Output-cache strategy for project endpoints. Tagged by <c>project:{shortName}</c> so any
/// endpoint depending on a project can be evicted together via <see cref="IOutputCacheInvalidator"/>.
/// </summary>
internal static class ProjectCache
{
    internal const string PolicyName = "Project";

    internal static async ValueTask<CacheDescriptor?> Describe(HttpContext ctx, CancellationToken ct)
    {
        if (!ctx.Request.RouteValues.TryGetValue("projectShortName", out var shortNameValue) || shortNameValue is null)
        {
            return null;
        }

        var shortName = shortNameValue.ToString()!;

        // Vary the shared project cache by whether the caller is in scope, so an entry populated by an
        // in-scope caller (a stored 200) is never served to an out-of-scope caller — who, on their own
        // "inScope=0" key, always misses and runs the handler, which returns a never-stored 404.
        var caller = await Caller.BindAsync(ctx);
        var inScope = caller is not null
            && ProjectShortName.TryParse(shortName, null, out var sn)
            && caller.Scope.Contains(sn);

        return new CacheDescriptor(
            TimeSpan.FromMinutes(5),
            new Dictionary<string, string> { ["inScope"] = inScope ? "1" : "0" },
            [CacheTags.Project(shortName)]);
    }
}