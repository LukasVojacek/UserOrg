using System.Diagnostics.CodeAnalysis;

namespace UserOrganizationManagement.Domain.Configuration;

public sealed class MailConfiguration
{
    public const string ConfigurationPath = "MailConfiguration";

    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "<Pending>")]
    public string? MailSenderUrl { get; set; }
    /// <summary>The key the emailsender service requires in the X-Api-Key header; blank sends no header.</summary>
    public string? MailSenderApiKey { get; set; }
    public string? SenderEmail { get; set; }
    public string? PortalAddress { get; set; }
}