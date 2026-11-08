using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared;

namespace Heappe.Contracts.Connection;

/// <summary>
/// The single place a HEAppE connection is built. Every connection addresses the instance through the NexusApi
/// proxy <c>{TokenExchange:Nexus:BaseUrl}/api/heappe/{identifier}/</c>; no caller holds an instance URL.
/// A failure is an <see cref="Error"/> whose code is a <see cref="HeappeFailureCause"/>.
/// </summary>
public interface IHeappeConnectionFactory
{
    /// <summary>A connection without credentials, for unauthenticated endpoints (version, health).</summary>
    ValueTask<Result<PublicHeappeConnectionInfo, Error>> ForPublicAsync(HeappeIdentifier identifier, CancellationToken ct);

    /// <summary>A connection that authenticates with UserOrg's own service key, read from Vault by identifier.</summary>
    ValueTask<Result<BaseHeappeConnectionInfo, Error>> ForServiceAsync(HeappeIdentifier identifier, CancellationToken ct);

    /// <summary>
    /// A connection that acts for the caller: with <paramref name="useToken"/> the caller's bearer token is sent as is,
    /// otherwise it is exchanged for a HEAppE session code first.
    /// </summary>
    ValueTask<Result<BaseHeappeConnectionInfo, Error>> ForUserAsync(HeappeIdentifier identifier, string bearerToken, bool useToken, CancellationToken ct);
}