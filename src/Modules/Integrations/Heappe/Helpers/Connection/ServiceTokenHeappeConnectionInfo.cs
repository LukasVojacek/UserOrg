using Heappe.Contracts.Connection;

using UserOrg.Shared;

namespace Heappe.Helpers.Connection;

public sealed record ServiceTokenHeappeConnectionInfo
(
    Uri BaseAddress,
    HeappeIdentifier Identifier,
    string ServiceToken
) : BaseHeappeConnectionInfo(BaseAddress, Identifier);