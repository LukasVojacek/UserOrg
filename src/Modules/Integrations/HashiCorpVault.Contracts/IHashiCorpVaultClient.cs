using System.Collections.Generic;
using System.Threading;

using HashiCorpVault.Contracts.Models;

using Lukdrasil.Error;
using Lukdrasil.Result;

namespace HashiCorpVault.Contracts;

public interface IHashiCorpVaultClient
{
    Task<Result<IReadOnlyCollection<ServiceAccessKeys>, Error>> GetServiceAccessKeysAsync(CancellationToken cancellation = default);
    Task<Result<string, Error>> GetServiceAccessKeyDataAsync(string KeyName, CancellationToken cancellation = default);
    Task<Result<ExternalServiceAccessCredentials, Error>> GetExternalCredentials(string Name, CancellationToken cancellation);

    Task<Result<BulkUpdateResult, Error>> UpdateServiceAccessKeysBulkAsync(IEnumerable<DataStagingLocationKey> items, CancellationToken cancellation = default);
    Task<Result<IReadOnlyCollection<DataStagingLocationKey>, Error>> GetDDIDataStagingKeysAsync(CancellationToken cancellation = default);
}