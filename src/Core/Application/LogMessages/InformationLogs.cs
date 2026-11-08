using Microsoft.Extensions.Logging;

namespace UserOrganizationManagement.Application.LogMessages;

public static partial class InformationLogs
{
    [LoggerMessage(EventId = (int)FeatureEventId.Migration, Level = LogLevel.Information, Message = "Starting migration and seeding into the new database.")]
    public static partial void MigrationStarting(this ILogger logger);

    [LoggerMessage(EventId = (int)FeatureEventId.Migration, Level = LogLevel.Information, Message = "Application and database migrations are same. Starting seeding data into database.")]
    public static partial void ValidMigration(this ILogger logger);

    [LoggerMessage(EventId = (int)FeatureEventId.Keycloak, Level = LogLevel.Information, Message = "User created {userName}")]
    public static partial void UserCreated(this ILogger logger, string userName);

    [LoggerMessage(EventId = (int)FeatureEventId.Keycloak, Level = LogLevel.Information, Message = "User updated {userName}")]
    public static partial void UserUpdated(this ILogger logger, string userName);

    [LoggerMessage(EventId = (int)FeatureEventId.Endpoint, Level = LogLevel.Information, Message = "HTTP request Information: (\"Schema\":{scheme}, \"Host\": {host}, \"Path\": {path}, \"QueryString\": {queryString}, \"Body\": {body}, \"Content-Length\": {contentLength})")]
    public static partial void HttpRequest(this ILogger logger, string scheme, string host, string path, string queryString, long? contentLength, string body);
}