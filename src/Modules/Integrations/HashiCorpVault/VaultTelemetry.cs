using System.Diagnostics;

namespace HashiCorpVault;

internal static class VaultTelemetry
{
    public const string ActivitySourceName = "UserOrg.Integrations.HashiCorpVault";
    public const string Version = "1.0.0";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);
}