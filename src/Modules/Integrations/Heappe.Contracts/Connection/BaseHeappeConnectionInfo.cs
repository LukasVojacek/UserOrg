using UserOrg.Shared;

namespace Heappe.Contracts.Connection;

/// <summary>How to reach one HEAppE instance: its address behind the Nexus proxy and its identifier.</summary>
/// <param name="BaseAddress">The Nexus proxy base <c>{TokenExchange:Nexus:BaseUrl}/api/heappe/{identifier}/</c>.</param>
/// <param name="Identifier">The instance the address belongs to; keys the per-instance circuit breaker.</param>
public abstract record BaseHeappeConnectionInfo
(
    Uri BaseAddress,
    HeappeIdentifier Identifier
);