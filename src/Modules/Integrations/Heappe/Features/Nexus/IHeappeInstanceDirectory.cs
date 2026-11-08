using Heappe.Contracts;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared;

namespace Heappe.Features.Nexus;

/// <summary>
/// Resolves a HEAppE identifier to its Nexus proxy route under <c>TokenExchange:Nexus:BaseUrl</c>. The implementation
/// is chosen once at startup by the token exchange flag: <see cref="HeappeInstanceDirectory"/> reads the registry
/// NexusApi serves (flag on), <see cref="ProxyOnlyHeappeInstanceDirectory"/> routes every identifier without it (flag off).
/// </summary>
internal interface IHeappeInstanceDirectory
{
    /// <summary>The route of an enabled instance; <see cref="HeappeFailureCause.HeappeInstanceNotFound"/> when Nexus lists none, <see cref="HeappeFailureCause.NexusUnavailable"/> when the registry cannot be read.</summary>
    ValueTask<Result<HeappeRoute, Error>> ResolveAsync(HeappeIdentifier identifier, CancellationToken ct);

    /// <summary>Drops the cached registry snapshot, so the next <see cref="ResolveAsync"/> reads it again.</summary>
    ValueTask InvalidateAsync(CancellationToken ct);
}