using ArturRios.Output;

namespace ArturRios.Messaging.Email;

public interface IEmailService
{
    Task<ProcessOutput> SendEmailAsync(string to, string subject, string body);
}
