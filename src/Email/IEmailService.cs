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
}
