using Heappe.Contracts.Connection;
using Heappe.Features.Nexus;

namespace Heappe.Features.Connection;

/// <summary>Turns the caller's bearer token into a connection to one routed HEAppE instance (token as is, or a HEAppE session code).</summary>
internal interface IHeappeUserAuthenticator
{
    Task<BaseHeappeConnectionInfo> AuthenticateAsync(HeappeRoute route, string bearerToken, bool useToken, CancellationToken ct);
}