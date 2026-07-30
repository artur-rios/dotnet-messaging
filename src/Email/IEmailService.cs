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
}
