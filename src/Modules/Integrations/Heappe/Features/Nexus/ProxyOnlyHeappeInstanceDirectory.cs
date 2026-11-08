using Heappe.Features.Connection;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.Options;

using UserOrg.Shared;

namespace Heappe.Features.Nexus;

/// <summary>
/// The directory with the token exchange flag off at startup: every identifier goes to
/// <c>{TokenExchange:Nexus:BaseUrl}/api/heappe/{identifier}/</c> without a registry query, a service account or an
/// instance <c>Enabled</c> check; NexusApi answers 404 for an unknown or disabled instance.
/// </summary>
internal sealed class ProxyOnlyHeappeInstanceDirectory(IOptions<TokenExchangeOptions> options) : IHeappeInstanceDirectory
{
    public ValueTask<Result<HeappeRoute, Error>> ResolveAsync(HeappeIdentifier identifier, CancellationToken ct) =>
        ValueTask.FromResult(Result<HeappeRoute, Error>.Success(
            new HeappeRoute(identifier, NexusEndpoints.ProxyBaseFor(options.Value.Nexus.BaseUrl, identifier), null, []),
            State.Ok)
        );

    public ValueTask InvalidateAsync(CancellationToken ct) => ValueTask.CompletedTask;
}