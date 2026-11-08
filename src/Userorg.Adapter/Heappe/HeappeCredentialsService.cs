using HashiCorpVault.Contracts;

using Heappe.Features.Credentials;

using Lukdrasil.Error;
using Lukdrasil.Result;

namespace Userorg.Adapter.Heappe;

internal sealed class HeappeCredentialsService(IHashiCorpVaultClient vaultClient) : ICredentialsService
{
    public Task<Result<HeappeCredential, Error>> GetCredentials(string heappeIdentifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(heappeIdentifier);
        return vaultClient.GetExternalCredentials(heappeIdentifier, cancellationToken)
            .MapAsync(secret => new HeappeCredential { SecretApiKey = secret.ApiKey ?? "" });
    }
}