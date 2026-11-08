using Core.Shared.OutputCache;

using Microsoft.AspNetCore.Http;

namespace Core.Features.Project.GetProjectTemplates;

/// <summary>
/// Output-cache strategy for the project templates endpoint. The response depends on the caller's HEAppE
/// view of the project, so unlike <see cref="ProjectCache"/> the cache also varies by user.
/// </summary>
internal static class ProjectTemplatesCache
{
    internal const string PolicyName = "ProjectTemplates";

    internal static async ValueTask<CacheDescriptor?> Describe(HttpContext ctx, CancellationToken ct)
    {
        if (await ProjectCache.Describe(ctx, ct) is not { } descriptor)
        {
            return null;
        }

        var caller = await Caller.BindAsync(ctx);

        return descriptor with
        {
            VaryByValues = new Dictionary<string, string>(descriptor.VaryByValues)
            {
                ["sub"] = caller?.IdpId.Value ?? ""
            }
        };
    }
}