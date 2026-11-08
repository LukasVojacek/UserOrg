using UserOrg.Shared;

namespace Heappe.Contracts.Entries.Sync;

/// <summary>
/// Represents a HEAppE instance entry, including its identifier, version, and the URI kept for logs.
/// </summary>
/// <param name="Uri">The direct URL of the HEAppE instance. Only for logs: every call is routed by <paramref name="Identifier"/> through NexusApi.</param>
/// <param name="Version">The version of the HEAppE instance.</param>
/// <param name="Identifier">The identifier of the HEAppE instance in the NexusApi registry.</param>
public sealed record HeappeInstanceSyncEntry
(
    Uri? Uri,
    Version Version,
    HeappeIdentifier Identifier
);