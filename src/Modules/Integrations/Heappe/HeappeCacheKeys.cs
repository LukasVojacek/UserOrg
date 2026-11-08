using UserOrg.Shared;

namespace Heappe;

/// <summary>
/// Cache key and tag builders shared between the command-template permission query (cache producer)
/// and the command-template mutation/synchronization services (cache invalidators).
/// </summary>
internal static class HeappeCacheKeys
{
    internal static string PermissionAllocations(string idpUserId, string heappeIdentifier)
        => $"CmdTemplatePerm_Alloc_{idpUserId}_{heappeIdentifier}";

    internal static string Permissions(string idpUserId, string heappeIdentifier)
        => $"CmdTemplatePerm_{idpUserId}_{heappeIdentifier}";

    internal static string PermissionHeappeTag(string heappeIdentifier)
        => $"CmdTemplatePerm_Heappe_{heappeIdentifier}";

    internal static string PermissionProjectTag(Guid projectId)
        => $"CmdTemplatePerm_Project_{projectId}";

    /// <summary>
    /// Single cache tag added to every cached permission entry (ADR-0077 §4). A kill-switch
    /// PUT evicts this one tag via <c>RemoveByTagAsync</c>, dropping all permission entries at
    /// once rather than leaving a partial-eviction hole during an incident.
    /// </summary>
    /// <remarks>
    /// The tag spans modules — Core's v1 TemplatesInfo entry carries it too, so the one eviction degrades
    /// both read paths — so the value lives in <see cref="CacheTags"/> where every producer and the evictor
    /// agree on it. This name remains the Heappe-side vocabulary for it.
    /// </remarks>
    internal const string KillSwitchGlobalTag = CacheTags.KillSwitchGlobal;
}