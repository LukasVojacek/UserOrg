using Core.Shared.Http;
using Core.Shared.OutputCache;
using Core.Shared.Paging;

using Heappe.Contracts;
using Heappe.Contracts.CommandTemplate;
using Heappe.Contracts.HEappeApiDto.Responses;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UserOrg.Shared;
using UserOrg.Shared.Extensions;

namespace Core.Features.Project.GetProjectTemplates;

internal static class GetProjectTemplatesEndpoint
{
    private sealed record TemplateInfo(
        bool IsAvailable,
        string ClusterName,
        string LocationId,
        long? ClusterId,
        Guid ResourceId,
        IReadOnlyList<NodeResponse> Nodes);

    internal sealed record Response(
        bool IsAvailable,
        string ClusterName,
        string LocationId,
        long? ClusterId,
        IReadOnlyList<NodeResponse> Nodes);

    internal sealed record NodeResponse(
        long NodeId,
        string NodeType,
        int? NumberOfNodes,
        int ClusterPerNode,
        int MaxWallTime,
        int FileTransferMethodId,
        IReadOnlyList<NodeAllocationResponse> Allocations);

    internal sealed record NodeAllocationResponse(
        string LexisResourceName,
        IReadOnlyList<CommandTemplateResponse> CommandTemplates);

    internal sealed record CommandTemplateResponse(
        long Id,
        string Name,
        bool IsGeneric,
        IReadOnlyList<TemplateParameterResponse> Parameters);

    internal sealed record TemplateParameterResponse(string? Identifier, string? Description);

    internal static async Task<IResult> HandleAsync(
        HttpContext http,
        [FromRoute] ProjectShortName projectShortName,
        Caller caller,
        [FromServices] IProjectQuery projectQuery,
        [FromServices] IHeappeConnectionService heappeConnectionService,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(typeof(GetProjectTemplatesEndpoint).FullName!);

        if (caller.RequireProjectScope(projectShortName) is { } denied)
            return denied;

        var originalToken = await OriginalBearerToken.BindAsync(http);
        if (originalToken is null)
            return Results.Unauthorized();
        var bearerToken = originalToken.Value;

        var links = await projectQuery.GetProjectHeappeLinksAsync(projectShortName, ct);
        if (links is null)
            return Problems.ProjectNotFound();

        if (links.Count == 0)
        {
            logger.LogWarning(
                "Project {ShortName}: no HEAppE links found — no HPC/Quantum locations configured or all filtered by IsDeleted/type/missing HEAPPE_IDENTIFIER",
                projectShortName.Value);
            return PagedResponse.Complete(http, (IReadOnlyList<Response>)[]);
        }

        var currentDate = DateTimeOffset.UtcNow.Date;
        var activeLinks = FilterActiveLinks(logger, projectShortName, links, currentDate);
        if (activeLinks.Count == 0)
            return PagedResponse.Complete(http, (IReadOnlyList<Response>)[]);

        var projectId = activeLinks[0].ProjectId;

        // Read the allowed-template partition set once per request — projectId is constant across links.
        var allowed = await heappeConnectionService.GetActiveCommandTemplatesAsync(projectId, ct) ?? [];

        var groupResults = await Task.WhenAll(
            activeLinks
                .GroupBy(l => l.HeappeIdentifier)
                .Select(group => BuildTemplatesForInstanceAsync(
                    heappeConnectionService, logger, projectShortName, group.Key,
                    bearerToken, group.ToList(), allowed, currentDate, ct)));

        // A transient HEAppE failure (null/non-success/timeout) yields IsAvailable=false entries;
        // don't let the output cache store that and serve it to later callers.
        if (groupResults.Any(r => r.Failed))
            ResponseCacheControl.SuppressStorage(http);

        var merged = MergeTemplateInfos(groupResults.SelectMany(r => r.Templates));

        ResponseCacheTags.Add(http, merged.SelectMany(e =>
            new[] { Core.Shared.OutputCache.CacheTags.ProjectResource(e.ResourceId), Core.Shared.OutputCache.CacheTags.Location(e.LocationId) }));

        return PagedResponse.Complete(http, merged.Select(MapToResponse).ToList());
    }

