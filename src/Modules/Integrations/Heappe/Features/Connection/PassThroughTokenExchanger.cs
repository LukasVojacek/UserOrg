using Heappe.Features.Nexus;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared;

namespace Heappe.Features.Connection;

/// <summary>
/// The exchanger with the token exchange flag off at startup: the caller's token goes to the NexusApi proxy unchanged,
/// with no IdP call, so TokenDelegation is not registered.
/// </summary>
internal sealed class PassThroughTokenExchanger : IHeappeTokenExchanger
{
    public ValueTask<Result<string, Error>> ExchangeAsync(HeappeRoute route, string bearerToken, CancellationToken ct) =>
        ValueTask.FromResult(Result<string, Error>.Success(bearerToken, State.Ok));
}