using System.Diagnostics.CodeAnalysis;

namespace UserOrg.Shared;

/// <summary>
/// Central cache key/tag builders shared across modules (Core application, HEAppE integration,
/// adapter). Keeping them in one place ensures producers and invalidators agree on the exact tag.
/// </summary>
public static class CacheTags
{
    /// <summary>Tag for the composed TemplatesInfo response of a single project.</summary>
    public static string TemplatesInfo(string projectShortName) => $"TemplatesInfo_{projectShortName.ToLowerInvariant()}";

    /// <summary>
    /// Tag applied to every cached HEAppE cluster of a single instance. Used for bulk invalidation of an
    /// instance's clusters (e.g. on command-template changes); individual clusters are keyed by
    /// <see cref="HeappeCluster"/> so a missing cluster is a plain cache miss rather than a masked partial.
    /// The identifier is normalised (lower case), so <c>HEAppE-Mock</c> and <c>heappe-mock</c> give the same tag.
    /// </summary>
    public static string HeappeClusters(HeappeIdentifier identifier) => $"Heappe_Clusters_{identifier.Value}";

    /// <summary>Cache key for a single HEAppE cluster on an instance, keyed by instance identifier and cluster name.</summary>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "A cache tag is folded to lower case so the cluster name matches whatever its case; this is not security normalization.")]
    public static string HeappeCluster(HeappeIdentifier identifier, string clusterName)
    {
        ArgumentNullException.ThrowIfNull(clusterName);
        return $"Heappe_Cluster_{identifier.Value}_{clusterName.ToLowerInvariant()}";
    }

    /// <summary>
    /// The single tag carried by every cache entry a Location kill-switch must degrade — HEAppE command-template
    /// permissions and the v1 TemplatesInfo response alike (ADR-0077 §3, §4). Enforcing or lifting evicts this one
    /// tag, dropping them all at once; per-project tags alone would leave a partial-eviction hole during an incident.
    /// </summary>
    public const string KillSwitchGlobal = "CmdTemplatePerm_KillSwitch";

    /// <summary>Tag for the full role list (ListRoles). Invalidated on any role mutation.</summary>
    public const string RoleList = "RoleList";

    /// <summary>Tag for the role name dictionary (ListRoleNames). Invalidated on any role mutation.</summary>
    public const string RoleNames = "RoleNames";
}