    private static List<ProjectHeappeLinkEntry> FilterActiveLinks(
        ILogger logger,
        ProjectShortName shortName,
        IReadOnlyList<ProjectHeappeLinkEntry> links,
        DateTimeOffset currentDate)
    {
        var active = links.Where(l => l.StartDate <= currentDate && currentDate <= l.EndDate).ToList();

        foreach (var expired in links.Where(l => l.StartDate > currentDate || l.EndDate < currentDate))
        {
            logger.LogWarning(
                "Project {ShortName}: link '{LocationName}' (Resource:{ResourceName}, Identifier:{HeappeIdentifier}) is outside active date window — StartDate={StartDate}, EndDate={EndDate}, today={Today}",
                shortName.Value, expired.LocationName, expired.ResourceName, expired.HeappeIdentifier,
                expired.StartDate, expired.EndDate, currentDate);
        }

        if (active.Count == 0)
        {
            logger.LogWarning(
                "Project {ShortName}: all {Total} HEAppE link(s) are outside the active date window (today={Today})",
                shortName.Value, links.Count, currentDate);
        }

        return active;
    }

    private static List<TemplateInfo> MergeTemplateInfos(IEnumerable<TemplateInfo> flat)
    {
        var merged = new List<TemplateInfo>();

        // One entry per (LocationId, ClusterName). A cluster can be linked via several resources;
        // if any link makes it available, surface a single available entry with unified nodes and
        // drop the unavailable duplicates — only emit an unavailable entry when no link succeeded.
        foreach (var group in flat
            .Where(x => !string.IsNullOrEmpty(x.LocationId))
            .GroupBy(x => (x.LocationId, x.ClusterName)))
        {
            var available = group.Where(x => x.IsAvailable).ToList();
            if (available.Count == 0)
            {
                merged.Add(group.First());
                continue;
            }

            if (available.Count == 1)
            {
                merged.Add(available[0]);
                continue;
            }

            var deduplicatedNodes = available.SelectMany(x => x.Nodes)
                .GroupBy(n => (n.NodeId, n.NodeType, n.FileTransferMethodId, n.NumberOfNodes, n.ClusterPerNode, n.MaxWallTime))
                .Select(ng => new NodeResponse(
                    ng.Key.NodeId,
                    ng.Key.NodeType,
                    ng.Key.NumberOfNodes,
                    ng.Key.ClusterPerNode,
                    ng.Key.MaxWallTime,
                    ng.Key.FileTransferMethodId,
                    ng.SelectMany(n => n.Allocations)
                        .GroupBy(a => a.LexisResourceName)
                        .Select(ag => new NodeAllocationResponse(
                            ag.Key,
                            ag.SelectMany(a => a.CommandTemplates).DistinctBy(c => c.Id).ToList()))
                        .ToList()))
                .ToList();

            merged.Add(available[0] with { Nodes = deduplicatedNodes });
        }

        return merged;
    }

    private static Response MapToResponse(TemplateInfo e) =>
        new(e.IsAvailable, e.ClusterName, e.LocationId, e.ClusterId, e.Nodes);

    private enum LinkOutcome
    {
        Available,
        ClusterNotListed,
        AllocationMissing
    }

    private sealed record IdentifierGroupResult(IReadOnlyList<TemplateInfo> Templates, bool Failed);

