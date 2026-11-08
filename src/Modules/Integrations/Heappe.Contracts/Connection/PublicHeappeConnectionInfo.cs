using UserOrg.Shared;

namespace Heappe.Contracts.Connection;

public sealed record PublicHeappeConnectionInfo
(
    Uri BaseAddress,
    HeappeIdentifier Identifier
) : BaseHeappeConnectionInfo(BaseAddress, Identifier);