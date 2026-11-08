using Lukdrasil.Error;
using Lukdrasil.Result;

namespace HashiCorpVault;

internal static class ResultCollectionExtensions
{
    extension<T>(Result<IReadOnlyCollection<T>, Error> credentials) where T : notnull
    {
        internal Result<T, Error> FindByName(string identifier, Func<T, string> nameSelector)
        {

            if (!credentials.IsSuccess)
            {
                return Result<T, Error>.Failure(
                    new Error(
                        "Vault.FetchFailed",
                        ErrorSeverity.Error,
                        "Failed to retrieve external credentials from Vault."
                    )
                );
            }

            if (credentials.Value.Count == 0)
            {
                return Result<T, Error>.Failure(
                    new Error(
                        "Vault.FetchZero",
                        ErrorSeverity.Error,
                        $"No {typeof(T).Name} entries found in the Vault."
                    )
                );
            }

            var foundValue = credentials.Value.FirstOrDefault(c => string.Equals(nameSelector(c), identifier, StringComparison.OrdinalIgnoreCase));

            if (foundValue == null)
            {
                return Result<T, Error>.Failure(
                    new Error(
                        "Vault.SecretNotFound",
                        ErrorSeverity.Error,
                        $"{typeof(T).Name} with identifier '{identifier}' was not found in Vault."
                    )
                );
            }

            return Result<T, Error>.Success(foundValue!);
        }
    }
}