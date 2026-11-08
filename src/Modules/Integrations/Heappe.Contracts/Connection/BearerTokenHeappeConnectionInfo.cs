using UserOrg.Shared;

namespace Heappe.Contracts.Connection;

public sealed record BearerTokenHeappeConnectionInfo
(
    Uri BaseAddress,
    HeappeIdentifier Identifier,
    string BearerToken
) : BaseHeappeConnectionInfo(BaseAddress, Identifier);