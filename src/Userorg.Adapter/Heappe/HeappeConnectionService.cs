using System.Text.Json;

using Heappe.Contracts;
using Heappe.Contracts.CommandTemplate;
using Heappe.Contracts.Connection;
using Heappe.Contracts.HEappeApiDto.Responses;
using Heappe.Features.VersionResolver;
using Heappe.Helpers.Connection;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

using Userorg.Adapter.Heappe.Userorg;

using UserOrg.Shared;

using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.ValueObjects;

using ExtProject = UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;

// This legacy adapter implements the Application-layer IHeappeConnectionService; disambiguate from main's new
// Heappe.Contracts.IHeappeConnectionService now also in scope after the merge (ADR 0033 port-later list).
using IHeappeConnectionService = UserOrganizationManagement.Application.UserOrg.IServices.IHeappeConnectionService;

namespace Userorg.Adapter.Heappe;

internal sealed partial class HeappeConnectionService(
    IHeappeService heappeService,
    IHeappeConnectionFactory connectionFactory,
    IHeappeVersionResolver heappeVersionResolver,
    IHeappeLivenessProbe heappeLivenessProbe,
    HybridCache cache,
    ILogger<HeappeConnectionService> logger) : IHeappeConnectionService
{
    private static readonly Version _useTokenMinimalVersion = new(6, 1, 1);

    private static readonly HybridCacheEntryOptions _clustersCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5)
    };

    // noCache: bypass read AND write on both cache tiers so the HEAppE cluster list is fetched live and not
    // stored. The HeappeClusters entry is shared across projects/resources on the same instance, so skipping
    // the write also guarantees a noCache request never refreshes or evicts another project's cached snapshot.
    private static readonly HybridCacheEntryOptions _clustersBypassCacheOptions = new()
    {
        Flags = HybridCacheEntryFlags.DisableLocalCacheRead
              | HybridCacheEntryFlags.DisableLocalCacheWrite
              | HybridCacheEntryFlags.DisableDistributedCacheRead
              | HybridCacheEntryFlags.DisableDistributedCacheWrite
    };

    private async Task<Result<BaseHeappeConnectionInfo, Error>> GetHeappeConnectionInfoAsync(string projectShortname, HeappeIdentifier identifier, string bearerToken, CancellationToken ct = default)
    {
        var heappeVersion = await heappeVersionResolver.GetVersion(projectShortname, identifier, ct);
        return await connectionFactory.ForUserAsync(identifier, bearerToken, ShouldUseToken(heappeVersion), ct);
    }

    private async Task<Result<BaseHeappeConnectionInfo, Error>> GetHeappeConnectionInfoAsync(HeappeIdentifier identifier, string bearerToken, CancellationToken ct = default)
    {
        var heappeVersion = await heappeVersionResolver.GetVersion(identifier, ct);
        return await connectionFactory.ForUserAsync(identifier, bearerToken, ShouldUseToken(heappeVersion), ct);
    }

    private static bool ShouldUseToken(Version heappeVersion) => heappeVersion >= _useTokenMinimalVersion;

    // The active command templates are a project-level precondition fetched once by Core before the HEAppE
    // fan-out (ADR 0074); this adapter just relays the read to the underlying Heappe service.
    public Task<Result<HashSet<CommandTemplatePartitionResponse>, Error>> GetActiveCommandTemplatesAsync(ProjectId projectId, CancellationToken ct)
        => heappeService.GetActiveCommandTemplatesAsync(projectId.Value, ct);

    public async ValueTask<Result<IList<ExtProject.ClusterInfoModel>, Error>> GetListAvailableClusters(
        ProjectShortName projectShortName,
        ProjectId projectId,
        string resourceName,
        HeappeIdentifier identifier,
        string bearerToken,
        IReadOnlyCollection<string> locationNames,
        IReadOnlyCollection<CommandTemplatePartitionResponse> activeCommandTemplates,
        CancellationToken ct,
        bool noCache = false)
    {
        // Liveness gate (ADR 0073): probe the cheap heappe/Health endpoint BEFORE any data call. A dead instance
        // is skipped here within the health-probe timeout (~15 s) instead of stalling the parallel build on a
        // 45 s data attempt; the failed probe propagates as a Failure so Core marks this instance degraded.
        if (!await heappeLivenessProbe.IsInstanceAliveAsync(identifier, ct))
        {
            logger.LogWarning(
                "HEAppE instance {HeappeIdentifier} (Project:{ProjectShortName}) failed its liveness probe; skipping as unavailable",
                identifier, projectShortName.Value);
            return Result<IList<ExtProject.ClusterInfoModel>, Error>.Failure(Errors.HeappeInstanceUnreachable(identifier));
        }

        var listAvailableClustersResponse = await GetCachedClustersForLocationsAsync(projectShortName, identifier, bearerToken, locationNames, noCache, ct);

        // A failed cluster fetch (HEAppE down, timeout, or mid-rebuild skeleton) MUST propagate as a failure so
        // the Core degraded-detection (ProjectService.ProcessUrlGroupAsync) marks the build degraded and the
        // TemplatesInfo cache is not poisoned with an empty snapshot. The pruning branch below reads
        // `.Value ?? []`, which on a failed Result silently became an empty success — turning an unavailable
        // instance into a valid-looking empty 200 cached for the full TTL. Guard before either branch.
        if (!listAvailableClustersResponse.IsSuccess)
        {
            logger.LogWarning(
                "HEAppE cluster fetch failed for project {ProjectShortName} (Resource:{ResourceName}, Identifier:{HeappeIdentifier}); propagating as degraded. Error: {Error}",
                projectShortName.Value, resourceName, identifier, listAvailableClustersResponse.Error?.Description);
            return Result<IList<ExtProject.ClusterInfoModel>, Error>.Failure(
                listAvailableClustersResponse.Error, listAvailableClustersResponse.State);
        }

        // Core only asks for links that are active in UserOrg, so a matched cluster whose catalog has no active
        // allocation for this resource is an incomplete HEAppE answer (mid-refresh), not "no allocation". Fail
        // the group so the composed TemplatesInfo snapshot is not cached (#291). The per-cluster entry is NOT
        // evicted: it is shared by every project on the instance, and a permanently mis-provisioned project
        // would otherwise push all of them back onto the uncached ForceRefresh path on every call. The residual
        // is that a bad answer can persist for one TTL for this project only. noCache stays a pass-through.
        if (!noCache)
        {
            var today = DateTime.Today;
            var withoutAllocation = (listAvailableClustersResponse.Value ?? [])
                .Where(c => !ClusterHasActiveAllocation(c, resourceName, today))
                .ToList();
            if (withoutAllocation.Count > 0)
            {
                var clusterNames = string.Join(", ", withoutAllocation.Select(c => c.Name));
                logger.LogWarning(
                    "HEAppE instance {HeappeIdentifier} (Project:{ProjectShortName}) returned cluster(s) [{Clusters}] without an active allocation for resource '{ResourceName}' (today={Today}); reported as degraded, snapshot not cached",
                    identifier, projectShortName.Value, clusterNames, resourceName, today);
                return Result<IList<ExtProject.ClusterInfoModel>, Error>.Failure(Errors.HeappeAllocationMissing(resourceName, clusterNames));
            }
        }

        if (activeCommandTemplates.Count == 0)
        {
            // No active partitions for this project — pruning is skipped entirely, so HEAppE's full template
            // list passes through unfiltered. Worth a log line: an empty allow-list reads as "allow everything",
            // which is the opposite of what an operator who just disabled every template expects.
            logger.LogWarning(
                "Project {ProjectShortName} (Resource:{ResourceName}, Identifier:{HeappeIdentifier}) has NO active command template partitions — command-template pruning is skipped and HEAppE's full template list is returned unfiltered",
                projectShortName.Value, resourceName, identifier);
            return Result<IList<ExtProject.ClusterInfoModel>, Error>.Success(
                (listAvailableClustersResponse.Value ?? []).Select(x => x.ConvertToModel()).ToList());
        }

        // Prune by the project's active command templates, fetched once by Core (ADR 0074). Use the set as-is
        // when it already is a HashSet (the common path) so pruning stays O(1) per lookup.
        var allowedTemplates = activeCommandTemplates as HashSet<CommandTemplatePartitionResponse> ?? [.. activeCommandTemplates];

        // Clone before pruning: the raw cluster list is shared from the cache across projects/resources,
        // and PruneDisallowedCommandTemplates mutates the nested Projects/CommandTemplates lists in place.
        var clusters = CloneClusters(listAvailableClustersResponse.Value ?? []);
        var pruned = PruneDisallowedCommandTemplates(clusters, allowedTemplates, resourceName);

        // Pruning removed data that HEAppE did return. Without this the removal is invisible: Core only sees
        // the pruned result and reports it as "HEAppE has no matching templates", pointing at the wrong side.
        // RemovedTemplates counts templates dropped because the (Name, Id) key did not match an active
        // partition exactly — the match is case-sensitive on both parts, so a renamed or re-created template
        // in HEAppE silently disappears here.
        if (pruned.RemovedProjects > 0 || pruned.RemovedTemplates > 0)
        {
            logger.LogInformation(
                "Project {ProjectShortName} (Resource:{ResourceName}, Identifier:{HeappeIdentifier}): pruning removed {RemovedProjects} project allocation(s) whose AccountingString is not '{ResourceName}' and {RemovedTemplates} command template(s) absent from the {AllowedCount} active partition(s); {RemainingTemplates} template(s) remain. Active partition keys: [{AllowedKeys}]",
                projectShortName.Value, resourceName, identifier, pruned.RemovedProjects, resourceName,
                pruned.RemovedTemplates, allowedTemplates.Count, pruned.RemainingTemplates,
                string.Join(", ", allowedTemplates.Select(t => $"'{t.Name}'#{t.CommandTemplateId}")));
        }

        return Result<IList<ExtProject.ClusterInfoModel>, Error>.Success(clusters.Select(x => x.ConvertToModel()).ToList());
    }

    private async ValueTask<Result<IList<ClusterInfoModel>, Error>> GetCachedClustersForLocationsAsync(
        ProjectShortName projectShortName,
        HeappeIdentifier identifier,
        string bearerToken,
        IReadOnlyCollection<string> locationNames,
        bool noCache,
        CancellationToken ct)
    {
        // Fetch the whole instance catalog at most once per call, shared by every requested location. A cache
        // MISS across CONCURRENT calls is still de-duplicated by HybridCache single-flight on each per-cluster
        // key, so this Lazy only coalesces the multiple locations of THIS call.
        var catalog = new Lazy<Task<Result<IList<ClusterInfoModel>, Error>>>(
            () => FetchWholeCatalogAsync(projectShortName, identifier, bearerToken, ct));

        var found = new List<ClusterInfoModel>();
        foreach (var name in locationNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var cluster = await cache.GetOrCreateAsync(
                    CacheTags.HeappeCluster(identifier, name),
                    async _ =>
                    {
                        var response = await catalog.Value;

                        // A failed catalog fetch must never be cached — propagate so the Core build is marked
                        // degraded instead of poisoning the cache with an empty/false snapshot.
                        if (!response.IsSuccess)
                            throw new HeappeClustersUnavailableException(response.Error);

                        var available = response.Value ?? [];
                        var match = available
                            .FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                        // The cluster is absent (instance still hydrating after wake-up, or the link is
                        // misconfigured), or present without usable node/project data (a mid-rebuild skeleton).
                        // Throw so HybridCache caches NOTHING for this key: a missing cluster stays a plain
                        // cache miss the next call refetches, never a stored "this cluster is empty" snapshot
                        // that would keep serving every project on the instance for the full TTL.
                        if (match is null || !ClusterHasUsableData(match))
                        {
                            // Log the actual catalog cluster names here, where the full fetched catalog is in
                            // scope: the Core-level "no match" warning only sees the clusters this adapter
                            // returned, so without this a misconfigured LocationName (typo / renamed cluster)
                            // could not be diagnosed from logs.
                            logger.LogWarning(
                                "HEAppE instance {HeappeIdentifier} (Project:{ProjectShortName}) has no usable cluster matching LocationName '{ClusterName}' ({Reason}); not cached. Catalog cluster names: [{Available}]",
                                identifier, projectShortName.Value, name,
                                match is null ? "absent from catalog" : "present but no node/project data (mid-rebuild skeleton)",
                                string.Join(", ", available.Select(c => c.Name)));
                            throw new ClusterUnavailableException();
                        }

                        return match;
                    },
                    noCache ? _clustersBypassCacheOptions : _clustersCacheOptions,
                    tags: [CacheTags.HeappeClusters(identifier)],
                    cancellationToken: ct);

                found.Add(cluster);
            }
            catch (HeappeClustersUnavailableException ex)
            {
                // The catalog fetch itself failed — the whole instance is unavailable, so fail the call and
                // let the Core build detect the degradation.
                logger.LogWarning(
                    "HEAppE cluster query failed for instance {HeappeIdentifier} (Project:{ProjectShortName}); snapshot not cached. Error: {Error}",
                    identifier, projectShortName.Value, ex.Error?.Description);
                return Result<IList<ClusterInfoModel>, Error>.Failure(ex.Error);
            }
            catch (ClusterUnavailableException)
            {
                // This location's cluster is not (yet) in the catalog (already logged with the catalog names
                // in the factory). Omit it — Core marks the unmatched active link degraded, so the composed
                // TemplatesInfo snapshot is not cached either.
            }
        }

        return Result<IList<ClusterInfoModel>, Error>.Success(found);
    }

    private async Task<Result<IList<ClusterInfoModel>, Error>> FetchWholeCatalogAsync(
        ProjectShortName projectShortName,
        HeappeIdentifier identifier,
        string bearerToken,
        CancellationToken ct)
    {
        var connection = await GetHeappeConnectionInfoAsync(projectShortName, identifier, bearerToken, ct);
        if (!connection.IsSuccess)
            return Result<IList<ClusterInfoModel>, Error>.Failure(connection.Error, connection.State);

        var heappeConnInfo = connection.Value;
        return heappeConnInfo switch
        {
            ServiceTokenHeappeConnectionInfo => (await heappeService.GetClusters(heappeConnInfo, ct))
                .Map<IList<ClusterDetailedModel>, IList<ClusterInfoModel>, Error>(x => x.Select(ClusterInfoModel.MapFrom).ToList()),
            _ => await heappeService.GetListAvailableClusters(heappeConnInfo, [new("ForceRefresh", "true")], ct)
        };
    }

    private static IList<ClusterInfoModel> CloneClusters(IList<ClusterInfoModel> clusters)
        => JsonSerializer.Deserialize<List<ClusterInfoModel>>(JsonSerializer.Serialize(clusters)) ?? [];

    // A live HEAppE cluster reports node types carrying project allocations; a cluster without them is a
    // mid-rebuild skeleton and must not be cached as usable.
    private static bool ClusterHasUsableData(ClusterInfoModel cluster)
        => (cluster.NodeTypes ?? []).Any(nt => nt.Projects is { Count: > 0 });

    private static bool ClusterHasActiveAllocation(ClusterInfoModel cluster, string resourceName, DateTime today)
        => (cluster.NodeTypes ?? []).Any(nt => (nt.Projects ?? []).Any(p =>
            string.Equals(p.AccountingString, resourceName, StringComparison.OrdinalIgnoreCase)
            && p.StartDate.Date <= today.Date && today.Date <= p.EndDate.Date));

    private sealed class HeappeClustersUnavailableException(Error error) : Exception
    {
        public Error Error { get; } = error;
    }

    // Internal signal that a requested cluster is absent from (or not yet usable in) the fetched catalog.
    // Thrown out of the per-cluster cache factory so HybridCache stores nothing for that key.
    private sealed class ClusterUnavailableException : Exception;

    public async ValueTask<Result<Dictionary<string, DownloadedFileModel>, Error>> DownloadChangedFilesAsync(
        HeappeIdentifier identifier,
        string bearerToken,
        long submittedJobInfoId,
        CancellationToken ct = default)
    {
        var connection = await GetHeappeConnectionInfoAsync(identifier, bearerToken, ct);
        if (!connection.IsSuccess)
            return Result<Dictionary<string, DownloadedFileModel>, Error>.Failure(connection.Error, connection.State);

        return await heappeService.DownloadChangedFilesAsync(connection.Value, submittedJobInfoId, ct);
    }

    private static PruneStats PruneDisallowedCommandTemplates(IEnumerable<ClusterInfoModel> clusters, HashSet<CommandTemplatePartitionResponse> allowedTemplates, string resourceName)
    {
        if (clusters is null || allowedTemplates is null || allowedTemplates.Count == 0)
        {
            return new PruneStats(0, 0, 0);
        }

        var nodes = clusters
            .SelectMany(c => c.NodeTypes ?? [])
            .ToList();

        var stats = new PruneStats(0, 0, 0);
        nodes.ForEach(f => stats += RemoveDisallowedProjectsFromNodes(f, resourceName, allowedTemplates));
        return stats;
    }

    private static PruneStats RemoveDisallowedProjectsFromNodes(NodeTypeModel node, string resourceName, HashSet<CommandTemplatePartitionResponse> allowedTemplates)
    {
        if (node.Projects == null || node.Projects.Count == 0)
        {
            return new PruneStats(0, 0, 0);
        }

        var stats = new PruneStats(0, 0, 0);
        for (var i = node.Projects.Count - 1; i >= 0; i--)
        {
            var tmpl = node.Projects[i];
            // AccountingString, not Name: every matcher downstream (Core BuildTemplateResponses, the allocation
            // guard above) keys on AccountingString, and HEAppE's Name can differ from it.
            if (!string.Equals(tmpl.AccountingString, resourceName, StringComparison.OrdinalIgnoreCase))
            {
                node.Projects.RemoveAt(i);
                stats = stats with { RemovedProjects = stats.RemovedProjects + 1 };
                continue;
            }
            stats += RemoveDisallowedTemplatesFromProject(tmpl, allowedTemplates);
        }

        return stats;
    }

    private static PruneStats RemoveDisallowedTemplatesFromProject(ProjectModel project, HashSet<CommandTemplatePartitionResponse> allowedTemplates)
    {
        if (project.CommandTemplates == null || project.CommandTemplates.Count == 0)
        {
            return new PruneStats(0, 0, 0);
        }

        var removed = 0;
        for (var i = project.CommandTemplates.Count - 1; i >= 0; i--)
        {
            var tmpl = project.CommandTemplates[i];
            var key = new CommandTemplatePartitionResponse(tmpl.Name ?? string.Empty, tmpl.Id);
            if (!allowedTemplates.Contains(key))
            {
                project.CommandTemplates.RemoveAt(i);
                removed++;
            }
        }

        return new PruneStats(0, removed, project.CommandTemplates.Count);
    }

    /// <summary>
    /// What a pruning pass removed, so the caller can log it — the pruning itself mutates the cluster tree in
    /// place and would otherwise leave no trace of the data it dropped.
    /// </summary>
    private readonly record struct PruneStats(int RemovedProjects, int RemovedTemplates, int RemainingTemplates)
    {
        public static PruneStats operator +(PruneStats a, PruneStats b)
            => new(a.RemovedProjects + b.RemovedProjects, a.RemovedTemplates + b.RemovedTemplates, a.RemainingTemplates + b.RemainingTemplates);
    }
}