    private static async Task<IdentifierGroupResult> BuildTemplatesForInstanceAsync(
        IHeappeConnectionService heappeConnectionService,
        ILogger logger,
        ProjectShortName shortName,
        HeappeIdentifier identifier,
        string bearerToken,
        List<ProjectHeappeLinkEntry> links,
        IReadOnlySet<CommandTemplatePartitionResponse> allowed,
        DateTimeOffset currentDate,
        CancellationToken ct)
    {
        // Fail-fast: probe the host once per identifier. A dead host gives up after the short probe timeout
        // (delaying only its own resources) instead of dragging the whole request.
        if (!await heappeConnectionService.IsHostAliveAsync(identifier, ct))
        {
            logger.LogWarning(
                "HEAppE host not reachable within probe timeout — skipping (Identifier:{Identifier}, Locations:[{Locations}])",
                identifier, LocationNames(links));
            return FailedGroup(links);
        }

        var clusters = await FetchCatalogAsync(shortName, identifier, links, bearerToken, heappeConnectionService, logger, ct);
        if (clusters is null)
        {
            return FailedGroup(links);
        }
            
        var results = new List<TemplateInfo>();
        var failed = false;
        var forget = false;
        foreach (var link in links)
        {
            var (templates, outcome) = ProcessLink(logger, identifier, link, clusters, allowed, currentDate);
            results.AddRange(templates);
            failed |= outcome != LinkOutcome.Available;
            forget |= outcome == LinkOutcome.AllocationMissing;
        }

        if (forget)
            await heappeConnectionService.ForgetClusterCatalogAsync(identifier, bearerToken, ct);

        return new IdentifierGroupResult(results, Failed: failed);
    }

    private static IdentifierGroupResult FailedGroup(List<ProjectHeappeLinkEntry> links)
        => new([.. links.Select(UnavailableEntry)], Failed: true);

    private static TemplateInfo UnavailableEntry(ProjectHeappeLinkEntry link)
        => new(false, link.LocationName, link.LocationId, null, link.ResourceId, []);

    private static string LocationNames(List<ProjectHeappeLinkEntry> links)
        => string.Join(", ", links.Select(l => l.LocationName));

