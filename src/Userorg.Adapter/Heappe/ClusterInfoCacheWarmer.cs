using AsyncAwaitBestPractices;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.ValueObjects;

namespace Userorg.Adapter.Heappe;

internal sealed class ClusterInfoCacheWarmer(
    IServiceScopeFactory scopeFactory,
    HybridCache cache,
    ILogger<ClusterInfoCacheWarmer> logger) : IClusterInfoCacheWarmer
{
    private static readonly HybridCacheEntryOptions _markerOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(30),
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    };

    public void WarmInBackground(string subjectClaim, IReadOnlyCollection<ProjectShortName> projects, string bearerToken)
    {
        if (string.IsNullOrEmpty(subjectClaim) || string.IsNullOrEmpty(bearerToken) || projects.Count == 0)
        {
            return;
        }

        Task.Run(() => WarmAsync(subjectClaim, projects, bearerToken))
            .SafeFireAndForget(ex =>
                logger.LogWarning(ex, "Background cluster cache warm-up failed for user {SubjectClaim}", subjectClaim));
    }

    private async Task WarmAsync(string subjectClaim, IReadOnlyCollection<ProjectShortName> projects, string bearerToken)
    {
        // Best-effort dedup so the two UserInfo endpoints don't both warm within the same short window.
        var shouldWarm = false;
        await cache.GetOrCreateAsync(
            $"UserInfoHydration_{subjectClaim}",
            _ =>
            {
                shouldWarm = true;
                return ValueTask.FromResult(true);
            },
            _markerOptions);

        if (!shouldWarm)
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var projectService = scope.ServiceProvider.GetRequiredService<IProjectService>();

            foreach (var project in projects)
            {
                try
                {
                    await projectService.ProjectTemplateInfo(project, bearerToken, noCache: false, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Background cluster cache warm-up failed for project {ProjectShortName}", project);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Background cluster cache warm-up failed for user {SubjectClaim}", subjectClaim);
        }
    }
}