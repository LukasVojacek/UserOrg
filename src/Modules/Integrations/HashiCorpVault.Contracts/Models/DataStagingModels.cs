using System.Diagnostics;
using System.Text.Json;

namespace HashiCorpVault.Contracts.Models;

[DebuggerDisplay("{Name}")]
public class DataStagingLocationKey : IVaultModel
{
    public string? BackupKey { get; set; }
    public string? Key { get; set; }
    public string? Name { get; set; }
    public string JsonData { get; set; } = string.Empty;
}

public sealed record DataStagingPart(string Name, string JsonData);

public sealed record DataStagingDataModel(string Name, DataStagingPart PublicData, DataStagingPart PrivateData);