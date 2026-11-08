using Microsoft.Extensions.Logging;

namespace Userorg.Adapter.Waldur;

internal static partial class LogMessages
{


    [LoggerMessage(1, LogLevel.Information, "Waldur Integration {WaldurIntegrationName}: Creating Waldur service account: {Email}")]
    internal static partial void CreatingWaldurServiceAccount(this ILogger logger, string waldurIntegrationName, string email);


    [LoggerMessage(2, LogLevel.Information, "Waldur Integration {WaldurIntegrationName}: Creating ProjectDomainType: {Name}")]
    internal static partial void CreatingProjectDomainType(this ILogger logger, string waldurIntegrationName, string name);

    [LoggerMessage(3, LogLevel.Warning, "Waldur Integration {WaldurIntegrationName}: Location not found for endpoint: {Endpoint}")]
    internal static partial void LocationNotFoundForEndpoint(this ILogger logger, string waldurIntegrationName, string? endpoint);

    [LoggerMessage(4, LogLevel.Warning, "Waldur Integration {WaldurIntegrationName}: AllocationUnitType not found for name: {Name}")]
    internal static partial void AllocationUnitTypeNotFound(this ILogger logger, string waldurIntegrationName, string? name);


    [LoggerMessage(5, LogLevel.Warning, "Waldur Integration {WaldurIntegrationName}: Country not found for name: {Name}")]
    internal static partial void CountryNotFound(this ILogger logger, string waldurIntegrationName, string? name);

    [LoggerMessage(6, LogLevel.Warning, "Waldur Integration {WaldurIntegrationName}: LocationType not found for name: {Name}")]
    internal static partial void LocationTypeNotFound(this ILogger logger, string waldurIntegrationName, string? name);


    [LoggerMessage(7, LogLevel.Warning, "Waldur Integration {WaldurIntegrationName}: Unknown Waldur role '{WaldurRole}' for user {UserName} in project {ExternalProjectId}, defaulting to ProjectMember")]
    internal static partial void UnknownWaldurRole(this ILogger logger, string waldurIntegrationName, string? waldurRole, string? userName, string externalProjectId);


    [LoggerMessage(8, LogLevel.Debug, "Waldur Integration {WaldurIntegrationName}: Country not found for name {Name}")]
    internal static partial void CountryNotFoundDebug(this ILogger logger, string waldurIntegrationName, string? name);

    [LoggerMessage(9, LogLevel.Debug, "Waldur Integration {WaldurIntegrationName}: Location type not found for name {Name}")]
    internal static partial void LocationTypeNotFoundDebug(this ILogger logger, string waldurIntegrationName, string? name);

    [LoggerMessage(10, LogLevel.Debug, "Waldur Integration {WaldurIntegrationName}: Multiple locations found for endpoint {Endpoint} and service {ServiceName}")]
    internal static partial void MultipleLocationsFoundForEndpointAndServiceDebug(this ILogger logger, string waldurIntegrationName, string? endpoint, string? serviceName);

    [LoggerMessage(11, LogLevel.Debug, "Waldur Integration {WaldurIntegrationName}: Multiple locations found for endpoint {Endpoint}")]
    internal static partial void MultipleLocationsFoundForEndpointDebug(this ILogger logger, string waldurIntegrationName, string? endpoint);

    [LoggerMessage(12, LogLevel.Debug, "Waldur Integration {WaldurIntegrationName}: Location not found for endpoint {Endpoint}")]
    internal static partial void LocationNotFoundForEndpointDebug(this ILogger logger, string waldurIntegrationName, string? endpoint);

    [LoggerMessage(13, LogLevel.Debug, "Waldur Integration {WaldurIntegrationName}: Location not found for endpoint {Endpoint} and provider {ProviderId}")]
    internal static partial void LocationNotFoundForEndpointAndProviderDebug(this ILogger logger, string waldurIntegrationName, string? endpoint, Guid providerId);

    [LoggerMessage(14, LogLevel.Debug, "Waldur Integration {WaldurIntegrationName}: Allocation unit type not found for name {Name}")]
    internal static partial void AllocationUnitTypeNotFoundDebug(this ILogger logger, string waldurIntegrationName, string? name);


    [LoggerMessage(15, LogLevel.Error, "Waldur Integration {WaldurIntegrationName}: Failed to save Waldur service account user to the database.")]
    internal static partial void FailedToSaveWaldurServiceAccount(this ILogger logger, Exception ex, string waldurIntegrationName);


    [LoggerMessage(16, LogLevel.Warning, "Waldur Integration {WaldurIntegrationName}: Skipped resource '{ResourceName}' for project {ProjectSlug}: {Reason}")]
    internal static partial void ResourceSkipped(this ILogger logger, string waldurIntegrationName, string? projectSlug, string? resourceName, string? reason);
}