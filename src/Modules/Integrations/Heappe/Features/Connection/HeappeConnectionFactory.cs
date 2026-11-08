using Heappe.Contracts.Connection;
using Heappe.Features.Credentials;
using Heappe.Features.Nexus;
using Heappe.Helpers.Connection;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared;

namespace Heappe.Features.Connection;

internal sealed class HeappeConnectionFactory(
    IHeappeInstanceDirectory directory,
    ICredentialsService credentials,
    IHeappeUserAuthenticator userAuthenticator,
    IHeappeTokenExchanger tokenExchanger) : IHeappeConnectionFactory
{
    public async ValueTask<Result<PublicHeappeConnectionInfo, Error>> ForPublicAsync(HeappeIdentifier identifier, CancellationToken ct)
    {
        var route = await directory.ResolveAsync(identifier, ct);
        return route.IsSuccess
            ? Result<PublicHeappeConnectionInfo, Error>.Success(new PublicHeappeConnectionInfo(route.Value.ProxyBaseAddress, route.Value.Identifier), State.Ok)
            : Result<PublicHeappeConnectionInfo, Error>.Failure(route.Error, route.State);
    }

    public async ValueTask<Result<BaseHeappeConnectionInfo, Error>> ForServiceAsync(HeappeIdentifier identifier, CancellationToken ct)
    {
        var route = await directory.ResolveAsync(identifier, ct);
        if (!route.IsSuccess)
        {
            return Result<BaseHeappeConnectionInfo, Error>.Failure(route.Error, route.State);
        }

        var credential = await credentials.GetCredentials(identifier.Value, ct);
        return credential.IsSuccess
            ? Result<BaseHeappeConnectionInfo, Error>.Success(
                new ServiceTokenHeappeConnectionInfo(route.Value.ProxyBaseAddress, route.Value.Identifier, credential.Value.SecretApiKey), State.Ok)
            : Result<BaseHeappeConnectionInfo, Error>.Failure(credential.Error, State.Error);
    }

    public async ValueTask<Result<BaseHeappeConnectionInfo, Error>> ForUserAsync(HeappeIdentifier identifier, string bearerToken, bool useToken, CancellationToken ct)
    {
        var route = await directory.ResolveAsync(identifier, ct);
        if (!route.IsSuccess)
        {
            return Result<BaseHeappeConnectionInfo, Error>.Failure(route.Error, route.State);
        }

        var token = await tokenExchanger.ExchangeAsync(route.Value, bearerToken, ct);
        return token.IsSuccess
            ? Result<BaseHeappeConnectionInfo, Error>.Success(await userAuthenticator.AuthenticateAsync(route.Value, token.Value, useToken, ct), State.Ok)
            : Result<BaseHeappeConnectionInfo, Error>.Failure(token.Error, State.Error);
    }
}