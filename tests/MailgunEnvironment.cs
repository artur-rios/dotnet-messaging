using ArturRios.Messaging.Email;

namespace ArturRios.Messaging.Tests;

/// <summary>
/// Sets every Mailgun environment variable the service reads for one test, and puts back what the process had
/// when the test is done. A variable the test does not name is cleared, so neither a <c>MAILGUN_*</c> variable of the
/// developer's shell nor one another test class left behind can change what a test sees. A test may still change a
/// variable itself; disposing restores it too.
/// </summary>
/// <remarks>
/// The variables are process-wide, which is why every class that uses this belongs to
/// <see cref="MailgunEnvironmentCollection"/>.
/// </remarks>
public sealed class MailgunEnvironment : IDisposable
{
    private static readonly string[] Variables =
    [
        MailgunEmailService.ApiKeyVariable,
        MailgunEmailService.DomainVariable,
        MailgunEmailService.ApiVersionVariable,
        MailgunEmailService.FromVariable
    ];

    private readonly Dictionary<string, string?> _previous =
        Variables.ToDictionary(name => name, Environment.GetEnvironmentVariable);

    public MailgunEnvironment(string? apiKey = null, string? domain = null, string? apiVersion = null,
        string? from = null)
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, apiKey);
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, domain);
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiVersionVariable, apiVersion);
        Environment.SetEnvironmentVariable(MailgunEmailService.FromVariable, from);
    }

    public void Dispose()
    {
        foreach (var (name, value) in _previous)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
