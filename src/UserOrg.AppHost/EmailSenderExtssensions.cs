namespace UserOrg.AppHost;

using Parameters = IDistributedApplicationBuilderExtensions.Parameters;

internal static class EmailSenderExtssensions
{
    public static IResourceBuilder<ContainerResource> AddEmailSender(this IDistributedApplicationBuilder builder, Parameters p)
    {

        var mailpit = builder.AddMailPit("mailpit")
            .WithUrlForEndpoint("http", static url =>
            {
                url.DisplayText = "📬 Mailpit";
                url.DisplayLocation = UrlDisplayLocation.SummaryAndDetails;
            })
            .WithUrlForEndpoint("smtp", static url =>
            {
                url.DisplayText = "📮 Mailpit SMTP";
                url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
            });

        var emailsender = builder.AddContainer("Emailsender", "emailsender")
            .WithImageRegistry("opencode.it4i.eu:5050/lexis-platform/backend")
            .WithLifetime(ContainerLifetime.Persistent)
            .WithEnvironment("MailSettings__Server", "mailpit")//mailpit.Resource.GetEndpoint("smtp"))
            .WithEnvironment("MailSettings__Port", "1025")
            .WithVolume("emailsender-database", "/opt/database")
            .WithEnvironment("ASPNETCORE_RUNTYPE_ENVIRONMENT", "Docker")
            .WithHttpEndpoint(targetPort: 8080)
            .WithUrlForEndpoint("http", static url =>
            {
                url.DisplayText = "✉️ Email sender API";
                url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
            })
            .WithImageTag("2.4.0")
            .WithEnvironment("Authorization__ApiKey", p.EmailSenderApiKey)
            .WithEnvironment("Authorization__BrowserToken", p.EmailSenderBrowserToken)
            .WithEnvironment("ConnectionStrings__Default", "Data Source=/opt/database/emailsQueueDev.db")
            .WithEnvironment("ApplicationSettings__TemplatePath", "/opt/emailTemplates/")
            .WithEnvironment("ApplicationSettings__MaxResendTries", "5")
            .WithEnvironment("ApplicationSettings__BackgroundSenderPeriodMinutes", "0.3")
            .WithEnvironment("MailSettings__UseSsl", "false")
            .WithEnvironment("MailSettings__UseAuthenticationToSmtp", "false")
            .WithEnvironment("IpAddressWhitelist__Sources__0__Description", "any")
            .WithEnvironment("IpAddressWhitelist__Sources__0__Ip", "*")
            .WithEnvironment("ApiDocumentation__Version", "v1.0.0")
            .WithEnvironment("ApiDocumentation__Title", "Email sender API")
            .WithEnvironment("ApiDocumentation__Description", "Email sender API")
            .WithEnvironment("ApiDocumentation__Host", "")
            .WithEnvironment("ApiDocumentation__HostPostfix", "")
            .WithEnvironment("ApiDocumentation__TermOfUsageUrl", "https://twitter.com/it4innovations")
            .WithEnvironment("ApiDocumentation__License", "GNU General Public License v3.0")
            .WithEnvironment("ApiDocumentation__LicenseUrl", "https://www.gnu.org/licenses/gpl-3.0.html")
            .WithEnvironment("ApiDocumentation__ContactName", "IT4Innovations")
            .WithEnvironment("ApiDocumentation__ContactEmail", "")
            .WithEnvironment("ApiDocumentation__ContactUrl", "https://twitter.com/it4innovations")
            .WithEmailSenderLogging();

        mailpit.WithParentRelationship(emailsender);
        return emailsender;
    }

    private static IResourceBuilder<ContainerResource> WithEmailSenderLogging(this IResourceBuilder<ContainerResource> container)
    {
        if (!container.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            return container;
        }

        return container
            .WithEnvironment("SerilogStepUp__Mode", "Auto")
            .WithEnvironment("SerilogStepUp__BaseLevel", "Information")
            .WithEnvironment("SerilogStepUp__StepUpLevel", "Debug")
            .WithEnvironment("SerilogStepUp__EnableConsoleLogging", "true")
            .WithEnvironment("SerilogStepUp__EnableOtlpExporter", "false")
            .WithEnvironment("SerilogStepUp__ExcludePaths__0", "/health");
    }
}