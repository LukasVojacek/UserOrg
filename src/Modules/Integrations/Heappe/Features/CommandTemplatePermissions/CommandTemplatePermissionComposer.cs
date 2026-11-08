using Heappe.Contracts.CommandTemplatePermissions.Contract;
using Heappe.Features.CommandTemplatePermissions.Response;
using Heappe.Features.KillSwitch;
using Heappe.Persistence.Enums;

namespace Heappe.Features.CommandTemplatePermissions;

/// <summary>
/// Correlates the materialized command-template partitions with the caller's project allocations and the
/// kill-switch state to produce the permission rows HEAppE gates launches on.
/// </summary>
/// <remarks>
/// This runs on rows already read from the database rather than inside the query: the partition projection
/// translates to SQL <c>APPLY</c> (SQL Server only), so an in-query degrade could not be covered by a
/// provider-portable test — and the degrade is the one rule that stops a launch during an incident.
/// </remarks>
internal static class CommandTemplatePermissionComposer
{
    /// <summary>
    /// Composes the permission rows for the caller's allocations.
    /// </summary>
    /// <param name="partitions">The materialized command-template partitions visible to the caller.</param>
    /// <param name="projectAllocations">The caller's project allocations, carrying the resource and location names.</param>
    /// <param name="killedPartitions">The partitions sitting on a frozen location (ADR-0077 §2). A killed partition keeps its row and
    /// reports <c>Enabled=false</c>; the stored state is never rewritten.</param>
    /// <returns>A permission row per (queue, resource, location) the caller is allocated to.</returns>
    internal static List<PermissionModel> Compose(
        IReadOnlyCollection<CommandTemplatePartitionRow> partitions,
        IList<ProjectAllocationInfoEntry> projectAllocations,
        IReadOnlySet<PartitionKillKey> killedPartitions)
    {
        var partitionsGroupedByProject = partitions
            .GroupBy(g => g.ProjectId)
            .ToDictionary(k => k.Key, v => v.ToList());

        return projectAllocations
            .Where(allocation => partitionsGroupedByProject.ContainsKey(allocation.Id))
            .SelectMany(allocation => ComposeForAllocation(allocation, partitionsGroupedByProject[allocation.Id], killedPartitions))
            .ToList();
    }

    private static IEnumerable<PermissionModel> ComposeForAllocation(
        ProjectAllocationInfoEntry allocation,
        List<CommandTemplatePartitionRow> projectPartitions,
        IReadOnlySet<PartitionKillKey> killedPartitions)
        => allocation.Resources.SelectMany(projectResource =>
            projectResource.Locations.SelectMany(location => projectPartitions
                .Where(p => MatchesLocation(p, projectResource.Id, location))
                .GroupBy(p => p.Partition)
                .Select(g => new PermissionModel(
                    QueueName: g.Key,
                    ProjectResource: projectResource.Name,
                    ClusterName: location.LocationName,
                    ProjectShortName: allocation.ShortName,
                    CommandTemplates: g.Select(s => new CommandTemplateModel(
                        s.CommandTemplateName,
                        IsEnabled(s, killedPartitions))).ToList()))));

    private static bool MatchesLocation(CommandTemplatePartitionRow partition, Guid projectResourceId, LocationAllocationInfo location)
        => partition.ProjectResourceId == projectResourceId
            && partition.LocationId == location.LocationId
            && (string.IsNullOrEmpty(location.AggregationName)
                || partition.AggregationName!.Equals(location.AggregationName, StringComparison.OrdinalIgnoreCase));

    private static bool IsEnabled(CommandTemplatePartitionRow partition, IReadOnlySet<PartitionKillKey> killedPartitions)
        => partition.CommandTemplateState == ECommandTemplateState.Active
            && !killedPartitions.Contains(new PartitionKillKey(partition.LocationId, partition.ProjectCommandTemplateId));
}