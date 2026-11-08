using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Email;

namespace Userorg.Adapter.Email;

/// <summary>
/// <see cref="IEmailSender"/> over the shared mail-sender HTTP service (the same
/// <c>api/Email/Html</c> templated endpoint the legacy stack posts to). Best-effort per the port's
/// contract: a transport failure is logged but never thrown.
/// </summary>
internal sealed class HttpEmailSender(
    IHttpClientFactory httpClientFactory,
    IOptions<EmailSenderOptions> options,
    ILogger<HttpEmailSender> logger) : IEmailSender
{
    internal const string HttpClientName = "UserOrgEmailSender";
    private const string HtmlEndpoint = "api/Email/Html";

    private readonly EmailSenderOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var requestBody = new
        {
            Sender = _options.SenderEmail,
            _options.Subject,
            message.Receivers,
            message.TemplateName,
            CC = message.Cc,
            message.TemplateArguments,
            message.Priority,
        };

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(HtmlEndpoint, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "Sent '{Template}' email to {ReceiverCount} receiver(s); mail service responded {StatusCode}.",
                    message.TemplateName, message.Receivers.Count, (int)response.StatusCode);
            }
            else
            {
                logger.LogWarning(
                    "Mail service rejected '{Template}' email to {ReceiverCount} receiver(s) with {StatusCode}.",
                    message.TemplateName, message.Receivers.Count, (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to send '{Template}' email.", message.TemplateName);
        }
    }
}