    private static async Task<IList<ClusterInfoModel>?> FetchCatalogAsync(
        ProjectShortName shortName,
        HeappeIdentifier identifier,
        List<ProjectHeappeLinkEntry> links,
        string bearerToken,
        IHeappeConnectionService heappeConnectionService,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            // One catalog fetch per identifier — it carries no resource filter, so every link on this instance shares it.
            var response = await heappeConnectionService.GetClusterCatalogAsync(
                shortName.Value, identifier, bearerToken, ct);

            return ReadCatalog(logger, identifier, links, response);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(e,
                "Request timed out calling HEAppE (Identifier:{Identifier}, Locations:[{Locations}])",
                identifier, LocationNames(links));
            return null;
        }
        catch (TaskCanceledException e)
        {
            logger.LogWarning(e,
                "Request canceled calling HEAppE (Identifier:{Identifier})",
                identifier);
            return null;
        }
        catch (Exception e)
        {
            logger.LogError(e,
                "Unexpected error calling HEAppE (Identifier:{Identifier})",
                identifier);
            return null;
        }
    }

    private static IList<ClusterInfoModel>? ReadCatalog(
        ILogger logger,
        HeappeIdentifier identifier,
        List<ProjectHeappeLinkEntry> links,
        Result<IList<ClusterInfoModel>, Error>? response)
    {
        if (response is null)
        {
            logger.LogWarning(
                "HEAppE returned null response (Identifier:{Identifier}, Locations:[{Locations}])",
                identifier, LocationNames(links));
            return null;
        }

        if (!response.IsSuccess)
        {
            logger.LogWarning(
                "HEAppE returned non-success status (Identifier:{Identifier}, Locations:[{Locations}]). Error: {Error}",
                identifier, LocationNames(links), response.Error?.Description);
            return null;
        }

        return response.Value ?? [];
    }

    private static (IReadOnlyList<TemplateInfo> Templates, LinkOutcome Outcome) ProcessLink(
        ILogger logger,
        HeappeIdentifier identifier,
        ProjectHeappeLinkEntry link,
        IList<ClusterInfoModel> clusters,
        IReadOnlySet<CommandTemplatePartitionResponse> allowed,
        DateTimeOffset currentDate)
    {
        var matching = clusters
            .Where(c => c.Name.Equals(link.LocationName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matching.Count == 0)
        {
            logger.LogWarning(
                "HEAppE returned {ClusterCount} cluster(s) but none matched LocationName '{LocationName}' (Resource:{ResourceName}). Available: [{Clusters}]",
                clusters.Count, link.LocationName, link.ResourceName,
                string.Join(", ", clusters.Select(c => c.Name)));
            return ([UnavailableEntry(link)], LinkOutcome.ClusterNotListed);
        }

        // The link is active in UserOrg (FilterActiveLinks), so a matched cluster without an active allocation
        // for its resource is an incomplete HEAppE catalog (mid-refresh), not "no allocation". Fail the group
        // so the output cache does not keep the empty answer (#291). The catalog entry is per caller, so
        // forgetting it costs only this caller's next refetch and lets that refetch see a rebuilt catalog.
        if (matching.Any(c => !HasActiveAllocation(c, link.ResourceName, currentDate)))
        {
            logger.LogWarning(
                "HEAppE catalog (Identifier:{Identifier}) has cluster '{LocationName}' but no active allocation for resource '{ResourceName}' (today={Today}); group reported as failed, response not cached",
                identifier, link.LocationName, link.ResourceName, currentDate);
            return ([UnavailableEntry(link)], LinkOutcome.AllocationMissing);
        }

        return ([.. BuildTemplateResponses(logger, link, matching, allowed, currentDate)], LinkOutcome.Available);
    }

    private static bool HasActiveAllocation(ClusterInfoModel cluster, string resourceName, DateTimeOffset currentDate)
        => cluster.NodeTypes.Any(nt => nt.Projects.Any(p => IsActiveAllocation(p, resourceName, currentDate)));

    private static bool IsActiveAllocation(ProjectModel project, string resourceName, DateTimeOffset currentDate)
        => project.AccountingString.Equals(resourceName, StringComparison.OrdinalIgnoreCase)
            && project.StartDate <= currentDate && currentDate <= project.EndDate;

    private static IEnumerable<TemplateInfo> BuildTemplateResponses(
        ILogger logger,
        ProjectHeappeLinkEntry link,
        List<ClusterInfoModel> clusters,
        IReadOnlySet<CommandTemplatePartitionResponse> allowed,
        DateTimeOffset currentDate)
    {
        foreach (var cluster in clusters)
        {
            if (BuildCluster(logger, link, cluster, allowed, currentDate) is { } template)
                yield return template;
        }
    }

    private static TemplateInfo? BuildCluster(
        ILogger logger,
        ProjectHeappeLinkEntry link,
        ClusterInfoModel cluster,
        IReadOnlySet<CommandTemplatePartitionResponse> allowed,
        DateTimeOffset currentDate)
    {
        if (cluster.NodeTypes.Count == 0)
        {
            logger.LogDebug(
                "Cluster '{ClusterName}' (Location:{LocationName}, Resource:{ResourceName}) has no node types, skipping",
                cluster.Name, link.LocationName, link.ResourceName);
            return null;
        }

        var matchingNodeTypes = cluster.NodeTypes
            .Where(nt => nt.Projects.Any(p => IsActiveAllocation(p, link.ResourceName, currentDate)))
            .ToList();

        if (matchingNodeTypes.Count == 0)
        {
            LogNoMatchingNodeType(logger, link, cluster, currentDate);
            return null;
        }

        var nodes = new List<NodeResponse>();
        foreach (var nt in matchingNodeTypes)
        {
            if (BuildNode(logger, link, cluster, nt, allowed, currentDate) is { } node)
                nodes.Add(node);
        }

        return new TemplateInfo(true, cluster.Name, link.LocationId, cluster.Id, link.ResourceId, nodes);
    }

    private static void LogNoMatchingNodeType(
        ILogger logger,
        ProjectHeappeLinkEntry link,
        ClusterInfoModel cluster,
        DateTimeOffset currentDate)
    {
        var allAccounting = cluster.NodeTypes
            .SelectMany(nt => nt.Projects)
            .Select(p => p.AccountingString)
            .Distinct()
            .ToList();
        logger.LogWarning(
            "Cluster '{ClusterName}' (Location:{LocationName}): no node type matches AccountingString='{ResourceName}' with active date range (today={Today}). Found accounting strings: [{Strings}]",
            cluster.Name, link.LocationName, link.ResourceName, currentDate,
            string.Join(", ", allAccounting));
    }

    private static NodeResponse? BuildNode(
        ILogger logger,
        ProjectHeappeLinkEntry link,
        ClusterInfoModel cluster,
        NodeTypeModel nt,
        IReadOnlySet<CommandTemplatePartitionResponse> allowed,
        DateTimeOffset currentDate)
    {
        if (nt.Projects.All(p => p.StartDate >= currentDate || p.EndDate <= currentDate))
        {
            logger.LogDebug(
                "Node {NodeId}/{NodeName} in cluster '{ClusterName}': all projects outside active date range (today={Today}), excluded. Ranges: [{Ranges}]",
                nt.Id, nt.Name, cluster.Name, currentDate,
                string.Join(", ", nt.Projects.Select(p => $"{p.StartDate:d} to {p.EndDate:d}")));
            return null;
        }

        var commandTemplates = AllowedCommandTemplates(link, nt, allowed, currentDate);

        if (commandTemplates.Count == 0)
        {
            logger.LogWarning(
                "Node {NodeId}/{NodeName} in cluster '{ClusterName}' (Resource:{ResourceName}, Location:{LocationName}): no command templates after filtering. LinkEndDate={LinkEndDate}, today={Today}",
                nt.Id, nt.Name, cluster.Name, link.ResourceName, link.LocationName,
                link.EndDate, currentDate);
        }

        var allocations = commandTemplates.Count > 0
            ? (IReadOnlyList<NodeAllocationResponse>)[new NodeAllocationResponse(link.ResourceName, commandTemplates)]
            : [];
        return new NodeResponse(nt.Id, nt.Name, nt.NumberOfNodes, nt.CoresPerNode, nt.MaxWalltime, nt.FileTransferMethodId, allocations);
    }

    private static List<CommandTemplateResponse> AllowedCommandTemplates(
        ProjectHeappeLinkEntry link,
        NodeTypeModel nt,
        IReadOnlySet<CommandTemplatePartitionResponse> allowed,
        DateTimeOffset currentDate)
        => nt.Projects
            .Where(p => IsActiveAllocation(p, link.ResourceName, currentDate) && link.EndDate >= currentDate)
            .SelectMany(p => p.CommandTemplates)
            .Where(c => allowed.Count == 0 || allowed.Contains(new CommandTemplatePartitionResponse(c.Name, c.Id)))
            .Select(c => new CommandTemplateResponse(
                c.Id,
                c.Name,
                c.IsGeneric,
                c.TemplateParameters
                    .Select(tp => new TemplateParameterResponse(tp.Identifier, tp.Description))
                    .ToList()))
            .ToList();

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetProjectTemplatesEndpoint()
        {
            app.MapGet("Projects/{projectShortName}/Templates", HandleAsync)
                .CacheOutput(ProjectTemplatesCache.PolicyName)
                .WithETag()
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetProjectTemplatesV2")
            .WithSummary("Get project HEAppE templates")
            .WithDescription("Returns available HEAppE cluster and command template data for the project as a single complete page (bounded reference list — paging parameters are ignored). Requires X-Original-Authorization header (Keycloak bearer token).")
            .WithTags("Project")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}