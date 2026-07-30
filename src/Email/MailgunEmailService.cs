using System.Net.Http.Headers;
using System.Text;
using ArturRios.Output;
using Microsoft.Extensions.Logging;

namespace ArturRios.Messaging.Email;

/// <summary>
/// Sends transactional e-mails through the <see href="https://www.mailgun.com/">Mailgun</see> HTTP API.
/// </summary>
/// <remarks>
/// Configuration is read from environment variables at call time:
/// <c>MAILGUN_API_KEY</c>, <c>MAILGUN_DOMAIN</c> and the optional <c>MAILGUN_API_VERSION</c>.
/// </remarks>
public class MailgunEmailService : IEmailService
{
    private readonly ILogger<MailgunEmailService> _logger;
    private readonly HttpClient _httpClient;
    private const string MailgunApiBaseUrl = "https://api.mailgun.net";
    private const string MailgunMessagesEndpoint = "messages";

    /// <summary>
    /// Name of the environment variable holding the Mailgun private API key.
    /// </summary>
    public const string ApiKeyVariable = "MAILGUN_API_KEY";

    /// <summary>
    /// Name of the environment variable holding the Mailgun sending domain.
    /// </summary>
    public const string DomainVariable = "MAILGUN_DOMAIN";

    /// <summary>
    /// Name of the environment variable holding the Mailgun API version to target.
    /// </summary>
    public const string ApiVersionVariable = "MAILGUN_API_VERSION";

    /// <summary>
    /// API version used when <see cref="ApiVersionVariable"/> is not set or is blank.
    /// </summary>
    public const string DefaultMailgunApiVersion = "v3";

    /// <summary>
    /// Initializes a new instance of the <see cref="MailgunEmailService"/> class.
    /// </summary>
    /// <param name="logger">Logger used to record request and response details.</param>
    /// <param name="httpClient">
    /// HTTP client used to reach the Mailgun API. When <see langword="null"/>, a new <see cref="HttpClient"/> is created.
    /// </param>
    public MailgunEmailService(ILogger<MailgunEmailService> logger, HttpClient? httpClient = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <summary>
    /// Sends a plain text e-mail through the Mailgun API.
    /// </summary>
    /// <param name="to">Recipient e-mail address.</param>
    /// <param name="subject">Subject line of the e-mail.</param>
    /// <param name="body">Plain text body of the e-mail.</param>
    /// <returns>
    /// A <see cref="ProcessOutput"/> without errors when Mailgun accepts the message, or carrying the status code and
    /// response body when it does not.
    /// </returns>
    public async Task<ProcessOutput> SendEmailAsync(string to, string subject, string body)
    {
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyVariable);
        var domain = Environment.GetEnvironmentVariable(DomainVariable);
        var apiVersion = GetApiVersion();

        var byteArray = Encoding.ASCII.GetBytes($"api:{apiKey}");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

        var content = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("from", $"Mailgun Sandbox <postmaster@{domain}>"),
            new KeyValuePair<string, string>("to", to),
            new KeyValuePair<string, string>("subject", subject),
            new KeyValuePair<string, string>("text", body)
        ]);

        _logger.LogInformation("Testing Mailgun email service...");

        var response =
            await _httpClient.PostAsync($"{MailgunApiBaseUrl}/{apiVersion}/{domain}/{MailgunMessagesEndpoint}",
                content);
        var responseContent = await response.Content.ReadAsStringAsync();

        _logger.LogInformation("Mailgun response: {ResponseContent}", responseContent);

        var output = new ProcessOutput();

        if (!response.IsSuccessStatusCode)
        {
            output.AddError(
                $"Failed to send e-mail via Mailgun. Status Code: {response.StatusCode} | Response: {responseContent}");
        }

        return output;
    }

    /// <summary>
    /// Reads the Mailgun API version from the environment.
    /// </summary>
    /// <returns>
    /// The value of <see cref="ApiVersionVariable"/>, or <see cref="DefaultMailgunApiVersion"/> when it is unset or blank.
    /// </returns>
    private static string GetApiVersion()
    {
        var apiVersion = Environment.GetEnvironmentVariable(ApiVersionVariable);

        return string.IsNullOrWhiteSpace(apiVersion) ? DefaultMailgunApiVersion : apiVersion;
    }
}
