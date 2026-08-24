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
    private const string MailgunApiBaseUrl = "https://api.mailgun.net";
    private const string MailgunMessagesEndpoint = "messages";

    /// <summary>
    /// The client used when the caller supplies none. One per process rather than one per service
    /// instance: a fresh <see cref="HttpClient"/> per instance holds its connections open after it goes
    /// out of scope, and nothing here ever disposed one.
    /// </summary>
    private static readonly Lazy<HttpClient> SharedClient = new(() => new HttpClient(), isThreadSafe: true);

    private readonly ILogger<MailgunEmailService> _logger;
    private readonly HttpClient _httpClient;

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
    /// HTTP client used to reach the Mailgun API. When <see langword="null"/>, a client shared by the
    /// whole process is used.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    public MailgunEmailService(ILogger<MailgunEmailService> logger, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _httpClient = httpClient ?? SharedClient.Value;
    }

    /// <summary>
    /// Sends a plain text e-mail through the Mailgun API.
    /// </summary>
    /// <param name="to">Recipient e-mail address.</param>
    /// <param name="subject">Subject line of the e-mail.</param>
    /// <param name="body">Plain text body of the e-mail.</param>
    /// <returns>
    /// A <see cref="ProcessOutput"/> without errors when Mailgun accepts the message, or carrying the status code and
    /// response body when it does not. A missing API key or sending domain is reported the same way rather
    /// than sent to Mailgun as an unauthenticated request.
    /// </returns>
    /// <remarks>
    /// The credential is attached to the request rather than to <see cref="HttpClient.DefaultRequestHeaders"/>.
    /// The client may be shared — the documented registration is <c>AddHttpClient</c>, which hands out a
    /// client this service does not own — and writing a credential onto its defaults both races with
    /// concurrent sends and leaves the Mailgun key attached to every later request that client makes.
    /// </remarks>
    public async Task<ProcessOutput> SendEmailAsync(string to, string subject, string body)
    {
        var output = new ProcessOutput();

        var apiKey = Environment.GetEnvironmentVariable(ApiKeyVariable);
        var domain = Environment.GetEnvironmentVariable(DomainVariable);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            output.AddError($"Cannot send e-mail via Mailgun: the {ApiKeyVariable} environment variable is not set.");
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            output.AddError($"Cannot send e-mail via Mailgun: the {DomainVariable} environment variable is not set.");
        }

        if (!output.Success)
        {
            _logger.LogError("Mailgun is not configured: {Errors}", string.Join(" ", output.Errors));

            return output;
        }

        var apiVersion = GetApiVersion();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{MailgunApiBaseUrl}/{apiVersion}/{domain}/{MailgunMessagesEndpoint}")
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{apiKey}")))
            },
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("from", $"Mailgun Sandbox <postmaster@{domain}>"),
                new KeyValuePair<string, string>("to", to),
                new KeyValuePair<string, string>("subject", subject),
                new KeyValuePair<string, string>("text", body)
            ])
        };

        _logger.LogInformation("Sending e-mail to {Recipient} via Mailgun...", to);

        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

        var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Mailgun accepted the message for {Recipient}.", to);

            return output;
        }

        _logger.LogError("Mailgun rejected the message for {Recipient}: {StatusCode} | {ResponseContent}",
            to, response.StatusCode, responseContent);

        output.AddError(
            $"Failed to send e-mail via Mailgun. Status Code: {response.StatusCode} | Response: {responseContent}");

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
