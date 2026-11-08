using Lukdrasil.Error;
using Lukdrasil.Result;

namespace Heappe.Features.Credentials;

public interface ICredentialsService
{
    Task<Result<HeappeCredential, Error>> GetCredentials(string heappeIdentifier, CancellationToken cancellationToken);
}