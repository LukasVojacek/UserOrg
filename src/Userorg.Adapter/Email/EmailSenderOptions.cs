namespace Userorg.Adapter.Email;

/// <summary>
/// Mail settings for <see cref="HttpEmailSender"/>, bound to the shared <c>MailConfiguration</c>
/// section (the same section the legacy stack and Core's own notification sender use).
/// </summary>
internal sealed class EmailSenderOptions
{
    public const string ConfigurationPath = "MailConfiguration";

    public string? MailSenderUrl { get; set; }
    public string? MailSenderApiKey { get; set; }
    public string? SenderEmail { get; set; }
    public string Subject { get; set; } = "Project Assignment Request";
}