namespace HashiCorpVault;

public class VaultOptions
{
    public const string Section = "VaultCredentials";
    public string MountPoint { get; set; } = "UserOrg_kv";
    public string? AddressPrefix { get; set; }


    public string BuildVaultDataPath(string secretPath)
    {

        if (string.IsNullOrWhiteSpace(MountPoint))
        {
            throw new InvalidOperationException(
                $"VaultOptions.MountPoint must not be null or whitespace. Actual value: '{MountPoint ?? "null"}'.");
        }

        // Build base paths. If AddressPrefix is set, include it before mountpoint.
        var prefix = string.IsNullOrWhiteSpace(AddressPrefix)
            ? string.Empty
            : AddressPrefix!.Trim('/');
        var mount = MountPoint.Trim('/');

        var dataBasePath = string.IsNullOrEmpty(prefix)
            ? $"/v1/{mount}/data"
            : $"/v1/{mount}/data/{prefix}";

        if (string.IsNullOrEmpty(secretPath))
        {
            return dataBasePath;
        }

        var normalizedSecretPath = secretPath.TrimStart('/');
        return $"{dataBasePath}/{normalizedSecretPath}";
    }
}