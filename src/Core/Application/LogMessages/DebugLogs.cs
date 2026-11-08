using Microsoft.Extensions.Logging;

using UserOrganizationManagement.Domain.Authorization;

namespace UserOrganizationManagement.Application.LogMessages;

public static partial class DebugLogs
{
    [LoggerMessage(EventId = (int)FeatureEventId.Endpoint, Level = LogLevel.Debug, Message = "EndpointInformation populated. Values: {endpointInformation}")]
    public static partial void EndpointInformationPopulated(this ILogger logger, IList<EndpointInformation> endpointInformation);

    [LoggerMessage(EventId = (int)FeatureEventId.Keycloak, Level = LogLevel.Debug, Message = "Request from {consumer}.")]
    public static partial void RequestFrom(this ILogger logger, string? consumer);

}