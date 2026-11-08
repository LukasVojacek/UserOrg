using UserOrg.Shared;

namespace Heappe.Contracts.CommandTemplatePermissions.Contract;

/// <summary>
/// Represents an entry for a Heappe instance, including its identifier, version, and the URI kept for logs.
/// </summary>
/// <param name="Uri">The direct URL of the Heappe instance. Only for logs: every call is routed by <paramref name="Identifier"/> through NexusApi.</param>
/// <param name="Version">The version of the Heappe instance.</param>
/// <param name="Identifier">The identifier of the Heappe instance in the NexusApi registry.</param>
public sealed record HeappeInstanceEntry
(
    Uri? Uri,
    Version Version,
    HeappeIdentifier Identifier
);