using Heappe.Contracts.Connection;

using UserOrg.Shared;

namespace Heappe.Helpers.Connection;

internal sealed record SessionCodeHEAppEConnectionInfo
(
    Uri BaseAddress,
    HeappeIdentifier Identifier,
    string SessionCode
) : BaseHeappeConnectionInfo(BaseAddress, Identifier);