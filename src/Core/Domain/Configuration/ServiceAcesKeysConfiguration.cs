namespace UserOrganizationManagement.Domain.Configuration;

public sealed class ServiceAcesKeysConfiguration
{
    public const string ConfigurationPath = "ServiceAccesKeys";

    public IList<ServiceAccesKey>? Consumers { get; }
}