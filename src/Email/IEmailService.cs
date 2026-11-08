using ArturRios.Output;

namespace ArturRios.Messaging.Email;

/// <summary>
/// Defines an e-mail delivery service.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends a plain text e-mail.
    /// </summary>
    /// <param name="to">Recipient e-mail address.</param>
    /// <param name="subject">Subject line of the e-mail.</param>
    /// <param name="body">Plain text body of the e-mail.</param>
    /// <returns>A <see cref="ProcessOutput"/> describing whether the e-mail was accepted for delivery.</returns>
    Task<ProcessOutput> SendEmailAsync(string to, string subject, string body);

    /// <summary>
    /// Sends a plain text e-mail, observing <paramref name="cancellationToken"/>.
    /// </summary>
    /// <param name="to">Recipient e-mail address.</param>
    /// <param name="subject">Subject line of the e-mail.</param>
    /// <param name="body">Plain text body of the e-mail.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A <see cref="ProcessOutput"/> describing whether the e-mail was accepted for delivery.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <remarks>
    /// The default implementation, for implementations written before this overload existed, only checks the token
    /// before delegating to <see cref="SendEmailAsync(string, string, string)"/>. <see cref="MailgunEmailService"/>
    /// passes it on to the HTTP request.
    /// </remarks>
    Task<ProcessOutput> SendEmailAsync(string to, string subject, string body, CancellationToken cancellationToken)
        => cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<ProcessOutput>(cancellationToken)
            : SendEmailAsync(to, subject, body);

    /// <summary>
    /// Sends a plain text e-mail with the sender and reply-to <paramref name="message"/> carries.
    /// </summary>
    /// <param name="message">The e-mail to send.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A <see cref="ProcessOutput"/> describing whether the e-mail was accepted for delivery.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <remarks>
    /// The default implementation, for implementations written before this overload existed, returns a failed
    /// output when the message sets a sender or a reply-to, rather than sending it without them. A message that sets
    /// neither goes to <see cref="SendEmailAsync(string, string, string, CancellationToken)"/>.
    /// </remarks>
    Task<ProcessOutput> SendEmailAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.From is null && message.ReplyTo is null)
        {
            return SendEmailAsync(message.To, message.Subject, message.Body, cancellationToken);
        }

        var output = new ProcessOutput();

        output.AddError("This e-mail service does not support a sender or a reply-to per message.");

        return Task.FromResult(output);
    }
}
