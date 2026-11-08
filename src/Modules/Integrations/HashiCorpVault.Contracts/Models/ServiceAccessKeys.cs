using System.Diagnostics;

using UserOrg.Shared;

namespace HashiCorpVault.Contracts.Models;

[DebuggerDisplay("{Name}")]
public class ServiceAccessKeys : IVaultModel
{
    public static string VaultPath { get; } = "ServiceAccessKeys";

    public required string BackupKey { get; init; }
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required EServiceType ServiceType { get; init; }
    public string? Data { get; init; }
    public string ContactEmail { get; init; } = "";
}