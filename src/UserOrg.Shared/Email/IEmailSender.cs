namespace UserOrg.Shared.Email;

/// <summary>
/// Sends templated emails. Implementations own the outbound transport and its failure handling.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Resolves the message's template and delivers it to its recipients. Best-effort: does not
    /// throw on delivery failure. Cancellation still propagates.
    /// </summary>
    /// <param name="message">Template, positional template arguments and recipients of the email.</param>
    /// <param name="cancellationToken">
    /// Cancels the send. A cancelled send may already have reached the transport.
    /// </param>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}