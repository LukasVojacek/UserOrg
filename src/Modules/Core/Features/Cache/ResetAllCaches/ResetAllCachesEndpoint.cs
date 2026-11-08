using Core.Shared.OutputCache;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Core.Features.Cache.ResetAllCaches;

/// <summary>
/// Flushes the entire v2 output cache (Admin only). Evicts the shared <see cref="CacheTags.All"/> tag, which
/// every cacheable response carries, so a single call clears everything. v2 counterpart of the legacy
/// <c>DELETE api/Administration/Cache</c>, scoped to the output cache (the legacy HybridCache concerns —
/// RBAC snapshot, JWKS — are not Core's output cache).
/// </summary>
internal static class ResetAllCachesEndpoint
{
    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        Caller caller,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] IOutputCacheInvalidator invalidator,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        CancellationToken ct)
    {
        // Flushing every user's cached data is platform maintenance, not a project member's right — Admin only.
        if (!caller.IsAdmin(roleOptions.Value))
        {
            await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeDenied(AuditActions.CacheOutputFlushed, caller) with
            {
                Reason = AuditActions.DenialReasons.CacheResetForbidden,
            }, logger);
            return Problems.Forbidden("cache.reset_forbidden", "Only an administrator may reset all caches.");
        }

        await invalidator.EvictAsync(CacheTags.All, ct);
        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(AuditActions.CacheOutputFlushed, caller), logger);
        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapResetAllCachesEndpoint()
        {
            app.MapDelete("Caches", HandleAsync)
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.Admin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("ResetAllCachesV2")
            .WithSummary("Reset all caches")
            .WithDescription("Flushes the entire v2 output cache (Admin only). Returns 204; 403 for non-admins.")
            .WithTags("Cache")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }
}