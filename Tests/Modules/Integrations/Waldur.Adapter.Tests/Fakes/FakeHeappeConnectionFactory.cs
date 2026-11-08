using Heappe.Contracts.Connection;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared;

namespace Waldur.Adapter.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IHeappeConnectionFactory"/> that resolves every identifier to its NexusApi proxy
/// address without a registry or HTTP call.
/// </summary>
internal sealed class FakeHeappeConnectionFactory : IHeappeConnectionFactory
{
    private static Uri ProxyBase(HeappeIdentifier identifier) => new($"https+http://nexusapi/api/heappe/{identifier}/");

    public ValueTask<Result<PublicHeappeConnectionInfo, Error>> ForPublicAsync(HeappeIdentifier identifier, CancellationToken ct)
        => ValueTask.FromResult(Result<PublicHeappeConnectionInfo, Error>.Success(new PublicHeappeConnectionInfo(ProxyBase(identifier), identifier)));

    public ValueTask<Result<BaseHeappeConnectionInfo, Error>> ForServiceAsync(HeappeIdentifier identifier, CancellationToken ct)
        => ValueTask.FromResult(Result<BaseHeappeConnectionInfo, Error>.Success(new PublicHeappeConnectionInfo(ProxyBase(identifier), identifier)));

    public ValueTask<Result<BaseHeappeConnectionInfo, Error>> ForUserAsync(HeappeIdentifier identifier, string bearerToken, bool useToken, CancellationToken ct)
        => ValueTask.FromResult(Result<BaseHeappeConnectionInfo, Error>.Success(new BearerTokenHeappeConnectionInfo(ProxyBase(identifier), identifier, bearerToken)));
}
