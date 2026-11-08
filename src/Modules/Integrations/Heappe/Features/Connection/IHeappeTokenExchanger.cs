using Heappe.Features.Nexus;

using Lukdrasil.Error;
using Lukdrasil.Result;

namespace Heappe.Features.Connection;

/// <summary>
/// Turns the caller's token into the token a user call to one HEAppE instance carries (ADR-0118). One of two
/// implementations is registered at startup by <see cref="TokenExchangeOptions.FeatureFlagName"/>:
/// <see cref="HeappeTokenExchanger"/> with the flag on, <see cref="PassThroughTokenExchanger"/> with it off.
/// </summary>
internal interface IHeappeTokenExchanger
{
    /// <summary>
    /// <see cref="PassThroughTokenExchanger"/>: <paramref name="bearerToken"/> unchanged and no IdP call.
    /// <see cref="HeappeTokenExchanger"/>: the token exchanged for the Keycloak client and scopes of
    /// <paramref name="route"/>, or a failure with <c>token-exchange-unavailable</c> or <c>token-exchange-failed</c>.
    /// An <see cref="OperationCanceledException"/> escapes only when <paramref name="ct"/> is cancelled.
    /// </summary>
    ValueTask<Result<string, Error>> ExchangeAsync(HeappeRoute route, string bearerToken, CancellationToken ct);
}